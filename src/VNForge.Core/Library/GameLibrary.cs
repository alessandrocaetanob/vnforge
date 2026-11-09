using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VNForge.Core.Library;

/// <summary>Erro esperado da biblioteca (arquivo ilegível, jogo que não existe, referência ambígua). A mensagem é em português e vai direto ao usuário.</summary>
public sealed class LibraryException(string message, Exception? innerException = null) : VNForgeException(message, innerException);

/// <summary>O que uma varredura mudou na biblioteca: jogos novos, jogos que já estavam (com engine, exe ou título atualizados) e jogos da pasta varrida que não foram achados dessa vez.</summary>
public sealed record LibraryMergeResult(IReadOnlyList<LibraryGame> Added, IReadOnlyList<LibraryGame> Updated, IReadOnlyList<LibraryGame> Missing);

/// <summary>
/// As VNs do usuário: a lista de jogos e a privacidade. Em memória; <see cref="LibraryFile"/> lê e grava <c>library.json</c>. A varredura nunca apaga um jogo
/// nem desfaz um casamento confirmado: jogo que sumiu da pasta só aparece em <see cref="LibraryMergeResult.Missing"/>.
/// </summary>
public sealed class GameLibrary
{
    private readonly List<LibraryGame> _games;

    public GameLibrary(IEnumerable<LibraryGame>? games = null, LibraryPrivacy? privacy = null, string? localeEmulatorPath = null)
    {
        _games = [.. games ?? []];
        Privacy = privacy ?? new LibraryPrivacy();
        LocaleEmulatorPath = string.IsNullOrWhiteSpace(localeEmulatorPath) ? null : localeEmulatorPath;
    }

    public LibraryPrivacy Privacy { get; set; }

    /// <summary>
    /// Onde está o Locale Emulator (a pasta dele ou o <c>LEProc.exe</c>), para os jogos que pedem localidade japonesa. É um ajuste do usuário, não de um jogo;
    /// fica aqui para o app e a CLI lerem o mesmo valor. <see langword="null"/> enquanto a pessoa não informou.
    /// </summary>
    public string? LocaleEmulatorPath { get; set; }

    public IReadOnlyList<LibraryGame> Games => _games;

    /// <summary>O id estável de um jogo: vem só da pasta (sem diferenciar maiúsculas), então varrer de novo dá o mesmo id.</summary>
    public static string IdFor(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var normalized = NormalizeFolder(folder).ToLowerInvariant();
        return "g" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..8];
    }

    public static string NormalizeFolder(string folder) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    /// <summary>
    /// Acha um jogo pelo id, pelo nome exato da pasta ou por um pedaço único do título ou da pasta (sem diferenciar maiúsculas).
    /// </summary>
    /// <exception cref="LibraryException">Nenhum jogo combina, ou mais de um combina com um pedaço de nome.</exception>
    public LibraryGame Find(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var text = reference.Trim();

        var byId = _games.FirstOrDefault(g => string.Equals(g.Id, text, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId;
        }

        var byFolderName = _games.Where(g => string.Equals(FolderName(g), text, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byFolderName.Count == 1)
        {
            return byFolderName[0];
        }

        var matches = byFolderName.Count > 1
            ? byFolderName
            : _games.Where(g => g.Title.Contains(text, StringComparison.OrdinalIgnoreCase) || FolderName(g).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new LibraryException($"Nenhum jogo da biblioteca combina com '{text}'. Veja os jogos com vnf library list."),
            _ => throw new LibraryException(
                $"'{text}' combina com {matches.Count} jogos: {string.Join(", ", matches.Take(5).Select(g => $"{g.Id} ({g.Title})"))}{(matches.Count > 5 ? ", ..." : string.Empty)}. Use o id."),
        };
    }

    /// <summary>Troca o jogo de mesmo id pelo novo.</summary>
    /// <exception cref="LibraryException">O jogo não está na biblioteca.</exception>
    public void Replace(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        var index = _games.FindIndex(g => g.Id == game.Id);
        if (index < 0)
        {
            throw new LibraryException($"O jogo {game.Id} não está na biblioteca.");
        }

        _games[index] = game;
    }

    /// <summary>Tira o jogo da biblioteca (só da lista; a pasta do jogo não é tocada). Devolve se existia.</summary>
    public bool Remove(string id) => _games.RemoveAll(g => string.Equals(g.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>
    /// Junta o que a varredura de <paramref name="scannedRoot"/> achou. Jogo novo entra; jogo conhecido guarda o casamento, as notas e o projeto, e
    /// só atualiza engine, executável e título da janela. Jogo conhecido sob <paramref name="scannedRoot"/> que não veio na varredura é devolvido em
    /// <see cref="LibraryMergeResult.Missing"/> e fica na lista.
    /// </summary>
    public LibraryMergeResult Merge(IEnumerable<LibraryGame> found, string scannedRoot)
    {
        ArgumentNullException.ThrowIfNull(found);
        ArgumentException.ThrowIfNullOrWhiteSpace(scannedRoot);

        var added = new List<LibraryGame>();
        var updated = new List<LibraryGame>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var game in found)
        {
            seen.Add(game.Id);
            var index = _games.FindIndex(g => g.Id == game.Id);
            if (index < 0)
            {
                _games.Add(game);
                added.Add(game);
                continue;
            }

            var current = _games[index];
            var merged = current with
            {
                Exe = game.Exe ?? current.Exe,
                Engine = game.Engine,
                WindowTitle = game.WindowTitle ?? current.WindowTitle,
                Title = current.Info?.Title ?? game.Title,
            };
            if (merged != current)
            {
                _games[index] = merged;
                updated.Add(merged);
            }
        }

        var root = NormalizeFolder(scannedRoot);
        var missing = _games.Where(g => !seen.Contains(g.Id) && IsUnder(g.Folder, root)).ToList();
        return new LibraryMergeResult(added, updated, missing);
    }

    /// <summary>A pasta está em <paramref name="root"/> ou é ela mesma. Compara sem diferenciar maiúsculas no Windows.</summary>
    public static bool IsUnder(string folder, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var path = NormalizeFolder(folder);
        var top = NormalizeFolder(root);
        return string.Equals(path, top, comparison)
            || path.StartsWith(top + Path.DirectorySeparatorChar, comparison)
            || path.StartsWith(top + Path.AltDirectorySeparatorChar, comparison);
    }

    public static string FolderName(LibraryGame game) => Path.GetFileName(Path.TrimEndingDirectorySeparator(game.Folder));

    /// <summary>Os jogos ordenados por título, para listar sempre na mesma ordem.</summary>
    public IReadOnlyList<LibraryGame> Sorted() =>
        [.. _games.OrderBy(g => g.Title, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)).ThenBy(g => g.Folder, StringComparer.OrdinalIgnoreCase)];
}
