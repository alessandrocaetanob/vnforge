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
    public async Task Pronta_com_patch_anterior_mostra_a_tabela_estimada_e_o_antes_e_o_depois_das_falas()
    {
        var read = ReleaseViewModelTests.ReadyWithPreviousPatch() with
        {
            ChangeExamples = [new ChangeExample(1, 2, "Lá fora, a chuva ainda não parou.", "Lá fora, a chuva não parou desde cedo.")],
        };
        var (vm, _, temp, _) = await ReleaseViewModelTests.OpenAsync(read);
        using var _t = temp;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    Assert.True(Mostra(window, "O que o patch muda no jogo"));
                    Assert.Contains("Game/script.ypf", Textos(window));
                    Assert.Contains("4 de 312 arquivos tocados", Textos(window));
                    Assert.Contains("Cena 1 · linha 2", Textos(window));
                    Assert.Contains("Lá fora, a chuva ainda não parou.", Textos(window));
                    Assert.Contains("e mais 40 linhas em 2 cenas", Textos(window));
                    Assert.Contains("5,1 MB", Textos(window));
                    Assert.Contains("9 KB", Textos(window));
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
                        Assert.Contains("Calcular as diferenças", Textos(window));
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
    public async Task Fora_da_versao_de_referencia_trava_o_gerar_e_so_mostra_os_detalhes_a_pedido()
    {
        var read = ReleaseViewModelTests.Ready(reference: ReleaseReference.Differs, problem: "O jogo mudou desde a extração. Arquivos alterados: Game/Game.exe.")
            with { ChangedFiles = [new VNForge.Core.Injection.ChangedGameFile("Game/Game.exe", "3f9a" + new string('0', 56) + "c21d", "77b0" + new string('1', 56) + "e04a")] };
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
                    Assert.Contains("Esperado (referência)", Textos(window));
                    Assert.Contains("Encontrado na pasta", Textos(window));
                    Assert.Contains("SHA-256 3f9a…c21d", window.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? string.Empty));
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
