using VNForge.Core.Engines;
using VNForge.Core.Text;

namespace VNForge.Core.Projects;

/// <summary>Um script que a extração gravou em <c>text/</c>.</summary>
/// <param name="Key">Chave do script (<c>yst00012</c>), também o nome do arquivo <c>text/yst00012.json</c>.</param>
/// <param name="Name">Nome legível que a engine conhece, ou nulo.</param>
/// <param name="Lines">Unidades de texto gravadas.</param>
public sealed record ExtractedScript(string Key, string? Name, int Lines);

/// <summary>Resultado de <see cref="ProjectExtractor.Extract"/>.</summary>
/// <param name="GameDirectory">Pasta do jogo que foi lida.</param>
/// <param name="CacheDirectory">Pasta com os pacotes descompactados (<see cref="ProjectFolder.GameCacheDirectory"/>).</param>
/// <param name="Archives">Pacotes descompactados.</param>
/// <param name="Scripts">Um item por script, na ordem da chave.</param>
/// <param name="LinesByKind">Unidades por <see cref="TextUnitKind"/>; todo valor do enum aparece, inclusive com zero.</param>
/// <param name="Warnings">Avisos da engine, cada um com o nome do script quando vem de um.</param>
public sealed record ExtractionResult(
    string GameDirectory,
    string CacheDirectory,
    IReadOnlyList<UnpackedArchive> Archives,
    IReadOnlyList<ExtractedScript> Scripts,
    IReadOnlyDictionary<TextUnitKind, int> LinesByKind,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Total de unidades de texto em todos os scripts.</summary>
    public int TotalLines => Scripts.Sum(s => s.Lines);
}

/// <summary>
/// A primeira extração de um projeto (<c>vnf extract</c>): descompacta os pacotes do jogo para <c>.cache/game/</c>, lê cada script com o
/// <see cref="IScriptFormat"/> da engine e grava <c>text/&lt;chave&gt;.json</c>. Só lê a pasta do jogo. A re-extração que preserva
/// traduções é a E3.5: com <c>text/*.json</c> já existente, esta etapa recusa e não toca em nada.
/// </summary>
public static class ProjectExtractor
{
    /// <summary>Extrai o jogo do <paramref name="project"/>.</summary>
    /// <param name="project">Projeto aberto; <c>game.path</c> do <c>vnforge.json</c> é relativo à pasta do projeto quando não é absoluto.</param>
    /// <param name="engines">Engines conhecidas; vale a de <c>engine.id</c> do <c>vnforge.json</c>.</param>
    /// <param name="cancellationToken">Cancela entre um script e outro; um cancelamento antes da gravação do texto não deixa <c>text/</c> pela metade.</param>
    /// <exception cref="VNForgeException">
    /// Engine desconhecida ou sem suporte à extração, pasta do jogo inexistente, <c>.cache/</c> dentro do jogo, <c>text/</c> que já tem
    /// arquivos, script ilegível ou duas chaves iguais. A mensagem, em português, cita o script ou o arquivo.
    /// </exception>
    public static ExtractionResult Extract(ProjectFolder project, IReadOnlyList<IEngine> engines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(engines);

        var config = project.LoadConfig();
        var engine = engines.FirstOrDefault(e => string.Equals(e.Id, config.Engine.Id, StringComparison.Ordinal))
            ?? throw new VNForgeException($"A engine '{config.Engine.Id}' do {ProjectFolder.ConfigFileName} não é conhecida por esta versão do vnf.");
        if (engine is not IGameUnpacker unpacker)
        {
            throw new VNForgeException($"A engine {engine.DisplayName} ainda não suporta a extração.");
        }

        var gameDirectory = Path.GetFullPath(config.Game.Path, project.Root);
        if (!Directory.Exists(gameDirectory))
        {
            throw new VNForgeException($"A pasta do jogo não existe: {gameDirectory}");
        }

        if (IsInside(ResolveLinks(gameDirectory), ResolveLinks(project.GameCacheDirectory)))
        {
            throw new VNForgeException(
                $"O .cache do projeto ('{project.CacheDirectory}') fica dentro da pasta do jogo ('{gameDirectory}'): o VNForge não grava nada no jogo fora da injeção com backup.");
        }

        ThrowIfTextExists(project);

        if (Directory.Exists(project.GameCacheDirectory))
        {
            Directory.Delete(project.GameCacheDirectory, recursive: true);
        }

        var unpacked = unpacker.Unpack(gameDirectory, project.GameCacheDirectory, cancellationToken);
        var game = unpacker.Open(project.GameCacheDirectory);
        if (game.Scripts.Count == 0)
        {
            throw new VNForgeException($"Nenhum script foi encontrado em '{project.GameCacheDirectory}' depois de descompactar o jogo; nada foi gravado em text/.");
        }

        var warnings = new List<string>(unpacked.Warnings);
        warnings.AddRange(game.Warnings);
        var extracted = ReadScripts(game, warnings, cancellationToken);

        var written = new List<string>();
        try
        {
            foreach (var script in extracted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                project.SaveText(script.Key, script.Units);
                written.Add(project.TextPath(script.Key));
            }
        }
        catch
        {
            foreach (var path in written)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // O erro original é o que importa; o que não saiu segue para o próximo.
                }
            }

