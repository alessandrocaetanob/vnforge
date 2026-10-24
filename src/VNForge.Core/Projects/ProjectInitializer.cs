using System.Globalization;
using VNForge.Core.Engines;

namespace VNForge.Core.Projects;

/// <summary>Escolhas do usuário ao criar um projeto; o que ficar nulo é deduzido da pasta do jogo.</summary>
/// <param name="Name">Nome do projeto; padrão: o nome da pasta do projeto.</param>
/// <param name="Exe">Executável do jogo, relativo à pasta do jogo; padrão: o único <c>.exe</c> da pasta (fora os desinstaladores).</param>
/// <param name="SourceLanguage">Idioma do jogo; padrão: <c>ja</c>.</param>
/// <param name="TargetLanguage">Idioma da tradução; padrão: <c>pt-BR</c>.</param>
/// <param name="EncodingStrategy">Estratégia de acentuação; padrão: <see cref="ProjectChoices.DefaultEncoding"/>.</param>
/// <param name="Ai">Provedor, modelo e lote da IA; padrão: <see cref="ProjectChoices.DefaultAi"/>.</param>
public sealed record ProjectInitOptions(
    string? Name = null,
    string? Exe = null,
    string? SourceLanguage = null,
    string? TargetLanguage = null,
    string? EncodingStrategy = null,
    AiConfig? Ai = null);

/// <summary>O que a detecção achou na pasta do jogo, sem criar nada: base do primeiro passo do assistente de novo projeto.</summary>
/// <param name="Executables">Executáveis candidatos, sem os desinstaladores, em ordem alfabética.</param>
public sealed record GameInspection(IEngine Engine, EngineDetection Detection, IReadOnlyList<string> Executables);

/// <summary>Valores que o <c>vnforge.json</c> aceita nas escolhas do <c>vnf init</c> e do assistente, e os padrões de cada uma.</summary>
public static class ProjectChoices
{
    public const string DefaultSourceLanguage = "ja";
    public const string DefaultTargetLanguage = "pt-BR";

    // fold até o patch do executável (#65) ser validado: o jogo lê Shift-JIS e cp1252 vira lixo na tela (decisão #63).
    public const string DefaultEncoding = "fold";

    public static IReadOnlyList<string> EncodingStrategies => ProjectConfigValidator.EncodingStrategies;

    public static IReadOnlyList<string> Models => ProjectConfigValidator.AnthropicModels;

    public static AiConfig DefaultAi => AiConfig.Default;
}

/// <summary>O projeto criado e o que a detecção encontrou no jogo.</summary>
public sealed record ProjectInitResult(ProjectFolder Folder, ProjectConfig Config, IEngine Engine, EngineDetection Detection);

/// <summary>
/// Cria um projeto de tradução a partir de uma pasta de jogo: roda a detecção das engines, grava engine, versão e chave no
/// <c>vnforge.json</c> e monta as pastas do projeto. Só lê a pasta do jogo.
/// </summary>
public static class ProjectInitializer
{
    /// <summary>Chave de <see cref="EngineDetection.Details"/> que vira <c>engine.version</c>.</summary>
    public const string VersionDetail = "ypfVersion";

    /// <summary>Chave de <see cref="EngineDetection.Details"/> que vira <c>engine.scriptKey</c>.</summary>
    public const string ScriptKeyDetail = "scriptKey";

    /// <summary>Chave de <see cref="EngineDetection.Details"/> com os avisos da detecção.</summary>
    public const string WarningsDetail = "warnings";

    private const string UninstallerPrefix = "unins";

    /// <summary>Roda a detecção das engines e lista os executáveis da pasta do jogo. Só lê a pasta.</summary>
    /// <exception cref="ProjectInitException">Jogo inexistente ou engine não reconhecida.</exception>
    public static GameInspection Inspect(string gameDir, IReadOnlyList<IEngine> engines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        ArgumentNullException.ThrowIfNull(engines);

        var game = Path.GetFullPath(gameDir);
        if (!Directory.Exists(game))
        {
            throw new ProjectInitException($"A pasta do jogo não existe: {game}");
        }

        var (engine, detection) = Detect(engines, game);
        return new GameInspection(engine, detection, ListExecutables(game));
    }

