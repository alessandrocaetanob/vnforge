using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Views;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A tela Lançar (#540) a 1440×1000 nos dois temas, nos quatro estados do quadro: pronto, gerando, versão pronta e jogo fora da versão de referência.
/// Os PNG seguem <c>VNFORGE_RENDER_DIR</c> e se conferem lado a lado com o quadro.
/// </summary>
public sealed class ReleaseRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant ThemeOf(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static void Render(ReleaseViewModelTestsHost host, string screen, ThemeVariant theme, Action<Window> inspect)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(screen, theme));
        var view = new ReleaseView { DataContext = host.Vm };
        try
        {
            ScreenRenderer.Render(view, theme, path, inspect: inspect);
        }
        finally
        {
            // A geração do estado "gerando" segue em segundo plano depois do render; sem a tela ligada, o que ela muda no ViewModel não toca em controle.
            view.DataContext = null;
        }

        Assert.True(new FileInfo(path).Length > 10_000);
    }

    private static bool Shows(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Any(c => c.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name && c.IsEffectivelyVisible && c.Bounds.Height > 0);

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_estado_pronto(string themeName)
    {
        using var host = await ReleaseViewModelTestsHost.OpenAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                Render(host, "lancar-pronto", ThemeOf(themeName), window =>
                {
                    Assert.True(Shows(window, "O que já confere"));
                    Assert.True(Shows(window, "A versão"));
                    Assert.True(Shows(window, "O que o jogador recebe"));
                    Assert.True(Shows(window, "O que mudou desde o patch anterior"));
                });
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_estado_gerando(string themeName)
    {
        using var host = await ReleaseViewModelTestsHost.OpenAsync();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        host.Publisher.Blocker = _ =>
        {
            if (++calls == 4)
            {
                reached.Set();
                release.Wait(TimeSpan.FromMinutes(3));
            }

            return false;
        };

        var running = host.Vm.GenerateCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), TestContext.Current.CancellationToken);
        try
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    Render(host, "lancar-gerando", ThemeOf(themeName), window =>
                    {
                        Assert.True(Shows(window, "Etapas"));
                        Assert.True(Shows(window, "Cancelar"));
                    });
                    return 0;
                },
                TestContext.Current.CancellationToken);
        }
        finally
        {
            release.Set();
            await running;
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_a_versao_pronta(string themeName)
    {
        using var host = await ReleaseViewModelTestsHost.OpenAsync();
        host.Vm.Credits = "Tradução: Equipe Chuva Mansa · Revisão: Hana";
        await host.Vm.GenerateCommand.ExecuteAsync(null);

        await fixture.Session.Dispatch(
            () =>
            {
                Render(host, "lancar-feito", ThemeOf(themeName), window =>
                {
                    Assert.True(Shows(window, "Arquivos gerados"));
                    Assert.True(Shows(window, "Verificação"));
                    Assert.True(Shows(window, "LEIA-ME.txt"));
                });
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_jogo_fora_da_versao_de_referencia(string themeName)
    {
        var read = ReleaseViewModelTests.Ready(
            reference: VNForge.App.Services.ReleaseReference.Differs,
            problem: "O jogo mudou desde a extração. Arquivos alterados: Game/Game.exe.");
        using var host = await ReleaseViewModelTestsHost.OpenAsync(read);

        await fixture.Session.Dispatch(
            () =>
            {
                Render(host, "lancar-checksum", ThemeOf(themeName), window =>
                {
                    Assert.True(Shows(window, "Checksum diferente"));
                    Assert.True(Shows(window, "O que fazer"));
                });
                return 0;
            },
            TestContext.Current.CancellationToken);
    }
}

/// <summary>O ViewModel da tela Lançar com o publicador falso e a pasta temporária que o teste de render precisa manter viva.</summary>
internal sealed class ReleaseViewModelTestsHost : IDisposable
{
    private readonly TempFolder temp;

    private ReleaseViewModelTestsHost(VNForge.App.ViewModels.ReleaseViewModel vm, ReleaseViewModelTests.FakePublisher publisher, TempFolder temp)
    {
        Vm = vm;
        Publisher = publisher;
        this.temp = temp;
    }

    public VNForge.App.ViewModels.ReleaseViewModel Vm { get; }

    public ReleaseViewModelTests.FakePublisher Publisher { get; }

    public static async Task<ReleaseViewModelTestsHost> OpenAsync(VNForge.App.Services.ReleaseReadiness? readiness = null)
    {
        var (vm, publisher, temp, _) = await ReleaseViewModelTests.OpenAsync(readiness);
        return new ReleaseViewModelTestsHost(vm, publisher, temp);
    }

    public void Dispose() => temp.Dispose();
}
