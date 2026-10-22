using Microsoft.Extensions.Logging;

namespace VNForge.Core.Logging;

/// <summary>Único ponto de configuração de log; a CLI e o app chamam o mesmo código.</summary>
public static class VNForgeLogging
{
    /// <summary>Diretório dos logs de um projeto: <c>.cache/logs</c> dentro da raiz.</summary>
    public static string GetLogDirectory(string projectRoot) => Path.Combine(projectRoot, ".cache", "logs");

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

        builder.SetMinimumLevel(minimumLevel);
        return builder.AddProvider(new FileLoggerProvider(GetLogDirectory(projectRoot)));
    }

    /// <summary>Cria uma fábrica de loggers já configurada; quem chama descarta a fábrica ao terminar.</summary>
    public static ILoggerFactory CreateFactory(string projectRoot, LogLevel minimumLevel = LogLevel.Information) =>
        LoggerFactory.Create(builder => builder.AddVNForgeLogging(projectRoot, minimumLevel));
}
