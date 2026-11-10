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

    // Dado de teste: os valores que o quadro desenha, para a comparação ser de layout e não de conteúdo.
    private const string Credits = "Tradução: Equipe Chuva Mansa · Revisão: Hana";

    /// <summary>O LEIA-ME que o Core grava (o mesmo texto de <c>ReleaseDocuments.ReadMe</c>), com as falas de exemplo do quadro.</summary>
    private const string ReadMeText =
        "Amaoto no Hakoniwa · Tradução para português do Brasil\r\nVersão 1.2.0 · 08/11/2026\r\n\r\n"
        + "COMO INSTALAR\r\n1. Feche o jogo.\r\n2. Execute o instalador.\r\n3. Aponte a pasta do jogo e clique em Instalar.\r\n\r\n"
        + "COMO DESINSTALAR\r\nExecute o instalador de novo e escolha\r\nVoltar ao jogo original.\r\n\r\n"
        + "O QUE MUDOU DESDE A 1.1.0\r\n41 linhas em 6 cenas. Veja MUDANCAS.txt.\r\n\r\n"
        + "CRÉDITOS\r\nTradução: Equipe Chuva Mansa\r\nRevisão: Hana\r\n\r\n"
        + "AVISOS\r\nO instalador usa a biblioteca VCDiff (Apache 2.0) e outras bibliotecas de código aberto. Os textos das licenças estão em LICENCAS.txt e no próprio instalador (instalador --licencas).\r\n";

    /// <summary>Os tempos do quadro: 0,4 s, 2,9 s e 11,6 s nos três primeiros passos, e 11,6 s na verificação.</summary>
    private static TimeSpan DurationsOfQuadro(VNForge.Core.Publishing.ReleaseStep step) => step switch
    {
        VNForge.Core.Publishing.ReleaseStep.Check => TimeSpan.FromSeconds(0.4),
        VNForge.Core.Publishing.ReleaseStep.Build => TimeSpan.FromSeconds(2.9),
        VNForge.Core.Publishing.ReleaseStep.Diff => TimeSpan.FromSeconds(11.6),
        VNForge.Core.Publishing.ReleaseStep.Verify => TimeSpan.FromSeconds(11.6),
        _ => TimeSpan.FromSeconds(1),
    };

    /// <summary>A pasta de saída que o quadro mostra; a real do teste é temporária, então o texto entra só na exibição.</summary>
    private const string QuadroOutput = @"H:\Releases\Amaoto no Hakoniwa\1.2.0";

    private static void Render(ReleaseViewModelTestsHost host, string screen, ThemeVariant theme, Action<Window> inspect, Action<Window>? prepare = null)
    {
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(screen, theme));
        var view = new ReleaseView { DataContext = host.Vm };
        try
        {
            ScreenRenderer.Render(view, theme, path, inspect: inspect, prepare: prepare);
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
        using var host = await ReleaseViewModelTestsHost.OpenAsync(ReleaseViewModelTests.ReadyLikeQuadro(), ReleaseViewModelTests.QuadroCounts);
        host.Vm.Credits = Credits;
        host.Vm.OutputDirectory = QuadroOutput;

        await fixture.Session.Dispatch(
            () =>
            {
                Render(host, "lancar-pronto", ThemeOf(themeName), window =>
                {
                    Assert.True(Shows(window, "O que já confere"));
                    Assert.True(Shows(window, "A versão"));
                    Assert.True(Shows(window, "O que o jogador recebe"));
                    Assert.True(Shows(window, "O que mudou desde o patch anterior"));
                    Assert.True(Shows(window, "O que o patch muda no jogo"));
                });
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_estado_gerando(string themeName)
    {
        using var host = await ReleaseViewModelTestsHost.OpenAsync(ReleaseViewModelTests.ReadyLikeQuadro(), ReleaseViewModelTests.QuadroCounts);
        host.Vm.Credits = Credits;
        host.Publisher.Duration = DurationsOfQuadro;
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
                        Assert.True(Shows(window, "O que o patch muda no jogo"));
                        Assert.True(Shows(window, "Andamento da etapa"));
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
        using var host = await ReleaseViewModelTestsHost.OpenAsync(ReleaseViewModelTests.ReadyLikeQuadro(), ReleaseViewModelTests.QuadroCounts);
        host.Vm.Credits = Credits;
        host.Publisher.Duration = DurationsOfQuadro;
        host.Publisher.ReadMe = ReadMeText;
        await host.Vm.GenerateCommand.ExecuteAsync(null);

        await fixture.Session.Dispatch(
            () =>
            {
                Render(host, "lancar-feito", ThemeOf(themeName), window =>
                {
                    Assert.True(Shows(window, "Arquivos gerados"));
                    Assert.True(Shows(window, "Verificação"));
                    Assert.True(Shows(window, "LEIA-ME.txt"));
                },
                prepare: window => window.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Single(t => t.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == "Pasta de saída")
                    .Text = QuadroOutput + @"\");
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_jogo_fora_da_versao_de_referencia(string themeName)
    {
        using var host = await ReleaseViewModelTestsHost.OpenAsync(ReleaseViewModelTests.ReadyLikeQuadro(VNForge.App.Services.ReleaseReference.Differs));

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

    public static async Task<ReleaseViewModelTestsHost> OpenAsync(VNForge.App.Services.ReleaseReadiness? readiness = null, VNForge.Core.Projects.StatusCounts? counts = null)
    {
        var (vm, publisher, temp, _) = await ReleaseViewModelTests.OpenAsync(readiness, counts);
        return new ReleaseViewModelTestsHost(vm, publisher, temp);
    }

    public void Dispose() => temp.Dispose();
}
