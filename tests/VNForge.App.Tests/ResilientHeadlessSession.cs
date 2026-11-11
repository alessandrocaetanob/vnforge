using Avalonia.Headless;
using SkiaSharp;

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
    private const int PurgeEvery = 25;

    private static int dispatches;

    public async Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        try
        {
            await RunWithRetry(async () =>
            {
                await session.Dispatch(action, cancellationToken);
                return 0;
            });
        }
        finally
        {
            ReleaseSkiaCaches();
        }
    }

    public async Task<TResult> Dispatch<TResult>(Func<TResult> action, CancellationToken cancellationToken)
    {
        try
        {
            return await RunWithRetry(() => session.Dispatch(action, cancellationToken));
        }
        finally
        {
            ReleaseSkiaCaches();
        }
    }

    public async Task<TResult> Dispatch<TResult>(Func<Task<TResult>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await RunWithRetry(() => session.Dispatch(action, cancellationToken));
        }
        finally
        {
            ReleaseSkiaCaches();
        }
    }

    /// <summary>
    /// Cada Dispatch sobe um aplicativo isolado e carrega de novo as fontes embutidas; o Skia guarda os tipos de letra e os glifos de todas as janelas já
    /// fechadas em um cache global que só se esvazia sob pedido (medido: cerca de 17 MB por janela principal aberta). Sem esta chamada a memória só cresce,
    /// mesmo com o coletor do .NET rodando. Limpar a cada Dispatch custava cerca de 180 ms cada (65 s em 362 Dispatch, uns 20% da suíte do App); a cada
    /// <see cref="PurgeEvery"/> Dispatch são 14 limpezas, 3 s, e o pico do processo cai igual: 1.900 MB contra 2.200 MB sem limpar.
    /// </summary>
    private static void ReleaseSkiaCaches()
    {
        if (Interlocked.Increment(ref dispatches) % PurgeEvery != 0)
        {
            return;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        SKGraphics.PurgeFontCache();
    }

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
