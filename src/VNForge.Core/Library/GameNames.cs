using System.Text.RegularExpressions;

namespace VNForge.Core.Library;

/// <summary>De onde veio um termo de busca do VNDB.</summary>
public enum TermSource
{
    /// <summary>O título da janela do jogo, lido do executável ou da configuração do jogo.</summary>
    WindowTitle,

    /// <summary>O nome da pasta do jogo, sem colchetes, versão e ruído de instalador.</summary>
    Folder,

    /// <summary>O nome do executável.</summary>
    Exe,
}

/// <summary>Um texto para buscar no VNDB e de onde ele veio.</summary>
public sealed record SearchTerm(string Text, TermSource Source);

/// <summary>
/// Tira do título da janela, do executável e da pasta os textos que valem uma busca no VNDB. Pastas de coleção costumam vir como
/// <c>[Marca] Título v1.02 (DL版)</c>: o título é o que sobra sem colchetes, parênteses e versão.
/// </summary>
public static partial class GameNames
{
    private const int RegexTimeoutMilliseconds = 500;

    private static readonly HashSet<string> GenericExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "start", "launcher", "run", "main", "play", "app", "application", "nw", "winmain", "program", "exe", "kirikiri", "krkr", "renpy", "nscr", "nscripter", "reallive",
    };

    private static readonly HashSet<string> GenericTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "game", "kirikiri", "krkr", "renpy", "ren'py", "nscripter", "onscripter", "reallive", "siglus", "unity", "rpg maker", "rpgmaker", "application", "windows application", "untitled",
    };

    /// <summary>Os termos de busca, do mais confiável para o menos, sem repetir o mesmo texto da mesma origem. Termo curto demais ou genérico não entra.</summary>
    public static IReadOnlyList<SearchTerm> SearchTerms(string? windowTitle, string? exe, string? folder)
    {
        var terms = new List<SearchTerm>();
        Add(terms, CleanWindowTitle(windowTitle), TermSource.WindowTitle);
        Add(terms, CleanFolderName(folder), TermSource.Folder);
        Add(terms, CleanExeName(exe), TermSource.Exe);
        return terms;
    }

    /// <summary>O título da janela sem espaços sobrando, ou nulo se vazio ou genérico (o nome da engine, por exemplo).</summary>
    public static string? CleanWindowTitle(string? title)
    {
        var text = Collapse(title ?? string.Empty);
        return text.Length == 0 || GenericTitles.Contains(text) ? null : text;
    }

    /// <summary>
    /// O nome da pasta como título: sem colchetes, parênteses, versão e palavras de instalador. Se só havia colchetes, vale o conteúdo do primeiro.
    /// Nulo para pasta sem nome (raiz de unidade) ou sem nada que sirva.
    /// </summary>
    public static string? CleanFolderName(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder.Trim()));
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Replace('_', ' ');
        var cleaned = Brackets().Replace(name, " ");
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            var inner = Brackets().Match(name);
            cleaned = inner.Success ? inner.Value.Trim('[', ']', '(', ')', '【', '】', '（', '）') : name;
        }

        cleaned = DlsiteCode().Replace(cleaned, " ");
        cleaned = Versions().Replace(cleaned, " ");
        cleaned = InstallerWords().Replace(cleaned, " ");
        if (!cleaned.Trim().Contains(' ', StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace('.', ' ');
        }

        cleaned = Collapse(cleaned).Trim(' ', '-', '~', '.', ',');
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>O nome do executável como título, ou nulo se for genérico (<c>game</c>, <c>start</c>...).</summary>
    public static string? CleanExeName(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(exe.Trim());
        if (GenericExecutables.Contains(stem))
        {
            return null;
        }

        var text = Collapse(stem.Replace('_', ' '));
        return text.Length == 0 ? null : text;
    }

    private static void Add(List<SearchTerm> terms, string? text, TermSource source)
    {
        if (text is null || text.Length < 2 || text.All(char.IsDigit))
        {
            return;
        }

        if (!terms.Any(t => t.Source == source && string.Equals(t.Text, text, StringComparison.OrdinalIgnoreCase)))
        {
            terms.Add(new SearchTerm(text, source));
        }
    }

    private static string Collapse(string text) => Spaces().Replace(text.Trim(), " ");

    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|【[^】]*】|（[^）]*）", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\b(?:RJ|VJ|BJ)\d{6,8}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex DlsiteCode();

    [GeneratedRegex(@"\b(?:v|ver\.?|version)\s*\d+(?:\.\d+)*[a-z]?\b|\b\d+\.\d+(?:\.\d+)+\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex Versions();

    [GeneratedRegex(@"\b(?:setup|installer|install|repack|portable|x64|x86|win32|win64)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex InstallerWords();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex Spaces();
}
