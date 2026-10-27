using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Abre a tela de verdade (tema, estilos e DataTemplates por tipo de linha) para pegar erro de XAML que o ViewModel sozinho não mostra.</summary>
public class ConferenceViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
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

                var cell = window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Cena 1 · linha 1");
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
    public async Task Enter_corrige_e_vai_para_o_proximo_sem_tocar_no_mouse()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", "duas  palavras", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new ConferenceView { DataContext = vm } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                for (var i = 0; i < 100 && (vm.IsFixing || vm.NoticeMessage is null); i++)
                {
                    await Task.Delay(50);
                    Dispatcher.UIThread.RunJobs();
                }

                window.Close();
                return 0;
            },
            CancellationToken.None);

        Assert.Equal("duas palavras", folder.LoadText("yst00001")[0].Translation);
        Assert.Equal("1 correção aplicada.", vm.NoticeMessage);
    }

    [Fact]
    public async Task Enter_com_o_foco_num_botao_usa_o_botao_e_nao_corrige()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", "duas  palavras", TranslationStatus.Translated)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();
        LineRequest? requested = null;
        vm.LineRequested += (_, request) => requested = request;

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new ConferenceView { DataContext = vm } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                var open = window.GetVisualDescendants().OfType<Button>().Single(b => b.Command == vm.OpenLineCommand && b.IsEffectivelyVisible);
                open.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }

                window.Close();
                return 0;
            },
            CancellationToken.None);

        Assert.NotNull(requested);
        Assert.Null(vm.NoticeMessage);
        Assert.Equal("duas  palavras", folder.LoadText("yst00001")[0].Translation);
    }

    [Fact]
    public async Task Enter_sem_correcao_disponivel_nao_faz_nada()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var folder = ProjectFolder.Open(root);
        folder.SaveText("yst00001", [new TextUnit("yst00001:000000", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Approved)]);
        var vm = new ConferenceViewModel { Project = ProjectSummaryReader.Read(root) };
        await vm.RefreshAsync();
        Assert.False(vm.FixAndNextCommand.CanExecute(null));
        LineRequest? requested = null;
        vm.LineRequested += (_, request) => requested = request;

        await fixture.Session.Dispatch<int>(
            async () =>
            {
                var window = new Window { Width = 1200, Height = 800, Content = new ConferenceView { DataContext = vm } };
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                for (var i = 0; i < 10; i++)
                {
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }

                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Command == vm.FixAndNextCommand && b.IsEffectivelyVisible);
                window.Close();
                return 0;
            },
            CancellationToken.None);

        Assert.Null(requested);
        Assert.False(vm.IsFixing);
        Assert.Null(vm.NoticeMessage);
        Assert.Null(folder.LoadText("yst00001")[0].Translation);
        Assert.Single(vm.Rows);
    }

    [Fact]
    public async Task Tela_desenha_filtros_problemas_e_o_painel_com_o_que_e_isso()
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
                Assert.Contains("Conferir a tradução", texts);
                Assert.Contains("Travam o teste", texts);
                Assert.Contains("Espaço duplo", texts);
                Assert.Contains("Cena 1 · linha 1", texts);
                Assert.Contains("Um clique", texts);
                Assert.Contains("Corrigir 1 com um clique", texts);
                Assert.Contains("Conferir de novo", texts);
                Assert.Contains("Abrir no editor", texts);
                Assert.Contains("Corrigir e ir para o próximo", texts);
                Assert.Contains("Está certo assim", texts);
                Assert.Contains("Como vai ficar", texts);
                Assert.Contains("O que é isso", texts);

                // O detalhe mostra a gravidade com o SeverityTag, e a primeira linha vem selecionada.
                var tag = window.GetVisualDescendants().OfType<SeverityTag>().Single();
                Assert.Equal(SeverityLevel.Warning, tag.Level);
            },
            CancellationToken.None);
    }
}
