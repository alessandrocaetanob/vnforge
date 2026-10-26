using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Abre a tela real do Painel (tema, estilos e bindings compilados) para pegar erro de XAML que os testes de ViewModel não veem.</summary>
public class DashboardViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    [Fact]
    public async Task Painel_renderiza_etapas_e_barras_do_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated);
        var vm = new DashboardViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.ReloadAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1400, Height = 900, Content = new DashboardView { DataContext = vm } };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();

                    var steps = window.GetVisualDescendants().OfType<PipelineStepItem>().ToList();
                    Assert.Equal(["Ler o texto", "Traduzir com a IA", "Revisar", "Conferir", "Testar no jogo"], steps.Select(s => s.Title));
                    Assert.Single(window.GetVisualDescendants().OfType<ProgressRing>());
                    Assert.Single(window.GetVisualDescendants().OfType<ChapterRow>());
                    Assert.NotEmpty(window.GetVisualDescendants().OfType<TodoItem>());
                    Assert.Single(window.GetVisualDescendants().OfType<BudgetBar>());
                    Assert.Single(window.GetVisualDescendants().OfType<GameStateBanner>());
                    Assert.Equal("Minha VN", window.GetVisualDescendants().OfType<PageHeader>().Single().Title);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);
    }
}
