using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VNForge.Core.Library;

/// <summary>
/// As capturas de tela dos jogos: as imagens PNG em <c>&lt;pasta&gt;/&lt;id do jogo&gt;/aaaammdd-hhmmss.png</c> e o índice em <c>&lt;pasta&gt;/captures.json</c>.
/// A pasta é a de dados do usuário (<see cref="DefaultDirectory"/>), nunca o repositório, o projeto de tradução ou a pasta do jogo (regras 1 e 2 do CLAUDE.md).
/// Cada imagem é gravada por <see cref="AtomicFile"/> e só entra no índice depois de gravada, sob a trava <c>captures.json.lock</c>; se o índice não puder ser gravado,
/// a imagem recém-criada é apagada, para não sobrar arquivo que o índice não conhece. Um índice ilegível é recusado, não esvaziado.
/// </summary>
public sealed class GameCapturesFile
{
    public const string FileName = "captures.json";
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonStore<GameCaptures> _store;
    private readonly TimeProvider _clock;

    public GameCapturesFile(string directory, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        _clock = clock ?? TimeProvider.System;
        Path = System.IO.Path.Combine(directory, FileName);
        _store = new JsonStore<GameCaptures>(
            Path,
            "o índice das capturas",
            $"O índice das capturas ('{Path}') está em uso por outro processo. Tente de novo em instantes.",
            () => new GameCaptures(),
            Parse,
            Serialize);
    }

    /// <summary>A pasta das capturas, onde ficam o índice e uma pasta por jogo.</summary>
    public string Directory { get; }

    public string Path { get; }

