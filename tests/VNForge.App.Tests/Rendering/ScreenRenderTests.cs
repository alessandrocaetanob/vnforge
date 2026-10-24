using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.App.Tests.Demo;
using VNForge.App.ViewModels;
using VNForge.App.Views;

namespace VNForge.App.Tests.Rendering;

/// <summary>
/// Renderiza as telas do app com o projeto de demonstração para PNG, a 1440×1000, nos dois temas, para pôr ao lado do artboard nos PRs de tela.
/// Os arquivos vão para <c>VNFORGE_RENDER_DIR</c> (ou uma pasta temporária) com o nome <c>tela-escuro.png</c> e <c>tela-claro.png</c>.
/// </summary>
public sealed class ScreenRenderTests(HeadlessFixture fixture, DemoFolderFixture demoFolder) : IClassFixture<HeadlessFixture>, IClassFixture<DemoFolderFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    public static TheoryData<string> Themes => ["Dark", "Light"];

    private static readonly (NavKey Key, string Name)[] Screens =
    [
        (NavKey.Start, "inicio"),
        (NavKey.Dashboard, "painel"),
        (NavKey.Editor, "editor"),
        (NavKey.Conference, "conferir"),
        (NavKey.Build, "build"),
        (NavKey.Settings, "ajustes"),
    ];

    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_cada_tela_em_1440_por_1000(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;

                    foreach (var (key, name) in Screens)
                    {
                        vm.SelectedNav = vm.Nav.Single(n => n.Key == key);
                        await Pending(vm, key);
                        ScreenRenderer.Settle(window);

                        var path = RenderOutput.PathFor(ScreenRenderer.FileName(name, theme));
                        ScreenRenderer.Save(window, path);

                        using var png = new Bitmap(path);
                        Assert.Equal(ScreenRenderer.Width, png.PixelSize.Width);
                        Assert.Equal(ScreenRenderer.Height, png.PixelSize.Height);
                    }
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    /// <summary>O Editor com o painel de busca e substituição aberto e a prévia de uma troca, para o PR da #234.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task Renderiza_o_Editor_com_a_busca_aberta(string themeName)
    {
        var theme = themeName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var demo = demoFolder;
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(demo.Root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label) };
                var window = new MainWindow { Width = ScreenRenderer.Width, Height = ScreenRenderer.Height, RequestedThemeVariant = theme, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    await vm.Editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495");

                    vm.Editor.OpenReplaceCommand.Execute(null);
                    vm.Editor.Search.Pattern = "guarda-chuva";
                    vm.Editor.Search.Replacement = "sombrinha";
                    await vm.Editor.Search.PreviewCommand.ExecuteAsync(null);
                    ScreenRenderer.Settle(window);

                    ScreenRenderer.Save(window, RenderOutput.PathFor(ScreenRenderer.FileName("editor-busca", theme)));
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static async Task Pending(MainWindowViewModel vm, NavKey key)
    {
        switch (key)
        {
            case NavKey.Editor:
                // O artboard abre o capítulo 13 numa linha da IA, com o capítulo 14 em tradução.
                vm.Editor.RunningScriptKey = "yst00014";
                await vm.Editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495");
                break;
            case NavKey.Dashboard:
                await vm.Dashboard.ReloadAsync();
                break;
            case NavKey.Build:
                await vm.Build.Loading;
                break;
            case NavKey.Conference:
                await vm.ConferenceLoading;
                break;
        }
    }
}

/// <summary>A pasta do projeto de demonstração, gravada uma vez por classe de teste e apagada no fim.</summary>
public sealed class DemoFolderFixture : IDisposable
{
    private readonly TempFolder temp = new();

    public DemoFolderFixture()
    {
        Root = DemoProject.Create(temp.SubFolder("projeto"));
        var folder = ProjectFolder.Open(Root);

        // Os artboards mostram a caixa de 40 colunas e 3 linhas e a voz dos três personagens do trecho.
        File.WriteAllText(folder.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""");
        var tones = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["美咲"] = "fala informal e animada; chama o Kenta pelo nome, sem honorífico",
            ["健太"] = "seco e resmungão; frases curtas",
            ["先生"] = "formal; chama os alunos pelo sobrenome",
        };
        folder.SaveCharacters([.. folder.LoadCharacters().Select(c => tones.TryGetValue(c.Source, out var tone) ? c with { Tone = tone } : c)]);
    }

    public string Root { get; }

    public void Dispose() => temp.Dispose();
}
