using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// PNG do Glossário (abas Termos e Personagens) e de cada passo do assistente de novo projeto, a 1440×1000 nos dois temas,
/// para pôr ao lado de <c>docs/design/telas/Glossario.dc.html</c> e <c>NovoProjeto.dc.html</c> no PR. Os arquivos seguem <c>VNFORGE_RENDER_DIR</c>.
/// </summary>
public sealed class GlossaryWizardRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static ThemeVariant Variant(string name) => name == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static void Check(string path)
    {
        using var png = new Bitmap(path);
        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_glossario_com_termos_e_com_personagens(string themeName)
    {
        var theme = Variant(themeName);
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Glossary);
                    await vm.Glossary.Loading;

                    ScreenRenderer.Settle(window);
                    var characters = RenderOutput.PathFor(ScreenRenderer.FileName("glossario-personagens", theme));
                    ScreenRenderer.Save(window, characters);
                    Check(characters);

                    vm.Glossary.SelectedTab = GlossaryTab.Terms;
                    ScreenRenderer.Settle(window);
                    var terms = RenderOutput.PathFor(ScreenRenderer.FileName("glossario", theme));
                    ScreenRenderer.Save(window, terms);
                    Check(terms);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_cada_passo_do_assistente_de_novo_projeto(string themeName)
    {
        var theme = Variant(themeName);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var temp = new TempFolder();
                var game = temp.SubFolder("jogo");
                File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
                var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);
                var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
                var vm = new MainWindowViewModel(start, newProjectWizard: () => wizard);
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    start.NewProjectCommand.Execute(null);
                    Assert.Same(wizard, vm.CurrentPage);

                    // Pasta ainda não escolhida, para o estado vazio do passo 1.
                    Render(window, "novo-projeto-1-vazio", theme);

                    await wizard.SetGameFolderAsync(game);
                    Render(window, "novo-projeto-1-pasta", theme);

                    wizard.SetProjectDirectory(Path.Combine(temp.Path, "minha-vn-ptbr"));
                    await wizard.NextCommand.ExecuteAsync(null);
                    wizard.ProjectName = "Minha VN";
                    Render(window, "novo-projeto-2-idiomas", theme);

                    await wizard.NextCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-3-acentuacao", theme);

                    await wizard.NextCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-4-ia", theme);

                    await wizard.NextCommand.ExecuteAsync(null);
                    Render(window, "novo-projeto-5-pronto", theme);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static void Render(MainWindow window, string name, ThemeVariant theme)
    {
        ScreenRenderer.Settle(window);
        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
        ScreenRenderer.Save(window, path);
        Check(path);
    }
}
