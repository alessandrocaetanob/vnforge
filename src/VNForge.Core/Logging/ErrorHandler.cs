using Microsoft.Extensions.Logging;

namespace VNForge.Core.Logging;

/// <summary>
/// Converte exceções em saída para o usuário: <see cref="VNForgeException"/> vira uma linha em português,
/// cancelamento vira "Operação cancelada." e qualquer outra exceção vira um aviso genérico e vai completa para o log.
/// Se o log não puder ser gravado, o erro inesperado sai completo no próprio <c>error</c>, sem prometer o arquivo.
/// </summary>
/// <remarks>
/// Uso na CLI: desligue o tratador padrão do System.CommandLine
/// (<c>InvocationConfiguration.EnableDefaultExceptionHandler = false</c>, senão ele imprime o stack no stderr antes
/// deste tratador) e rode a invocação inteira dentro de <see cref="ExecuteAsync"/>:
/// <c>ErrorHandler.ExecuteAsync(ct =&gt; parseResult.InvokeAsync(configuration, ct), logger, Console.Error, logDirectory, ct)</c>.
/// O valor devolvido é o código de saída do processo.
/// </remarks>
public static class ErrorHandler
{
    public const int ExpectedErrorExitCode = 1;

    public const int UnexpectedErrorExitCode = 2;

    /// <summary>Convenção de shell para SIGINT (128 + 2).</summary>
    public const int CancelledExitCode = 130;

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

    public static async Task<int> ExecuteAsync(
        Func<CancellationToken, Task<int>> action,
        ILogger logger,
        TextWriter error,
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
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

        switch (exception)
        {
            case OperationCanceledException:
                error.WriteLine("Operação cancelada.");
                TryLog(() => logger.LogInformation("Operação cancelada pelo usuário"));
                return CancelledExitCode;

            case VNForgeException:
                error.WriteLine($"Erro: {exception.Message}");
                TryLog(() => logger.LogWarning(exception, "Erro esperado: {Mensagem}", exception.Message));
                return ExpectedErrorExitCode;

            default:
                var logged = TryLog(() => logger.LogError(exception, "Erro inesperado")) &&
                    !VNForgeLogging.HasWriteFailed(logDirectory);
                if (logged)
                {
                    error.WriteLine($"Erro inesperado. O log completo está em: {logDirectory}");
                }
                else
                {
                    error.WriteLine($"Erro inesperado (não foi possível gravar o log em {logDirectory}):");
                    error.WriteLine(exception.ToString());
                }

                return UnexpectedErrorExitCode;
        }
    }

    private static bool TryLog(Action log)
    {
        try
        {
            log();
            return true;
        }
        catch (Exception)
        {
            // O log é auxiliar: nunca pode esconder a mensagem nem o erro original.
            return false;
        }
    }
}
