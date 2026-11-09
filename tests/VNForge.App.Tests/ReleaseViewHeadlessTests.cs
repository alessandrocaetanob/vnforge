using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Publishing;

namespace VNForge.App.Tests;

/// <summary>A tela Lançar de verdade (tema, estilos, recursos): cada fase mostra os blocos e os botões do quadro aprovado e só eles.</summary>
public sealed class ReleaseViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static Window Abrir(ReleaseViewModel vm)
    {
        var window = new Window { Width = 1440, Height = 1000, Content = new ReleaseView { DataContext = vm } };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static IEnumerable<Control> Visiveis(Window window, string name) =>
        window.GetVisualDescendants().OfType<Control>().Where(c => c.GetValue(AutomationProperties.NameProperty) == name && c.IsEffectivelyVisible);

    private static bool Mostra(Window window, string name) => Visiveis(window, name).Any();

    private static IEnumerable<string> Textos(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty);

    private static void SemTravessao(Window window) =>
        Assert.All(Textos(window), t => Assert.DoesNotContain(t, c => c is '—' or '–'));

    [Fact]
    public async Task Pronta_mostra_as_conferencias_o_formulario_e_o_botao_de_gerar_habilitado()
    {
        var (vm, _, temp, _) = await ReleaseViewModelTests.OpenAsync();
        using var _t = temp;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    Assert.True(Mostra(window, "O que já confere"));
                    Assert.True(Mostra(window, "A versão"));
                    Assert.True(Mostra(window, "O que o jogador recebe"));
                    Assert.True(Mostra(window, "O que mudou desde o patch anterior"));
                    Assert.True(Mostra(window, "Ver o Testar"));
                    Assert.False(Mostra(window, "Ver os erros"));
                    Assert.False(Mostra(window, "Etapas"));
                    Assert.False(Mostra(window, "Arquivos gerados"));
                    Assert.False(Mostra(window, "Abrir a pasta"));
                    var gerar = Assert.Single(Visiveis(window, "Gerar patch e instalador"));
                    Assert.True(gerar.IsEffectivelyEnabled);
                    Assert.Contains("accent", gerar.Classes);
                    Assert.Single(Textos(window), "Mudou desde o patch 1.1.0");
                    Assert.Contains("Última versão: 1.1.0 · 02/10", Textos(window));
                    SemTravessao(window);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Com_erros_na_conferencia_o_gerar_fica_desabilitado_e_a_faixa_oferece_os_erros()
    {
        var (vm, _, temp, _) = await ReleaseViewModelTests.OpenAsync(ReleaseViewModelTests.Ready(errors: 2));
        using var _t = temp;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    Assert.False(Assert.Single(Visiveis(window, "Gerar patch e instalador")).IsEffectivelyEnabled);
                    Assert.True(Mostra(window, "Ver os erros"));
                    Assert.False(Mostra(window, "Ver o Testar"));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Gerando_mostra_as_seis_etapas_e_troca_o_botao_da_faixa_por_cancelar()
    {
        var (vm, publisher, temp, _) = await ReleaseViewModelTests.OpenAsync();
        using var _t = temp;
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        publisher.Blocker = _ =>
        {
            if (++calls == 3)
            {
                reached.Set();
                release.Wait(TimeSpan.FromMinutes(3));
            }

            return false;
        };

        var running = vm.GenerateCommand.ExecuteAsync(null);
        await Task.Run(() => reached.Wait(TimeSpan.FromMinutes(3)), TestContext.Current.CancellationToken);
        try
        {
            await fixture.Session.Dispatch(
                () =>
                {
                    var window = Abrir(vm);
                    try
                    {
                        Assert.True(Mostra(window, "Etapas"));
                        Assert.False(Mostra(window, "A versão"));
                        Assert.True(Mostra(window, "Cancelar"));
                        var gerar = Assert.Single(Visiveis(window, "Gerar patch e instalador"));
                        Assert.False(gerar.IsEffectivelyEnabled);
                        Assert.Contains("Gerando...", Textos(window));
                        Assert.Contains("Gerando a versão 1.2.0", Textos(window));
                        Assert.Contains("Calcular as diferenças e gravar o patch", Textos(window));
                        Assert.Single(window.GetVisualDescendants().OfType<ProgressBar>(), p => p.IsEffectivelyVisible && p.IsIndeterminate);
                        SemTravessao(window);
                    }
                    finally
                    {
                        // A geração segue em segundo plano depois do teste; sem a tela ligada, o que ela muda no ViewModel não toca em controle.
                        ((ReleaseView)window.Content!).DataContext = null;
                        window.Close();
                    }
                },
                CancellationToken.None);
        }
        finally
        {
            release.Set();
            await running;
        }
    }

    [Fact]
    public async Task Versao_pronta_mostra_os_arquivos_com_copiar_checksum_so_nos_pacotes_e_o_leia_me()
    {
        var (vm, _, temp, _) = await ReleaseViewModelTests.OpenAsync();
        using var _t = temp;
        await vm.GenerateCommand.ExecuteAsync(null);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    Assert.True(Mostra(window, "Arquivos gerados"));
                    Assert.True(Mostra(window, "Verificação"));
                    Assert.True(Mostra(window, "O que o patch muda no jogo"));
                    Assert.True(Mostra(window, "LEIA-ME.txt"));
                    Assert.False(Mostra(window, "O que o jogador recebe"));
                    Assert.False(Mostra(window, "A versão"));
                    Assert.Equal(2, Visiveis(window, "Copiar checksum").Count());
                    Assert.True(Mostra(window, "Abrir a pasta"));
                    Assert.True(Mostra(window, "Testar o instalador"));
                    Assert.Contains("accent", Assert.Single(Visiveis(window, "Abrir a pasta")).Classes);
                    Assert.Contains("Versão 1.2.0 pronta", Textos(window));
                    Assert.Contains(Textos(window), t => t.Contains("COMO INSTALAR", StringComparison.Ordinal));
                    SemTravessao(window);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Fora_da_versao_de_referencia_trava_o_gerar_e_so_mostra_os_detalhes_a_pedido()
    {
        var read = ReleaseViewModelTests.Ready(reference: ReleaseReference.Differs, problem: "O jogo mudou desde a extração. Arquivos alterados: Game/Game.exe.");
        var (vm, _, temp, _) = await ReleaseViewModelTests.OpenAsync(read);
        using var _t = temp;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    Assert.True(Mostra(window, "Checksum diferente"));
                    Assert.True(Mostra(window, "O que fazer"));
                    Assert.True(Mostra(window, "Escolher outra pasta"));
                    Assert.True(Mostra(window, "Ver os detalhes"));
                    Assert.False(Mostra(window, "Detalhes do checksum"));
                    Assert.False(Assert.Single(Visiveis(window, "Gerar patch e instalador")).IsEffectivelyEnabled);
                    Assert.False(Mostra(window, "A versão"));
                    Assert.Contains("O patch só vale para a versão que o projeto usou. Nada foi gerado.", Textos(window));

                    vm.ToggleProblemDetailsCommand.Execute(null);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.True(Mostra(window, "Detalhes do checksum"));
                    SemTravessao(window);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }
}
