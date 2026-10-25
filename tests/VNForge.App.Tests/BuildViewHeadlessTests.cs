using Avalonia.Controls;
using Avalonia.Threading;
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

    private sealed class NoopStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory)
        {
        }
    }
}
