using VNForge.AI.Claude;
using VNForge.AI.Translation;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.App.Services;

/// <summary>As duas opções de uma reescrita de fala.</summary>
/// <param name="Options">De uma a duas versões da tradução, com os marcadores do original e diferentes da tradução de hoje.</param>
/// <param name="FromFakeProvider">Veio do provedor falso (app sem chave de API), não do Claude.</param>
/// <param name="Model">O id do modelo que respondeu; nulo no provedor falso.</param>
/// <param name="Effort">O esforço pedido ao modelo, em português; nulo no provedor falso.</param>
public sealed record LineRewrite(IReadOnlyList<string> Options, bool FromFakeProvider, string? Model = null, string? Effort = null);

/// <summary>Pede à IA uma reescrita dirigida da tradução de uma fala. Atrás de uma interface para os painéis rodarem nos testes sem rede.</summary>
public interface ILineRewriter
{
    /// <summary>Duas versões da tradução <paramref name="current"/> na direção pedida, com o "Como fala" do falante e a caixa do jogo quando se sabe.</summary>
    /// <param name="speaker">O falante da linha no Glossário, com o "Como fala" no tom; nulo na narração e em quem não está cadastrado.</param>
    /// <param name="glossary">Os termos do glossário que aparecem no original da linha.</param>
    /// <param name="box">A caixa do jogo; nula sem perfil de formatação.</param>
    /// <exception cref="LineSuggestionException">Recusa do modelo ou resposta sem uma versão aproveitável.</exception>
    Task<LineRewrite> RewriteAsync(TextUnit unit, RewriteDirection direction, string current, CharacterInfo? speaker, IReadOnlyList<GlossaryTerm> glossary, WrapRule? box, CancellationToken ct);
}

public sealed partial class LineSuggester : ILineRewriter
{
    /// <summary>
    /// Pede as duas versões pelo mesmo provedor da sugestão. Sem chave de API usa o <see cref="FakeTranslationProvider"/>, como a sugestão; um provedor que
    /// não sabe reescrever é erro. As versões que perdem ou trocam um marcador do original, repetem a tradução de hoje ou repetem uma à outra saem da lista.
    /// </summary>
    public async Task<LineRewrite> RewriteAsync(TextUnit unit, RewriteDirection direction, string current, CharacterInfo? speaker, IReadOnlyList<GlossaryTerm> glossary, WrapRule? box, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(glossary);

        var real = await Task.Run(openProvider, ct).ConfigureAwait(false);
        IRewriteProvider rewriter = real is null ? new FakeTranslationProvider() : real as IRewriteProvider
            ?? throw new LineSuggestionException("O provedor de IA atual não sabe reescrever falas.");
        var batch = new TranslationBatch([unit])
        {
            Characters = speaker is null ? [] : [speaker],
            Glossary = [.. glossary.Select(g => new GlossaryEntry(g.Source, g.Target, g.Notes))],
        };
        var request = box is { MaxLines: not null } ? new RewriteRequest(direction, current, box.MaxLines, box.MaxWidth) : new RewriteRequest(direction, current);
        var result = await rewriter.RewriteAsync(batch, request, ct).ConfigureAwait(false);
        if (result.Refused)
        {
            throw new LineSuggestionException("A IA recusou reescrever esta fala. Edite o texto à mão.");
        }

        var options = KeepUsable(batch, result, current);
        if (options.Count == 0)
        {
            throw new LineSuggestionException("A IA não devolveu uma versão aproveitável. Tente de novo ou edite o texto à mão.");
        }

        return real is null
            ? new LineRewrite(options, FromFakeProvider: true)
            : new LineRewrite(options, FromFakeProvider: false, currentModel?.Invoke() is { Length: > 0 } model ? model : null, Effort: "baixo");
    }

    private static List<string> KeepUsable(TranslationBatch batch, RewriteResult result, string current)
    {
        var kept = new List<string>();
        foreach (var option in result.Options)
        {
            var marked = TranslationResponseValidator.Validate(batch, [new TranslationResult(result.Id, option)]);
            if (marked.Valid.Count == 1 && option != current && !kept.Contains(option))
            {
                kept.Add(option);
            }
        }

        return kept;
    }
}
