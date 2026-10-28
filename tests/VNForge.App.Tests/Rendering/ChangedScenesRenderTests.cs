using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Qa;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// A lista "Mudou desde o último teste" montada de verdade na página Testar e no Painel, nos dois temas, com o projeto de demonstração: três cenas editadas depois
/// do retrato do último teste (<c>mudou-desde-o-teste-testar</c> e <c>mudou-desde-o-teste-painel</c>) e o caso sem retrato, em que a lista vem vazia com o motivo.
/// Os PNGs saem em <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class ChangedScenesRenderTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset TestAt = new(2026, 10, 22, 23, 16, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task A_pagina_Testar_lista_as_cenas_que_mudaram_com_o_Abrir_de_cada_uma(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = DemoBuild.Create(temp.SubFolder("demo"));
        var (_, keys) = Edit(root, withSnapshot: true);
        var vm = new BuildViewModel(new BackgroundTaskService(), [new DemoBuildEngine()], new IdleStarter(), new QaEngine([])) { Project = DemoProject.Summary(root) };

        await fixture.Session.Dispatch(
            async () =>
            {
                await vm.ReloadAsync();
                var path = RenderOutput.PathFor(ScreenRenderer.FileName("mudou-desde-o-teste-testar", theme));
                ScreenRenderer.Render(new BuildView { DataContext = vm }, theme, path, inspect: window => AssertList(window, vm.SinceTest, keys.Count));
                Assert.True(new FileInfo(path).Length > 10_000);
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task O_Painel_lista_as_cenas_que_mudaram_ao_lado_do_estado_do_jogo(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        using var temp = new TempFolder();
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        var (_, keys) = Edit(root, withSnapshot: true);
        var vm = new DashboardViewModel { Project = DemoProject.Summary(root) };

        await fixture.Session.Dispatch(
            async () =>
            {
                await vm.ReloadAsync();
                var path = RenderOutput.PathFor(ScreenRenderer.FileName("mudou-desde-o-teste-painel", theme));
                ScreenRenderer.Render(new DashboardView { DataContext = vm }, theme, path, inspect: window => AssertList(window, vm.SinceTest, keys.Count));
                Assert.True(new FileInfo(path).Length > 10_000);
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Sem_retrato_a_lista_vem_vazia_com_o_motivo_e_sem_botao_Abrir()
    {
        using var temp = new TempFolder();
        var root = DemoBuild.Create(temp.SubFolder("demo"));
        Edit(root, withSnapshot: false);
        var vm = new BuildViewModel(new BackgroundTaskService(), [new DemoBuildEngine()], new IdleStarter(), new QaEngine([])) { Project = DemoProject.Summary(root) };

        await fixture.Session.Dispatch(
            async () =>
            {
                await vm.ReloadAsync();
                var path = RenderOutput.PathFor(ScreenRenderer.FileName("mudou-desde-o-teste-sem-retrato", ThemeVariant.Dark));
                ScreenRenderer.Render(new BuildView { DataContext = vm }, ThemeVariant.Dark, path, inspect: window =>
                {
                    var panel = window.GetVisualDescendants().OfType<ChangedScenesPanel>().Single();
                    var texts = panel.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
                    Assert.Contains(vm.SinceTest.Message, texts);
                    Assert.Contains("versão anterior", vm.SinceTest.Message);
                    Assert.DoesNotContain(panel.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible);
                });
                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void AssertList(Window window, ChangedScenesViewModel list, int scenes)
    {
        var panel = window.GetVisualDescendants().OfType<ChangedScenesPanel>().Single();
        Assert.True(panel.IsEffectivelyVisible);
        var texts = panel.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains(list.Message, texts);
        Assert.Equal(scenes, list.Rows.Count);
        Assert.All(list.Rows, row =>
        {
            Assert.Contains(row.Name, texts);
            Assert.Contains(row.LinesText, texts);
        });
        var open = panel.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && Equals(b.Content, "Abrir")).ToList();
        Assert.Equal(scenes, open.Count);
        Assert.All(open, b => Assert.True(b.IsEffectivelyEnabled));
        var bounds = new Rect(window.Bounds.Size);
        Assert.All(open, b => Assert.True(bounds.Contains(b.TranslatePoint(new Point(b.Bounds.Width - 1, b.Bounds.Height - 1), window)!.Value), "O Abrir sai da janela."));
    }

    /// <summary>Guarda o último teste, com ou sem retrato, e depois edita a primeira fala traduzida de três cenas.</summary>
    private static (TranslationSnapshot Snapshot, IReadOnlyList<string> Keys) Edit(string root, bool withSnapshot)
    {
        var project = ProjectFolder.Open(root);
        var edited = ChangedSinceTestTests.EditThreeScenes(project);
        GameStateFile.Save(project, new GameStateFile(
            GameStateFile.CurrentVersion,
            null,
            new GameLastTest(TestAt, GameTestKind.Translation, [], withSnapshot ? edited.Snapshot : null)));
        return edited;
    }

    private sealed class IdleStarter : IGameProcessStarter
    {
        public void Start(string executablePath, string workingDirectory) => throw new IOException("o teste não abre o jogo");
    }
}
