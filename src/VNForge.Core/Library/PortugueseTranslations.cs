using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>
/// O que o VNDB tem de tradução em português de um jogo, da melhor para a pior: <see cref="Complete"/> (edição completa em português, que não é tradução automática),
/// <see cref="Partial"/> (só edição parcial), <see cref="MachineOnly"/> (só tradução automática, que não conta como tradução) e <see cref="None"/>.
/// </summary>
public enum PortugueseStatus
{
    None,
    MachineOnly,
    Partial,
    Complete,
}

/// <summary>O resultado de olhar as edições de um jogo: o estado e os ids das edições em português que o definiram.</summary>
public sealed record PortugueseFinding(PortugueseStatus Status, IReadOnlyList<string> ReleaseIds);

/// <summary>A conferência que a biblioteca guarda no jogo. <paramref name="VisualNovelId"/> diz de qual jogo do VNDB ela é, e <paramref name="CheckedAt"/> quando o VNDB foi consultado.</summary>
public sealed record LibraryPortuguese(string VisualNovelId, PortugueseStatus Status, DateTimeOffset CheckedAt, IReadOnlyList<string> ReleaseIds);

/// <summary>Lê as edições de um jogo do VNDB e diz se já existe tradução em português. Português do Brasil (<c>pt-br</c>) e de Portugal (<c>pt-pt</c>) contam.</summary>
public static class PortugueseTranslations
{
    private static readonly string[] Languages = ["pt-br", "pt-pt", "pt"];

    /// <summary>
    /// Olha as edições que ligam ao jogo <paramref name="visualNovelId"/>. Edição de teste (<c>trial</c>) e edição de outro jogo não contam; a tradução automática
    /// só vale como <see cref="PortugueseStatus.MachineOnly"/>.
    /// </summary>
    public static PortugueseFinding Classify(string visualNovelId, IEnumerable<VndbRelease> releases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visualNovelId);
        ArgumentNullException.ThrowIfNull(releases);
        var found = new List<(PortugueseStatus Status, string Id)>();
        foreach (var release in releases)
        {
            if (release.VisualNovels.FirstOrDefault(v => v.VisualNovelId == visualNovelId) is not { } link || link.ReleaseType == "trial")
            {
                continue;
            }

            foreach (var language in release.Languages.Where(l => Languages.Contains(l.Language, StringComparer.OrdinalIgnoreCase)))
            {
                var status = language.MachineTranslated ? PortugueseStatus.MachineOnly : link.ReleaseType == "partial" ? PortugueseStatus.Partial : PortugueseStatus.Complete;
                found.Add((status, release.Id));
            }
        }

        if (found.Count == 0)
        {
            return new PortugueseFinding(PortugueseStatus.None, []);
        }

        var best = found.Max(f => f.Status);
        return new PortugueseFinding(best, [.. found.Where(f => f.Status == best).Select(f => f.Id).Distinct(StringComparer.Ordinal)]);
    }
}

/// <summary>Um jogo cuja conferência falhou e por quê.</summary>
public sealed record TranslationCheckFailure(LibraryGame Game, string Message);

/// <summary>O andamento da conferência: quantos jogos já foram vistos (com ou sem falha), de quantos, e o último que terminou.</summary>
public sealed record TranslationCheckProgress(int Done, int Total, LibraryGame Game);

/// <summary>
/// O resultado de um lote de conferências. <paramref name="Checked"/> são os jogos que ganharam resposta, <paramref name="Incomplete"/> os que o VNDB tem edições demais
/// para listar e nenhuma delas é tradução completa (não dá para afirmar que não há), e <paramref name="AbortReason"/> diz por que parou antes do fim.
/// </summary>
public sealed record TranslationCheckRun(
    IReadOnlyList<LibraryGame> Checked,
    IReadOnlyList<LibraryGame> Incomplete,
    IReadOnlyList<TranslationCheckFailure> Failures,
    bool Cancelled,
    string? AbortReason);

/// <summary>
/// Consulta as edições do VNDB dos jogos da biblioteca para saber se já existe tradução em português e guarda a resposta em <see cref="LibraryGame.Portuguese"/>.
/// Só consulta o que a fila de tradução usa: jogo com jogo do VNDB confirmado, engine que o app traduz e sem projeto ainda.
/// </summary>
public sealed class PortugueseTranslationChecker(IVndbCatalog catalog, TimeProvider? time = null)
{
    /// <summary>Falhas seguidas do VNDB que param o lote (rede fora, limite de pedidos).</summary>
    public const int MaxConsecutiveFailures = 3;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Confere os jogos que ainda não têm resposta (ou todos, com <paramref name="again"/>) e vai trocando-os na <paramref name="library"/>.
    /// </summary>
    /// <param name="onChecked">Chamada a cada jogo conferido, logo depois de trocá-lo na <paramref name="library"/>: quem grava o arquivo o faz aqui, jogo a jogo.</param>
    public async Task<TranslationCheckRun> CheckAllAsync(
        GameLibrary library,
        bool again = false,
        IProgress<TranslationCheckProgress>? progress = null,
        Action<LibraryGame>? onChecked = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        var queue = library.Games.Where(g => NeedsCheck(g, again)).ToList();
        List<LibraryGame> checkedGames = [];
        List<LibraryGame> incomplete = [];
        List<TranslationCheckFailure> failures = [];
        var consecutive = 0;
        var cancelled = false;
        string? abort = null;
        for (var i = 0; i < queue.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            try
            {
                var releases = await catalog.GetReleasesAsync(queue[i].VndbId!, cancellationToken).ConfigureAwait(false);
                consecutive = 0;
                var finding = PortugueseTranslations.Classify(queue[i].VndbId!, releases.Items);
                if (releases.Truncated && finding.Status != PortugueseStatus.Complete)
                {
                    incomplete.Add(queue[i]);
                }
                else
                {
                    var updated = queue[i] with { Portuguese = new LibraryPortuguese(queue[i].VndbId!, finding.Status, _time.GetUtcNow(), finding.ReleaseIds) };
                    library.Replace(updated);
                    onChecked?.Invoke(updated);
                    checkedGames.Add(updated);
                }

                progress?.Report(new TranslationCheckProgress(i + 1, queue.Count, queue[i]));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (VndbException e)
            {
                failures.Add(new TranslationCheckFailure(queue[i], e.Message));
                progress?.Report(new TranslationCheckProgress(i + 1, queue.Count, queue[i]));
                consecutive++;
                if (consecutive >= MaxConsecutiveFailures)
                {
                    abort = $"O VNDB falhou {consecutive} vezes seguidas; parei. Último erro: {e.Message}";
                    break;
                }
            }
        }

        return new TranslationCheckRun(checkedGames, incomplete, failures, cancelled, abort);
    }

    /// <summary>O jogo é daqueles que a fila precisa conferir: o resultado dele muda o lugar na fila.</summary>
    public static bool NeedsCheck(LibraryGame game, bool again = false)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.IsConfirmed && game.ProjectPath is null && game.Engine.Translatable && (again || game.CurrentPortuguese is null);
    }
}
