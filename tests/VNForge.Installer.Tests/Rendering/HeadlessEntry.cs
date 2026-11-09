using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Skia;

namespace VNForge.Installer.Tests.Rendering;

/// <summary>Ponto de entrada da sessão headless: sobe o <see cref="InstallerApplication"/> de verdade (tema, estilos e recursos), sem janela do sistema.</summary>
public static class HeadlessEntry
{
    // O Skia pede ao sistema uma fonte padrão; um contêiner Linux sem fontes não tem nenhuma e o FontManager lança.
    // Apontar para a fonte embutida deixa o resultado igual em qualquer máquina.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<InstallerApplication>()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://vnf-instalador/Assets/Fonts#Noto Sans" })
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Uma sessão para o processo todo: o Avalonia só aceita uma instância de aplicativo por thread de UI, então as classes de teste a dividem.</summary>
public sealed class HeadlessFixture
{
    private static readonly Lazy<HeadlessUnitTestSession> Shared = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry)));

    // Sem Dispose: Session.Dispose() trava quando chamado da thread do xUnit; a thread da sessão não segura o processo.
    public HeadlessUnitTestSession Session { get; } = Shared.Value;
}
