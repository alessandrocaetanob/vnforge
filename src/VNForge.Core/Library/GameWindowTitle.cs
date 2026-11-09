using System.Diagnostics;
using System.Text.Json;

namespace VNForge.Core.Library;

/// <summary>Lê o título da janela de um jogo sem executá-lo. O app e a CLI podem trazer leitores de engine (o <c>yscfg.ybn</c> do YU-RIS, por exemplo) por esta interface.</summary>
public interface IGameWindowTitleReader
{
    /// <summary>O título da janela, ou nulo quando não dá para ler sem executar o jogo. Nunca lança por arquivo ilegível.</summary>
    /// <param name="gameFolder">Pasta do jogo.</param>
    /// <param name="exe">Executável do jogo, relativo à pasta, quando se sabe qual é.</param>
    string? Read(string gameFolder, string? exe);
}

/// <summary>
/// O leitor que o Core traz: o título do RPG Maker (<c>Data/System.json</c> do MV e do MZ, <c>Game.ini</c> do VX), e o nome do produto que o Windows guarda no
/// executável (recurso de versão). Lê só os recursos do arquivo; não carrega nem roda o executável.
/// </summary>
public sealed class GameWindowTitleReader : IGameWindowTitleReader
{
    private static readonly string[] SystemJsonPaths = ["www/data/System.json", "data/System.json"];

    public string? Read(string gameFolder, string? exe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameFolder);
        return FromRpgMaker(gameFolder) ?? FromExecutable(gameFolder, exe);
    }

    private static string? FromRpgMaker(string gameFolder)
    {
        foreach (var relative in SystemJsonPaths)
        {
            var path = Path.Combine(gameFolder, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (File.Exists(path))
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                    if (document.RootElement.ValueKind == JsonValueKind.Object
                        && document.RootElement.TryGetProperty("gameTitle", out var title)
                        && title.ValueKind == JsonValueKind.String
                        && GameNames.CleanWindowTitle(title.GetString()) is { } found)
                    {
                        return found;
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                // Arquivo ilegível: sem título por aqui.
            }
        }

        return FromGameIni(Path.Combine(gameFolder, "Game.ini"));
    }

    private static string? FromGameIni(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            foreach (var line in File.ReadLines(path))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("Title=", StringComparison.OrdinalIgnoreCase))
                {
                    return GameNames.CleanWindowTitle(trimmed["Title=".Length..]);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Arquivo ilegível: sem título por aqui.
        }

        return null;
    }

    private static string? FromExecutable(string gameFolder, string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return null;
        }

        try
        {
            var path = Path.Combine(gameFolder, exe);
            if (!File.Exists(path))
            {
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            var stem = Path.GetFileNameWithoutExtension(exe);
            foreach (var candidate in new[] { info.ProductName, info.FileDescription })
            {
                var title = GameNames.CleanWindowTitle(candidate);
                if (title is not null && !string.Equals(title, stem, StringComparison.OrdinalIgnoreCase))
                {
                    return title;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            // Executável ilegível: sem título por aqui.
        }

        return null;
    }
}
