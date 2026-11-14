using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using VNForge.App.Services;

namespace VNForge.App.Tests;

/// <summary>Ponto de entrada da sessão headless: sobe o <see cref="App"/> de verdade (tema, estilos e recursos), sem janela do sistema.</summary>
public static class HeadlessEntry
{
    // O Skia pede ao sistema uma fonte padrão; um contêiner Linux sem fontes não tem nenhuma e o FontManager lança.
    // Apontar para a fonte embutida deixa o resultado igual em qualquer máquina.
    public static AppBuilder BuildAvaloniaApp()
    {
        // O relógio das animações não anda no headless: sem isto uma página recém-trocada ficaria parada no primeiro quadro (opacidade 0).
        _ = MotionPreferences.Force(false);
        return Configure();
    }

    private static AppBuilder Configure() =>
        AppBuilder.Configure<App>()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://VNForge.App/Assets/Fonts#Noto Sans" })
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Uma sessão para o processo todo: o Avalonia só aceita uma instância de aplicativo por thread de UI, então as classes de teste a dividem.</summary>
public sealed class HeadlessFixture
{
    private static readonly Lock Gate = new();
    private static HeadlessUnitTestSession? shared;

    // Um Lazy guardaria a exceção da primeira tentativa e todo teste falharia igual, sem a causa; aqui uma falha de subida não fica guardada e
    // chega aos testes com a mensagem original.
    private static HeadlessUnitTestSession Shared()
    {
        lock (Gate)
        {
            if (shared is not null)
            {
                return shared;
            }

            var session = HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry), AvaloniaTestIsolationLevel.PerAssembly);
            try
            {
                // Um Application só: o Dispatcher.UIThread pertence à thread da sessão desde o primeiro Dispatch, e qualquer teste que o leia antes dele
                // (um ViewModel que posta na interface) criaria um dispatcher preso à thread do xUnit.
                ResilientHeadlessSession.RunWithRetry(async () =>
                {
                    await session.Dispatch(() => 0, CancellationToken.None);
                    return 0;
                }).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"A sessão headless não subiu: {e.Message}", e);
            }

            return shared = session;
        }
    }

    /// <summary>Sobe a sessão antes do primeiro teste do assembly (<see cref="HeadlessSessionFixture"/>).</summary>
    internal static void Warm() => _ = Shared();

    // Sem Dispose: Session.Dispose() trava quando chamado da thread do xUnit; a thread da sessão não segura o processo.
    public ResilientHeadlessSession Session { get; } = new(Shared());
}

/// <summary>Fixture do assembly: abre a sessão headless uma vez, antes de qualquer teste, ver <see cref="HeadlessFixture"/>.</summary>
public sealed class HeadlessSessionFixture
{
    public HeadlessSessionFixture() => HeadlessFixture.Warm();
}
