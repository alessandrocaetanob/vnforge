using Microsoft.Extensions.Logging;

namespace VNForge.Core.Logging;

/// <summary>
/// Converte exceções em saída para o usuário: <see cref="VNForgeException"/> vira uma linha em português,
/// qualquer outra exceção vira um aviso genérico e vai completa para o log.
/// </summary>
public static class ErrorHandler
{
    public const int ExpectedErrorExitCode = 1;

    public const int UnexpectedErrorExitCode = 2;

    public static int Execute(Func<int> action, ILogger logger, TextWriter error, string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return Handle(ex, logger, error, logDirectory);
        }
    }

    public static async Task<int> ExecuteAsync(Func<Task<int>> action, ILogger logger, TextWriter error, string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Handle(ex, logger, error, logDirectory);
        }
    }

    public static int Handle(Exception exception, ILogger logger, TextWriter error, string logDirectory)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        if (exception is VNForgeException)
        {
            logger.LogWarning(exception, "Erro esperado: {Mensagem}", exception.Message);
            error.WriteLine($"Erro: {exception.Message}");
            return ExpectedErrorExitCode;
        }

        logger.LogError(exception, "Erro inesperado");
        error.WriteLine($"Erro inesperado. O log completo está em: {logDirectory}");
        return UnexpectedErrorExitCode;
    }
}
