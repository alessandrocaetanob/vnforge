using Microsoft.Extensions.Logging;
using VNForge.Core.Projects;

namespace VNForge.Core.Logging;

/// <summary>Único ponto de configuração de log; a CLI e o app chamam o mesmo código.</summary>
public static class VNForgeLogging
{
    /// <summary>Diretório dos logs de um projeto: <c>.cache/logs</c> dentro da raiz.</summary>
    public static string GetLogDirectory(string projectRoot) => Path.Combine(projectRoot, ".cache", "logs");

    /// <summary>Diretório de logs fora de qualquer projeto: <c>%LOCALAPPDATA%/VNForge/logs</c>.</summary>
    public static string GetUserLogDirectory() =>
        GetUserLogDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Como <see cref="GetUserLogDirectory()"/>, mas usa a pasta temporária se <paramref name="localApplicationData"/> vier vazia ou relativa, para nunca gravar relativo ao diretório atual.</summary>
    public static string GetUserLogDirectory(string? localApplicationData)
    {
        var baseDirectory = !string.IsNullOrWhiteSpace(localApplicationData) && Path.IsPathRooted(localApplicationData)
            ? localApplicationData
            : Path.GetTempPath();
        return Path.Combine(baseDirectory, "VNForge", "logs");
    }

    /// <summary>
    /// Escolhe onde gravar o log sem criar <c>.cache</c> em pasta qualquer (pode ser a pasta do jogo).
    /// Usa <c>.cache/logs</c> da pasta de <paramref name="project"/>, ou do diretório atual sem <paramref name="project"/>,
    /// só quando essa raiz tem <c>vnforge.json</c>; nos demais casos usa <paramref name="userLogDirectory"/>.
    /// </summary>
    public static string ResolveLogDirectory(string? project, string currentDirectory, string userLogDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userLogDirectory);

        var root = string.IsNullOrWhiteSpace(project) ? currentDirectory : Path.GetFullPath(project, currentDirectory);
        return File.Exists(Path.Combine(root, ProjectFolder.ConfigFileName)) ? GetLogDirectory(root) : userLogDirectory;
    }

    /// <summary>Indica se a última gravação de log em <paramref name="logDirectory"/> falhou (disco, permissão, caminho inválido).</summary>
    public static bool HasWriteFailed(string logDirectory) => FileLoggerProvider.WriteFailed(logDirectory);

    /// <summary>Registra o log em arquivo em <c>.cache/logs</c> do projeto, para hosts que usam <see cref="ILoggingBuilder"/>.</summary>
    public static ILoggingBuilder AddVNForgeLogging(
        this ILoggingBuilder builder,
        string projectRoot,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        return builder.AddVNForgeLoggingToDirectory(GetLogDirectory(projectRoot), minimumLevel);
    }

    /// <summary>Registra o log em arquivo direto em <paramref name="logDirectory"/>.</summary>
    public static ILoggingBuilder AddVNForgeLoggingToDirectory(
        this ILoggingBuilder builder,
        string logDirectory,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);

        builder.SetMinimumLevel(minimumLevel);
        return builder.AddProvider(new FileLoggerProvider(logDirectory));
    }

    /// <summary>Cria uma fábrica de loggers já configurada; quem chama descarta a fábrica ao terminar.</summary>
    public static ILoggerFactory CreateFactory(string projectRoot, LogLevel minimumLevel = LogLevel.Information) =>
        LoggerFactory.Create(builder => builder.AddVNForgeLogging(projectRoot, minimumLevel));

    /// <summary>Como <see cref="CreateFactory"/>, mas com o diretório de logs já resolvido (ver <see cref="ResolveLogDirectory"/>).</summary>
    public static ILoggerFactory CreateFactoryForDirectory(string logDirectory, LogLevel minimumLevel = LogLevel.Information) =>
        LoggerFactory.Create(builder => builder.AddVNForgeLoggingToDirectory(logDirectory, minimumLevel));
}