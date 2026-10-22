using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace VNForge.Core.Logging;

/// <summary>
/// Grava uma linha por registro (mais a exceção completa, se houver) em
/// <c>vnf-AAAAMMDD.log</c> dentro do diretório informado. Falha de E/S nunca sobe para quem loga.
/// </summary>
internal sealed class FileLoggerProvider(string logDirectory) : ILoggerProvider
{
    private static readonly UTF8Encoding Utf8SemBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly ConcurrentDictionary<string, bool> Failures = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

    public static bool WriteFailed(string directory) => Failures.ContainsKey(directory);

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = DateTimeOffset.Now;
        var text = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(category).Append(": ").Append(message)
            .AppendLine();

        if (exception is not null)
        {
            text.AppendLine(exception.ToString());
        }

        var path = Path.Combine(logDirectory, $"vnf-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(logDirectory);
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                var bytes = Utf8SemBom.GetBytes(text.ToString());
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        catch (Exception)
        {
            // O log é auxiliar: se o disco recusar, o comando segue sem ele.
            // O ErrorHandler consulta WriteFailed para não apontar para um arquivo que não existe.
            Failures[logDirectory] = true;
            return;
        }

        Failures.TryRemove(logDirectory, out _);
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