    /// <summary><c>%LOCALAPPDATA%/VNForge/captures</c>, ou a pasta temporária se o sistema não informar uma absoluta.</summary>
    public static string DefaultDirectory()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = !string.IsNullOrWhiteSpace(local) && System.IO.Path.IsPathRooted(local) ? local : System.IO.Path.GetTempPath();
        return System.IO.Path.Combine(baseDirectory, "VNForge", "captures");
    }

    /// <exception cref="LibraryException">O índice existe e não pôde ser lido ou não segue o formato; a mensagem cita o arquivo.</exception>
    public GameCaptures Load() => _store.Load();

    /// <summary>O arquivo da imagem da captura, sem conferir se ele existe.</summary>
    public string PathOf(string gameId, GameCapture capture)
    {
        ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(capture);
        ValidateFileName(capture.File);
        return System.IO.Path.Combine(Directory, gameId, capture.File);
    }

    /// <summary>
    /// Grava a imagem como PNG com a data e a hora locais no nome e a põe no índice. Duas capturas no mesmo segundo ficam com <c>-2</c>, <c>-3</c>... no fim do nome.
    /// </summary>
    /// <exception cref="LibraryException">O id do jogo não é válido, o índice está ilegível ou em uso, ou a imagem não pôde ser gravada.</exception>
    public GameCapture Save(string gameId, WindowImage image, string? note = null)
    {
        ValidateGameId(gameId);
        ArgumentNullException.ThrowIfNull(image);
        var png = PngEncoder.Encode(image);
        var written = default(string);
        try
        {
            return _store.Update(captures =>
            {
                var at = _clock.GetLocalNow();
                var capture = new GameCapture(FreeName(gameId, at, captures), at, image.Width, image.Height, string.IsNullOrWhiteSpace(note) ? null : note.Trim());
                var target = PathOf(gameId, capture);
                try
                {
                    AtomicFile.WriteAllBytes(target, png);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new LibraryException($"A captura não pôde ser gravada em '{target}': {e.Message}", e);
                }

                written = target;
                captures.Add(gameId, capture);
                return capture;
            });
        }
        catch (LibraryException)
        {
            DeleteQuietly(written);
            throw;
        }
    }

    /// <summary>Tira a captura do índice e apaga a imagem. Devolve a captura que saiu, ou nulo quando o jogo não tinha esse arquivo.</summary>
    /// <exception cref="LibraryException">O índice está ilegível ou em uso, ou a imagem não pôde ser apagada.</exception>
    public GameCapture? Remove(string gameId, string file)
    {
        ValidateGameId(gameId);
        return _store.Update(captures =>
        {
            var removed = captures.Remove(gameId, file);
            if (removed is not null)
            {
                var target = PathOf(gameId, removed);
                try
                {
                    File.Delete(target);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new LibraryException($"A imagem '{target}' não pôde ser apagada: {e.Message}", e);
                }
            }

            return removed;
        });
    }

    /// <summary>O id do jogo vira nome de pasta, então só letras, dígitos, <c>-</c> e <c>_</c> (os ids da biblioteca são <c>g</c> e oito dígitos hexadecimais).</summary>
    /// <exception cref="LibraryException">O id não é válido.</exception>
    internal static void ValidateGameId(string gameId)
    {
        if (string.IsNullOrEmpty(gameId) || !gameId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new LibraryException($"O id de jogo '{gameId}' não serve para guardar capturas: use só letras, números, - e _.");
        }
    }

    /// <summary>O nome é só um nome de arquivo PNG, sem pasta nem <c>..</c>: um índice mexido à mão não leva a lugar nenhum fora da pasta do jogo.</summary>
    /// <exception cref="LibraryException">O nome não é válido.</exception>
    internal static void ValidateFileName(string file)
    {
        if (string.IsNullOrEmpty(file)
            || !file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || !file.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            || file.StartsWith('.'))
        {
            throw new LibraryException($"O nome de arquivo de captura '{file}' não é válido: use só letras, números, - _ e . e termine em .png.");
        }
    }

    private string FreeName(string gameId, DateTimeOffset at, GameCaptures captures)
    {
        var stamp = at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var attempt = 1; ; attempt++)
        {
            var name = attempt == 1 ? $"{stamp}.png" : $"{stamp}-{attempt}.png";
            if (!captures.Contains(gameId, name) && !File.Exists(System.IO.Path.Combine(Directory, gameId, name)))
            {
                return name;
            }
        }
    }

    private static void DeleteQuietly(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>O JSON do índice, com os jogos por id e as capturas da mais antiga para a mais nova.</summary>
    public static string Serialize(GameCaptures captures)
    {
        ArgumentNullException.ThrowIfNull(captures);
        var games = new SortedDictionary<string, List<GameCapture>?>(StringComparer.Ordinal);
        foreach (var id in captures.GameIds)
        {
            games[id] = [.. captures.Of(id)];
        }

        return JsonSerializer.Serialize(new Envelope(CurrentVersion, games), Json);
    }

    /// <exception cref="LibraryException">JSON malformado, versão desconhecida, jogo sem id, ou captura com nome de arquivo inválido ou repetido.</exception>
    public static GameCaptures Parse(string json, string filePath = "(memória)")
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json, Json);
        }
        catch (JsonException e)
        {
            throw new LibraryException($"O arquivo '{filePath}' não está no formato esperado: {e.Message}", e);
        }

        if (envelope is null)
        {
            throw new LibraryException($"O arquivo '{filePath}' está vazio.");
        }

        if (envelope.Version != CurrentVersion)
        {
            throw new LibraryException($"O arquivo '{filePath}' é da versão {envelope.Version} das capturas da biblioteca; esta versão do VNForge lê a {CurrentVersion}.");
        }

        var captures = new GameCaptures();
        try
        {
            foreach (var (gameId, list) in envelope.Games ?? [])
            {
                foreach (var capture in list ?? [])
                {
                    captures.Add(gameId, capture ?? throw new LibraryException($"há uma captura vazia no jogo {gameId}."));
                }
            }
        }
        catch (LibraryException e)
        {
            throw new LibraryException($"O arquivo '{filePath}': {e.Message}", e);
        }

        return captures;
    }

    private sealed record Envelope(int Version, SortedDictionary<string, List<GameCapture>?>? Games);
}
