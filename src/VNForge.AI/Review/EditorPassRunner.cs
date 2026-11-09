using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Review;

/// <summary>O que a passada de editor deixou numa cena.</summary>
/// <param name="Suggestions">Os ajustes que passaram na conferência, na ordem das linhas.</param>
/// <param name="LinesRead">Quantas linhas traduzidas o modelo leu.</param>
/// <param name="Discarded">Quantos ajustes do modelo não valeram: linha desconhecida ou já aprovada, texto igual ao de hoje, vazio ou com marcadores de controle diferentes.</param>
public sealed record EditorPassResult(IReadOnlyList<EditorSuggestion> Suggestions, int LinesRead, int Discarded);

/// <summary>
/// Roda a passada de editor numa cena: manda ao <see cref="IEditorPassProvider"/> as linhas traduzidas (a cena inteira num pedido só, ou em fatias de <see cref="MaxLinesPerRequest"/>
/// quando ela é longa demais) e confere cada ajuste antes de virar <see cref="EditorSuggestion"/>. Nunca mexe na tradução da linha.
/// </summary>
public sealed class EditorPassRunner(IEditorPassProvider provider, TranslationBatchBuilder builder, TimeProvider? clock = null)
{
    /// <summary>Linhas por pedido. Uma cena de YU-RIS raramente passa disso; acima, o pedido seguinte leva as falas da fatia anterior como contexto.</summary>
    public const int MaxLinesPerRequest = 200;

    private readonly IEditorPassProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly TranslationBatchBuilder _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Lê a cena <paramref name="scriptKey"/>, cujas linhas são <paramref name="units"/>, e devolve os ajustes. Linhas sem tradução não entram.</summary>
    public async Task<EditorPassResult> RunSceneAsync(string scriptKey, IReadOnlyList<TextUnit> units, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(scriptKey);
        ArgumentNullException.ThrowIfNull(units);

        var lines = units.Where(u => u.HasTranslation).ToList();
        var now = _clock.GetUtcNow();
        List<EditorSuggestion> suggestions = [];
        var discarded = 0;
        for (var start = 0; start < lines.Count; start += MaxLinesPerRequest)
        {
            ct.ThrowIfCancellationRequested();
            var slice = lines.Skip(start).Take(MaxLinesPerRequest).ToList();
            var scene = _builder.Build(slice, previous: lines.Take(start).ToList());
            var proposals = await _provider.ReviewSceneAsync(scene, ct).ConfigureAwait(false);
            discarded += Collect(scriptKey, slice, proposals, now, suggestions);
        }

        return new EditorPassResult(suggestions, lines.Count, discarded);
    }

    /// <summary>
    /// "Outra": pede ao provedor uma proposta nova para a linha <paramref name="unitId"/> da cena, com as falas em volta como contexto. Nula quando a linha não existe, está aprovada,
    /// o modelo não propõe nada ou o que ele propõe não vale (igual ao texto de hoje, igual à recusada, vazio ou com marcadores diferentes).
    /// </summary>
    public async Task<EditorSuggestion?> AnotherAsync(string scriptKey, IReadOnlyList<TextUnit> units, string unitId, string rejected, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(scriptKey);
        ArgumentNullException.ThrowIfNull(units);
        var lines = units.Where(u => u.HasTranslation).ToList();
        var index = lines.FindIndex(u => string.Equals(u.Id, unitId, StringComparison.Ordinal));
        if (index < 0)
        {
            return null;
        }

        var unit = lines[index];
        var batch = _builder.Build([unit], previous: lines.Take(index).ToList(), next: lines.Skip(index + 1).ToList());
        var proposal = await _provider.AnotherAsync(batch, rejected, ct).ConfigureAwait(false);
        if (proposal is null || !string.Equals(proposal.Id, unit.Id, StringComparison.Ordinal) || !IsValid(unit, proposal) || string.Equals(proposal.Proposed.Trim(), rejected.Trim(), StringComparison.Ordinal))
        {
            return null;
        }

        return new EditorSuggestion(scriptKey, unit.Id, proposal.Kind, unit.Translation!, proposal.Proposed.Trim(), proposal.Reason.Trim(), _clock.GetUtcNow());
    }

    private static int Collect(string scriptKey, List<TextUnit> slice, IReadOnlyList<EditorPassProposal> proposals, DateTimeOffset now, List<EditorSuggestion> into)
    {
        var byId = slice.ToDictionary(u => u.Id, StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<(int Index, EditorSuggestion Suggestion)>();
        var discarded = 0;
        foreach (var proposal in proposals)
        {
            if (!byId.TryGetValue(proposal.Id, out var unit) || !IsValid(unit, proposal) || !taken.Add(proposal.Id))
            {
                discarded++;
                continue;
            }

            found.Add((slice.IndexOf(unit), new EditorSuggestion(scriptKey, unit.Id, proposal.Kind, unit.Translation!, proposal.Proposed.Trim(), proposal.Reason.Trim(), now)));
        }

        into.AddRange(found.OrderBy(f => f.Index).Select(f => f.Suggestion));
        return discarded;
    }

    private static bool IsValid(TextUnit unit, EditorPassProposal proposal)
    {
        var proposed = proposal.Proposed.Trim();
        return unit.Status != TranslationStatus.Approved
            && !string.IsNullOrEmpty(proposed)
            && !string.IsNullOrWhiteSpace(proposal.Reason)
            && !string.Equals(proposed, unit.Translation, StringComparison.Ordinal)
            && TranslationResponseValidator.DescribeMarkerDifference(unit.Translation!, proposed) is null;
    }
}