    /// <summary>Cria o projeto em <paramref name="projectDir"/> para o jogo de <paramref name="gameDir"/>.</summary>
    /// <exception cref="ProjectConfigException">A pasta já tem um <c>vnforge.json</c>; nada é sobrescrito.</exception>
    /// <exception cref="ProjectInitException">Jogo inexistente, projeto dentro da pasta do jogo, engine não reconhecida ou executável não resolvido; nada é criado.</exception>
    public static ProjectInitResult Initialize(
        string projectDir,
        string gameDir,
        IReadOnlyList<IEngine> engines,
        ProjectInitOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDir);
        ArgumentNullException.ThrowIfNull(engines);
        options ??= new ProjectInitOptions();

        var project = Path.GetFullPath(projectDir);
        var game = Path.GetFullPath(gameDir);
        if (!Directory.Exists(game))
        {
            throw new ProjectInitException($"A pasta do jogo não existe: {game}");
        }

        if (IsInside(game, project))
        {
            throw new ProjectInitException(
                $"A pasta do projeto ('{project}') não pode ficar dentro da pasta do jogo ('{game}'): o VNForge não grava nada no jogo fora da injeção com backup.");
        }

        if (File.Exists(Path.Combine(project, ProjectFolder.ConfigFileName)))
        {
            throw new ProjectConfigException($"Já existe um {ProjectFolder.ConfigFileName} em '{project}'; o projeto não foi sobrescrito.");
        }

        var (engine, detection) = Detect(engines, game);
        var exe = ResolveExe(game, options.Exe);
        var config = new ProjectConfig(
            string.IsNullOrWhiteSpace(options.Name) ? NameOf(project) : options.Name.Trim(),
            new GameConfig(game.Replace(Path.DirectorySeparatorChar, '/'), exe),
            new EngineConfig(engine.Id, VersionOf(detection), KeyOf(detection)),
            new LanguagesConfig(
                string.IsNullOrWhiteSpace(options.SourceLanguage) ? ProjectChoices.DefaultSourceLanguage : options.SourceLanguage.Trim(),
                string.IsNullOrWhiteSpace(options.TargetLanguage) ? ProjectChoices.DefaultTargetLanguage : options.TargetLanguage.Trim()),
            new EncodingConfig(string.IsNullOrWhiteSpace(options.EncodingStrategy) ? ProjectChoices.DefaultEncoding : options.EncodingStrategy.Trim()),
            options.Ai ?? ProjectChoices.DefaultAi);

        return new ProjectInitResult(ProjectFolder.Create(project, config), config, engine, detection);
    }

    private static (IEngine Engine, EngineDetection Detection) Detect(IReadOnlyList<IEngine> engines, string game)
    {
        string? warnings = null;
        foreach (var engine in engines)
        {
            var detection = engine.Detect(game);
            if (detection.IsMatch)
            {
                return (engine, detection);
            }

            if (warnings is null && detection.Details.TryGetValue(WarningsDetail, out var found))
            {
                warnings = found;
            }
        }

        var hint = warnings is null ? string.Empty : $" Avisos: {warnings}.";
        throw new ProjectInitException($"Engine não reconhecida na pasta do jogo: {game}.{hint} O projeto não foi criado.");
    }

    private static string ResolveExe(string game, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return ValidateExe(game, requested.Trim());
        }

        var candidates = ListExecutables(game);

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new ProjectInitException($"Nenhum executável (.exe) em '{game}'. Informe o executável do jogo com --exe."),
            _ => throw new ProjectInitException(
                $"Mais de um executável (.exe) em '{game}': {string.Join(", ", candidates)}. Informe o do jogo com --exe."),
        };
    }

    private static List<string> ListExecutables(string game) =>
        Directory
            .EnumerateFiles(game, "*.exe", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.StartsWith(UninstallerPrefix, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ValidateExe(string game, string requested)
    {
        var full = Path.GetFullPath(requested.Replace('\\', '/'), game);
        if (!IsInside(game, full) || !File.Exists(full))
        {
            throw new ProjectInitException($"O executável '{requested}' não existe dentro da pasta do jogo ('{game}').");
        }

        return Path.GetRelativePath(game, full).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Verdadeiro quando <paramref name="path"/> é <paramref name="folder"/> ou está dentro dela, com a comparação de maiúsculas do sistema.</summary>
    private static bool IsInside(string folder, string path)
    {
        var relative = Path.GetRelativePath(folder, path);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string NameOf(string project) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(project));

    private static int? VersionOf(EngineDetection detection) =>
        detection.Details.TryGetValue(VersionDetail, out var text)
        && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
        && version > 0
            ? version
            : null;

    private static string? KeyOf(EngineDetection detection) =>
        detection.Details.TryGetValue(ScriptKeyDetail, out var key) ? key : null;
}
