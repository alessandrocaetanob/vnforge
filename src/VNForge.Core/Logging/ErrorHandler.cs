using Microsoft.Extensions.Logging;
using VNForge.Core.Output;

namespace VNForge.Core.Logging;

/// <summary>
/// Converte exceções em saída para o usuário: <see cref="VNForgeException"/> vira uma linha em português,
/// cancelamento vira "Operação cancelada." e qualquer outra exceção vira um aviso genérico e vai completa para o log.
/// Se o log não puder ser gravado, o erro inesperado sai completo no próprio <c>error</c>, sem prometer o arquivo.
/// Com <c>jsonOutput</c> (<c>--json</c>) o erro sai no formato de <see cref="JsonEnvelope"/>, sem stack trace.
/// </summary>
/// <remarks>
/// Uso na CLI: desligue o tratador padrão do System.CommandLine
/// (<c>InvocationConfiguration.EnableDefaultExceptionHandler = false</c>, senão ele imprime o stack no stderr antes
/// deste tratador) e rode a invocação inteira dentro de <see cref="ExecuteAsync"/>:
/// <c>ErrorHandler.ExecuteAsync(ct =&gt; parseResult.InvokeAsync(configuration, ct), logger, Console.Error, logDirectory, ct)</c>.
/// O valor devolvido é o código de saída do processo: 0 ok, 1 erro (esperado ou inesperado),
/// 2 QA bloqueante (devolvido pelo comando) e 3 cancelado.
/// </remarks>
public static class ErrorHandler
{
    public const int OkExitCode = 0;

    /// <summary>Erro esperado (<see cref="VNForgeException"/>) ou inesperado.</summary>
    public const int ErrorExitCode = 1;

    /// <summary>O QA encontrou erros bloqueantes; quem devolve é o comando, não este tratador.</summary>
    public const int QaBlockedExitCode = 2;

    public const int CancelledExitCode = 3;

    public static int Execute(
        Func<int> action,
        ILogger logger,
        TextWriter error,
        string logDirectory,
        TextWriter? jsonOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return Handle(ex, logger, error, logDirectory, jsonOutput, cancellationToken);
        }
    }

    public static async Task<int> ExecuteAsync(
        Func<CancellationToken, Task<int>> action,
        ILogger logger,
        TextWriter error,
        string logDirectory,
        CancellationToken cancellationToken = default,
        TextWriter? jsonOutput = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Handle(ex, logger, error, logDirectory, jsonOutput, cancellationToken);
        }
    }

    /// <param name="cancellationToken">
    /// Só conta como cancelamento (código 3) uma <see cref="OperationCanceledException"/> com este token cancelado;
    /// sem isso é um timeout ou erro de rede e vira erro inesperado.
    /// </param>
    /// <param name="jsonOutput">
    /// Quando informado (<c>--json</c>), o erro sai neste writer, uma linha de JSON, e nada vai para <paramref name="error"/>.
    /// </param>
    public static int Handle(
        Exception exception,
        ILogger logger,
        TextWriter error,
        string logDirectory,
        TextWriter? jsonOutput = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(error);

        void Report(
            string kind,
            string message,
            string? humanText = null,
            string? logPath = null,
            string? detail = null)
        {
            var exitCode = kind == JsonEnvelope.CancelledKind ? CancelledExitCode : ErrorExitCode;
            if (jsonOutput is null)
            {
                error.WriteLine(humanText ?? message);
            }
            else
            {
                jsonOutput.WriteLine(JsonEnvelope.Error(kind, message, exitCode, logPath, detail));
            }
        }

        switch (exception)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                Report(JsonEnvelope.CancelledKind, "Operação cancelada.");
                TryLog(() => logger.LogInformation("Operação cancelada pelo usuário"));
                return CancelledExitCode;

            case VNForgeException:
                Report(JsonEnvelope.ExpectedKind, exception.Message, humanText: $"Erro: {exception.Message}");
                TryLog(() => logger.LogWarning(exception, "Erro esperado: {Mensagem}", exception.Message));
                return ErrorExitCode;

            default:
                var logged = TryLog(() => logger.LogError(exception, "Erro inesperado")) &&
                    !VNForgeLogging.HasWriteFailed(logDirectory);
                if (logged)
                {
                    Report(
                        JsonEnvelope.UnexpectedKind,
                        "Erro inesperado.",
                        humanText: $"Erro inesperado. O log completo está em: {logDirectory}",
                        logPath: logDirectory);
                }
                else
                {
                    Report(
                        JsonEnvelope.UnexpectedKind,
                        $"Erro inesperado (não foi possível gravar o log em {logDirectory}).",
                        humanText: $"Erro inesperado (não foi possível gravar o log em {logDirectory}):{Environment.NewLine}{exception}",
                        detail: $"{exception.GetType().FullName}: {exception.Message}");
                }

                return ErrorExitCode;
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
