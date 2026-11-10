using System.Globalization;
using VNForge.Core.Projects;
using VNForge.Core.Vndb;

namespace VNForge.Core.Library;

/// <summary>O que a grade mostra no lugar da capa de um jogo.</summary>
public enum LibraryCoverKind
{
    /// <summary>A capa do VNDB (a do jogo confirmado, ou a do primeiro candidato enquanto a pessoa não confirma).</summary>
    Image,

    /// <summary>Capa de conteúdo adulto escondida pela privacidade: o véu "18+".</summary>
    AdultVeil,

    /// <summary>Sem capa: a placa com o título e o título original.</summary>
    NoArt,
}

/// <summary>O estado do casamento com o VNDB, o círculo no canto da capa.</summary>
public enum LibraryMatchState
{
    /// <summary>Sem círculo: a capa está escondida por ser adulta.</summary>
    None,

    /// <summary>Casado com um jogo do VNDB.</summary>
    Matched,

    /// <summary>O casamento achou candidatos e espera a pessoa confirmar.</summary>
    NeedsConfirmation,

    /// <summary>O VNDB não achou o jogo, ou ainda não foi procurado.</summary>
    Unmatched,
}

/// <summary>
/// Um jogo como a grade o desenha. <paramref name="Chip"/> é a etiqueta de engine no pé da capa e <paramref name="ChipWarn"/> a pinta de aviso (engine que o VNForge não traduz).
/// <paramref name="ProgressPercent"/> é a parte traduzida do projeto ligado (0 a 100), nula sem projeto. <paramref name="Subtitle"/> é a linha de baixo ("Média · 18 h · 38% traduzida").
/// </summary>
public sealed record LibraryCard(
    string Id,
    string Title,
    string? OriginalTitle,
    LibraryCoverKind Cover,
    VndbImageRef? Image,
    string Chip,
    bool ChipWarn,
    LibraryMatchState Match,
    string MatchText,
    int? ProgressPercent,
    string Subtitle);

/// <summary>Descreve cada jogo da biblioteca para a grade, a lista e a gaveta. Só texto e escolhas; a tela decide o desenho.</summary>
public static class LibraryCards
{
    public const string NoData = "sem dado";

    /// <param name="translation">O andamento do projeto ligado ao jogo; nulo sem projeto.</param>
    public static LibraryCard Describe(LibraryGame game, LibraryPrivacy privacy, StatusCounts? translation = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(privacy);
        var info = game.Info;
        var hideCover = info is { Adult: true } && !privacy.ShowAdult;
        var progress = game.ProjectPath is not null ? PercentReady(translation) : null;

        var image = hideCover ? null : CoverOf(game);
        var cover = hideCover ? LibraryCoverKind.AdultVeil : image is null ? LibraryCoverKind.NoArt : LibraryCoverKind.Image;
        var (match, matchText) = game.IsConfirmed
            ? (LibraryMatchState.Matched, "Casado com o VNDB")
            : game.Candidates.Count > 0
                ? (LibraryMatchState.NeedsConfirmation, "Confirmar o casamento")
                : (LibraryMatchState.Unmatched, game.SearchedAt is null ? "Ainda não procurado no VNDB" : "Sem casamento no VNDB");
        if (hideCover)
        {
            match = LibraryMatchState.None;
        }

        var parts = new List<string>();
        if (LibraryLength.Short(info?.LengthMinutes, info?.Length) is { } length)
        {
            parts.Add(length);
        }

        if (progress is > 0)
        {
            parts.Add($"{progress.Value.ToString(CultureInfo.InvariantCulture)}% traduzida");
        }

        return new LibraryCard(
            game.Id,
            info?.Title ?? game.Title,
            game.IsConfirmed ? info?.OriginalTitle : OriginalOfUnmatched(game),
            cover,
            image,
            ChipText(game.Engine),
            !game.Engine.Translatable,
            match,
            matchText,
            progress,
            parts.Count == 0 ? NoData : string.Join(" · ", parts));
    }

    /// <summary>A parte pronta (revisada, aprovada ou "não traduzir") do projeto, de 0 a 100, arredondada para baixo: a mesma conta do cabeçalho do jogo. Nula sem linhas.</summary>
    public static int? PercentReady(StatusCounts? counts) =>
        counts is { Total: > 0 } ? (int)Math.Floor(100.0 * counts.Ready / counts.Total) : null;

    /// <summary>"Yu-RIS" quando o VNForge traduz a engine, "Siglus · sem suporte" quando só a reconhece, "Engine não reconhecida" quando nem isso.</summary>
    public static string ChipText(LibraryEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Source switch
        {
            LibraryEngineSource.Engine => engine.Name,
            LibraryEngineSource.Signature => $"{engine.Name} · sem suporte",
            _ => "Engine não reconhecida",
        };
    }

    private static VndbImageRef? CoverOf(LibraryGame game)
    {
        if (game.Info is { } info)
        {
            return info.CoverUrl is { Length: > 0 } url ? new VndbImageRef(url, info.Adult ? VndbImageVisibility.AdultLevel : 0) : null;
        }

        return game.Candidates.Select(c => c.Image).FirstOrDefault(i => i is not null);
    }

    private static string? OriginalOfUnmatched(LibraryGame game) =>
        game.WindowTitle is { Length: > 0 } window && !string.Equals(window, game.Title, StringComparison.OrdinalIgnoreCase) ? window : null;
}
