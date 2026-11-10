using System.Globalization;

namespace VNForge.Core.Library;

/// <summary>Onde o jogo está na fila de tradução. A ordem dos valores é a ordem da fila.</summary>
public enum TranslationQueueState
{
    /// <summary>Falta traduzir e o app sabe como: é o que a fila mostra.</summary>
    Queued,

    /// <summary>Já tem projeto de tradução.</summary>
    InProgress,

    /// <summary>O VNDB já tem tradução completa em português.</summary>
    AlreadyTranslated,

    /// <summary>A engine do jogo é de uma que o app ainda não traduz.</summary>
    UnsupportedEngine,
}

/// <summary>
/// Um jogo na fila de tradução. <paramref name="Portuguese"/> é nulo enquanto não se sabe: o jogo não tem jogo do VNDB confirmado, ou a conferência ainda não rodou.
/// <paramref name="ApproximateMinutes"/> é o tamanho do texto, pela duração do VNDB; nulo quando o VNDB não tem duração.
/// </summary>
public sealed record TranslationQueueEntry(LibraryGame Game, TranslationQueueState State, PortugueseStatus? Portuguese, int? ApproximateMinutes);

/// <summary>
/// A fila de tradução da biblioteca: o que o usuário tem, ainda não existe em português no VNDB e está numa engine que o app traduz. Primeiro vem o que não tem
/// nenhuma tradução, depois o que só tem automática, depois o parcial, depois o que ainda não se sabe; dentro de cada grupo o texto mais curto vem na frente,
/// porque é o que se termina antes. Jogo com projeto, já traduzido ou de engine sem suporte vai para o fim, nessa ordem, para a lista inteira ainda explicar cada jogo.
/// </summary>
public static class TranslationQueue
{
    /// <summary>
    /// Duração de uma categoria do VNDB (1 a 5) em minutos, pelo meio da faixa: menos de 2 horas, 2 a 10, 10 a 30, 30 a 50 e mais de 50. Só entra quando o VNDB
    /// não tem a duração votada em minutos.
    /// </summary>
    private static readonly int[] CategoryMinutes = [60, 360, 1200, 2400, 3600];

    /// <summary>Todos os jogos da biblioteca, já na ordem da fila. Com <paramref name="privacy"/>, o jogo que o <see cref="AdultMode.Hide"/> tira da Biblioteca fica de fora.</summary>
    public static IReadOnlyList<TranslationQueueEntry> Build(IEnumerable<LibraryGame> games, LibraryPrivacy? privacy = null)
    {
        ArgumentNullException.ThrowIfNull(games);
        return
        [
            .. games
                .Where(g => privacy is null || !privacy.Hides(g))
                .Select(Describe)
                .OrderBy(e => e.State)
                .ThenBy(e => e.State == TranslationQueueState.Queued ? Rank(e.Portuguese) : 0)
                .ThenBy(e => e.ApproximateMinutes ?? int.MaxValue)
                .ThenBy(e => e.Game.Title, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true))
                .ThenBy(e => e.Game.Folder, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Só o que falta traduzir (<see cref="TranslationQueueState.Queued"/>), na ordem da fila.</summary>
    public static IReadOnlyList<TranslationQueueEntry> Pending(IEnumerable<LibraryGame> games, LibraryPrivacy? privacy = null) =>
        [.. Build(games, privacy).Where(e => e.State == TranslationQueueState.Queued)];

    /// <summary>O lugar de um jogo na fila: o estado dele e o que se sabe do português e do tamanho do texto.</summary>
    public static TranslationQueueEntry Describe(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var portuguese = game.CurrentPortuguese?.Status;
        var state = game switch
        {
            { ProjectPath: not null } => TranslationQueueState.InProgress,
            _ when portuguese == PortugueseStatus.Complete => TranslationQueueState.AlreadyTranslated,
            { Engine.Translatable: false } => TranslationQueueState.UnsupportedEngine,
            _ => TranslationQueueState.Queued,
        };
        return new TranslationQueueEntry(game, state, portuguese, ApproximateMinutes(game.Info));
    }

    /// <summary>Os minutos votados no VNDB, ou o meio da categoria de duração; nulo sem nenhum dos dois.</summary>
    public static int? ApproximateMinutes(LibraryVndbInfo? info)
    {
        if (info is null)
        {
            return null;
        }

        if (info.LengthMinutes is > 0 and var minutes)
        {
            return minutes;
        }

        return info.Length is >= 1 and <= 5 and var category ? CategoryMinutes[category - 1] : null;
    }

    private static int Rank(PortugueseStatus? status) =>
        status switch
        {
            PortugueseStatus.None => 0,
            PortugueseStatus.MachineOnly => 1,
            PortugueseStatus.Partial => 2,
            _ => 3,
        };
}
