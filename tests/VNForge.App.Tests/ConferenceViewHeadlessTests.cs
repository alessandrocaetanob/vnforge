using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O Avalonia só aceita um aplicativo por thread de UI: toda classe de teste headless entra nesta coleção e divide a mesma sessão.</summary>
[CollectionDefinition(Name)]
public sealed class HeadlessCollection : ICollectionFixture<HeadlessFixture>
{
    public const string Name = "Headless";
}

/// <summary>Abre a tela de verdade (tema, estilos e DataTemplates por tipo de linha) para pegar erro de XAML que o ViewModel sozinho não mostra.</summary>
[Collection(HeadlessCollection.Name)]
public class ConferenceViewHeadlessTests(HeadlessFixture fixture)
{
    [Fact]
    public async Task Duplo_clique_na_ocorrencia_pede_a_linha()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        ProjectFolder.Open(root).SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", "duas  palavras", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();
        LineRequest? requested = null;
        vm.LineRequested += (_, request) => requested = request;

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new ConferenceView { DataContext = vm } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var cell = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "yst00001:000000");
                var center = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);
                window.MouseDown(center, MouseButton.Left);
                window.MouseUp(center, MouseButton.Left);

                Assert.Equal(new LineRequest("yst00001", "yst00001:000000"), requested);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Tela_desenha_cabecalho_de_regra_ocorrencia_e_os_botoes()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", "duas  palavras", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();

        await fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new ConferenceView { DataContext = vm } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("espaco-duplo", texts);
                Assert.Contains("yst00001:000000", texts);
                Assert.Contains("Corrigir esta regra", texts);
                Assert.Contains("Abrir linha", texts);
                Assert.Contains("Marcar como ok", texts);
                Assert.Contains("Corrigir automaticamente", texts);
            },
            CancellationToken.None);
    }
}
