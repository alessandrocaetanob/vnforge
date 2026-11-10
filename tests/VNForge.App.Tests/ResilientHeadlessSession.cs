using Avalonia.Headless;

namespace VNForge.App.Tests;

/// <summary>
/// Envolve a <see cref="HeadlessUnitTestSession"/> e repete uma vez o Dispatch que falha na preparação da sessão (issue #510).
/// Com o isolamento por teste, todo Dispatch começa em <c>EnsureIsolatedApplication</c>, que zera o <c>Dispatcher.UIThread</c> estático e só o retoma
/// dentro de <c>SetupUnsafe</c>. Se outra thread ler <c>Dispatcher.UIThread</c> nesse intervalo, o Avalonia cria um dispatcher preso a ela e a
/// thread da sessão leva "a different thread owns it" (https://github.com/AvaloniaUI/Avalonia/issues/22021, aberto no 12.1.x).
/// A exceção sai antes de o delegado do teste começar, então repetir não roda o corpo do teste duas vezes.
/// Qualquer outra exceção, e a segunda falha, seguem sem alteração.
/// </summary>
public sealed class ResilientHeadlessSession(HeadlessUnitTestSession session)
{
    public Task Dispatch(Action action, CancellationToken cancellationToken) =>
        RunWithRetry(async () =>
        {
            await session.Dispatch(action, cancellationToken);
            return 0;
        });

    public Task<TResult> Dispatch<TResult>(Func<TResult> action, CancellationToken cancellationToken) =>
        RunWithRetry(() => session.Dispatch(action, cancellationToken));

    public Task<TResult> Dispatch<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken) =>
        RunWithRetry(() => session.Dispatch(action, cancellationToken));

    /// <summary>Só a falha de preparação conta: a pilha passa por <c>EnsureIsolatedApplication</c>, antes de qualquer código do teste.</summary>
    internal static bool IsSetupRace(Exception exception) =>
        exception is InvalidOperationException && exception.StackTrace?.Contains("EnsureIsolatedApplication", StringComparison.Ordinal) == true;

    internal static async Task<TResult> RunWithRetry<TResult>(Func<Task<TResult>> attempt, Action<string>? log = null)
    {
        try
        {
            return await attempt();
        }
        catch (Exception ex) when (IsSetupRace(ex))
        {
            (log ?? LogToTestOutput)($"Dispatch repetido uma vez: corrida de preparação da sessão headless (issue #510): {ex.Message}");
            return await attempt();
        }
    }

    private static void LogToTestOutput(string message) => TestContext.Current.TestOutputHelper?.WriteLine(message);
}