            throw;
        }

        var byKind = Enum.GetValues<TextUnitKind>().ToDictionary(kind => kind, _ => 0);
        foreach (var unit in extracted.SelectMany(s => s.Units))
        {
            byKind[unit.Kind]++;
        }

        return new ExtractionResult(
            gameDirectory,
            project.GameCacheDirectory,
            unpacked.Archives,
            extracted.Select(s => new ExtractedScript(s.Key, s.Name, s.Units.Count)).ToList(),
            byKind,
            warnings);
    }

    private sealed record Extracted(string Key, string? Name, IReadOnlyList<TextUnit> Units);

    private static List<Extracted> ReadScripts(UnpackedGame game, List<string> warnings, CancellationToken cancellationToken)
    {
        var result = new List<Extracted>(game.Scripts.Count);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var script in game.Scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new ScriptFile(script.Name, File.ReadAllBytes(script.Path));
            if (!keys.TryAdd(file.Key, script.Path))
            {
                throw new VNForgeException(
                    $"Os scripts '{keys[file.Key]}' e '{script.Path}' têm a mesma chave '{file.Key}'; dois scripts não podem gerar o mesmo text/{file.Key}.json.");
            }

            ScriptExtraction extraction;
            try
            {
                extraction = script.Format.Extract(file, game.Context);
            }
            catch (Exception ex) when (ex is InvalidDataException or VNForgeException)
            {
                throw new VNForgeException($"Não foi possível extrair o texto do script '{script.Name}': {ex.Message}", ex);
            }

            warnings.AddRange(extraction.Warnings.Select(w => $"{script.Name}: {w}"));
            result.Add(new Extracted(file.Key, script.DisplayName, extraction.Units));
        }

        result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return result;
    }

    private static void ThrowIfTextExists(ProjectFolder project)
    {
        if (!Directory.Exists(project.TextDirectory))
        {
            return;
        }

        var existing = Directory.EnumerateFiles(project.TextDirectory, "*.json").Count();
        if (existing > 0)
        {
            throw new VNForgeException(
                $"A pasta text/ já tem {existing} arquivo(s) de texto, e a re-extração que preserva as traduções ainda não existe (E3.5). Nada foi alterado. Para extrair do zero, mova ou apague a pasta text/ (as traduções nela se perdem).");
        }
    }

    /// <summary>Segue os links simbólicos e junções do caminho, para um <c>.cache</c> que aponta para dentro do jogo não passar.</summary>
    internal static string ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (info.Exists && info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                current = target.FullName;
            }
        }

        return current;
    }

    internal static bool IsInside(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
