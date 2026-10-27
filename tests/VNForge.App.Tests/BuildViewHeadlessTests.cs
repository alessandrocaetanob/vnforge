using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Time.Testing;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

/// <summary>A tela Build e injeção de verdade (tema, estilos, recursos): abre, mostra o que muda no jogo e habilita só os botões que valem.</summary>
public class BuildViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static Window Abrir(BuildViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new BuildView { DataContext = vm, TechnicalExpanded = true } };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static Button Botao(Window window, string name) =>
        window.GetVisualDescendants().OfType<Button>().Single(b => b.GetValue(Avalonia.Automation.AutomationProperties.NameProperty) == name);

    [Fact]
    public async Task Tela_mostra_os_botoes_habilitados_conforme_o_estado_e_a_lista_do_que_muda()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "ysbin.ypf"), "original");
        File.WriteAllText(Path.Combine(game, "jogo.exe"), "MZ");
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        File.WriteAllText(Path.Combine(folder.BuildDirectory, "ysbin.ypf"), "traduzido");
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);

                Assert.True(Botao(window, "Injetar no jogo").IsEffectivelyEnabled);
                Assert.True(Botao(window, "Abrir jogo").IsEffectivelyEnabled);
                Assert.False(Botao(window, "Restaurar originais").IsEffectivelyEnabled);
                Assert.True(Botao(window, "Novo build").IsEffectivelyEnabled);

                vm.InjectCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(Botao(window, "Confirmar").IsEffectivelyVisible);
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ysbin.ypf");
            },
            CancellationToken.None);
    }

    /// <summary>Um build pronto e um jogo que mudou depois do plano: a injeção falha e a tela ganha banner de erro e linhas de log.</summary>
    private static async Task<BuildViewModel> ComErroEmLog(TempFolder temp)
    {
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "ysbin.ypf"), "original");
        File.WriteAllText(Path.Combine(game, "jogo.exe"), "MZ");
        var folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        File.WriteAllText(Path.Combine(folder.BuildDirectory, "ysbin.ypf"), "traduzido");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 20, 19, 8, TimeSpan.Zero));
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter(), clock: clock);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        vm.InjectCommand.Execute(null);
        File.WriteAllText(Path.Combine(game, "ysbin.ypf"), "mudou depois do plano");
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.NotEmpty(vm.Log);
        return vm;
    }

    [Fact]
    public async Task Banner_de_erro_e_linhas_do_log_deixam_selecionar_o_texto()
    {
        using var temp = new TempFolder();
        var vm = await ComErroEmLog(temp);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var selecionaveis = window.GetVisualDescendants().OfType<SelectableTextBlock>().ToList();

                Assert.Contains(selecionaveis, t => t.Text == vm.ErrorMessage);
                Assert.All(
                    selecionaveis,
                    t => Assert.Contains(((MenuFlyout)t.ContextFlyout!).Items.OfType<MenuItem>(), i => i.Header as string == "Copiar"));
                foreach (var linha in vm.Log)
                {
                    Assert.Contains(selecionaveis, t => t.Inlines?.OfType<Run>().Any(r => r.Text!.Contains(linha.Message, StringComparison.Ordinal)) == true);
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Copiar_log_poe_o_log_inteiro_com_data_e_hora_na_area_de_transferencia()
    {
        using var temp = new TempFolder();
        var vm = await ComErroEmLog(temp);

        await fixture.Session.Dispatch(
            async () =>
            {
                var window = Abrir(vm);
                var botao = Botao(window, "Copiar log");
                Assert.True(botao.IsEffectivelyEnabled);

                botao.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                var copiado = await window.Clipboard!.TryGetTextAsync();
                Assert.Equal(vm.LogText, copiado);
                Assert.StartsWith("2026-10-26 20:19:08", copiado, StringComparison.Ordinal);
                foreach (var linha in vm.Log)
                {
                    Assert.Contains(linha.Message, copiado, StringComparison.Ordinal);
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Copiar_log_fica_desabilitado_enquanto_nao_ha_log()
    {
        using var temp = new TempFolder();
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], new NoopStarter());

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);

                Assert.False(Botao(window, "Copiar log").IsEffectivelyEnabled);
            },
            CancellationToken.None);
    }

    private sealed class NoopStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
