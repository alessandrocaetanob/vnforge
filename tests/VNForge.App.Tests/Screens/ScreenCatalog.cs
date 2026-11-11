using Avalonia.Styling;
using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Demo;
using VNForge.Core.Library;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Screens;

/// <summary>
/// Uma tela do app: o nome do arquivo da captura e o jeito de abri-la. <see cref="Open"/> recebe a pasta do projeto de demonstração e o tema e devolve a
/// janela pronta (assentada, com os dados carregados); quem chama descarta o <see cref="ScreenHost"/>.
/// </summary>
internal sealed record ScreenDef(string Name, Func<string, ThemeVariant, Task<ScreenHost>> Open);

/// <summary>
/// As telas do app, em um lugar só: o <c>ScreenSmokeTests</c> abre cada uma e confere que não lança nem corta texto, e a ferramenta
/// <c>tools/VNForge.Capturas</c> grava o PNG de cada uma, nos dois temas, para o designer. Arquivo sem xUnit.
/// </summary>
internal static class ScreenCatalog
{
    /// <summary>As páginas do trilho e do cabeçalho do jogo que abrem sobre o projeto de demonstração. Uma janela só passa por todas, como a pessoa faz.</summary>
    public static IReadOnlyList<(string Name, NavKey Key)> Pages { get; } =
    [
        ("painel", NavKey.Dashboard),
        ("editor", NavKey.Editor),
        ("conferir", NavKey.Conference),
        ("build", NavKey.Build),
        ("ajustes", NavKey.Settings),
        ("glossario", NavKey.Glossary),
        ("imagens", NavKey.Images),
        ("lancar", NavKey.Release),
        ("biblia", NavKey.Bible),
        ("ficha", NavKey.Sheet),
        ("jogar", NavKey.Play),
    ];

    /// <summary>As telas que não são só uma página do projeto: a Biblioteca, os Personagens, um modo ou painel aberto sobre o Editor, um diálogo ou o assistente.</summary>
    public static IReadOnlyList<ScreenDef> Standalone { get; } =
    [
        new("biblioteca", OpenLibrary),
        new("personagens", OpenCharacters),
        new("editor-busca", OpenEditorSearch),
        new("revisao", OpenReview),
        new("leitura", OpenReading),
        new("paleta", OpenPalette),
        new("atalhos", OpenShortcutMap),
        new("traduzir", OpenTranslate),
        new("novo-projeto", OpenWizard),
    ];

    /// <summary>
    /// O projeto de demonstração gravado numa pasta de <paramref name="temp"/>, com a caixa de 40 colunas e 3 linhas e a voz dos três personagens do trecho,
    /// como os artboards mostram.
    /// </summary>
    public static string CreateDemoFolder(TempFolder temp)
    {
        var root = DemoProject.Create(temp.SubFolder("projeto"));
        var folder = ProjectFolder.Open(root);
        File.WriteAllText(folder.FormattingPath, """{ "wrap": { "enabled": true, "maxWidth": 40, "maxLines": 3, "charWidth": "uniform" } }""");
        var tones = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["美咲"] = "fala informal e animada; chama o Kenta pelo nome, sem honorífico",
            ["健太"] = "seco e resmungão; frases curtas",
            ["先生"] = "formal; chama os alunos pelo sobrenome",
        };
        folder.SaveCharacters([.. folder.LoadCharacters().Select(c => tones.TryGetValue(c.Source, out var tone) ? c with { Tone = tone } : c)]);
        return root;
    }

    /// <summary>Roda o passo de preparação e, se ele falhar, fecha a janela antes de a exceção subir.</summary>
    private static async Task Prepare(ScreenHost host, Func<Task> step)
    {
        try
        {
            await step();
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    private static async Task<ScreenHost> OpenLibrary(string root, ThemeVariant theme)
    {
        var config = new TempFolder();
        var previous = VndbImages.Source;
        VndbImages.Source = new LibrarySamples.Images();
        var file = LibrarySamples.Save(config.Path, LibrarySamples.Library());
        var library = new LibraryViewModel(file, new FakeFolderPicker(null), action => action(), clock: new FixedClock(LibrarySamples.Now), readTranslation: LibrarySamples.Translation, sheets: new LibrarySamples.Sheets());
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(null), new FixedClock(LibrarySamples.Now));
        var host = ScreenHost.Show(new MainWindowViewModel(start, library: library), theme, config, new RestoreImages(previous));
        await Prepare(host, async () =>
        {
            await library.LoadAsync();
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenCharacters(string root, ThemeVariant theme)
    {
        var config = new TempFolder();
        await CharactersSamples.SeedAsync(config.Path, matched: true, showSpoilers: true);
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(config.Path), new FixedClock(CharactersSamples.Now));
        var vm = new MainWindowViewModel(start);
        vm.Characters.ConfigDirectory = config.Path;
        var host = ScreenHost.Show(vm, theme, config);
        await Prepare(host, () => host.GoToAsync(NavKey.Characters));
        return host;
    }

    private static async Task<ScreenHost> OpenEditorSearch(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Editor.OpenReplaceCommand.Execute(null);
            host.Vm.Editor.Search.Pattern = "guarda-chuva";
            host.Vm.Editor.Search.Replacement = "sombrinha";
            await host.Vm.Editor.Search.PreviewCommand.ExecuteAsync(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenReview(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Editor.StartReviewCommand.Execute(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenReading(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Editor.StartReadingCommand.Execute(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenPalette(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Palette.Open();
            host.Vm.Palette.Query = "ed";
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenShortcutMap(string root, ThemeVariant theme)
    {
        var host = await ScreenHost.OpenProjectAsync(root, theme);
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.ToggleShortcutMapCommand.Execute(null);
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenTranslate(string root, ThemeVariant theme)
    {
        var vault = new MemoryApiKeyStore();
        vault.Save("sk-ant-chave-da-demonstracao");
        var config = new TempFolder();
        var host = await ScreenHost.OpenProjectAsync(
            root,
            theme,
            start => new MainWindowViewModel(
                start,
                new ResolvedApiKey("sk-ant-chave-da-demonstracao", ApiKeySource.Vault),
                settings: new SettingsViewModel(vault, new AppSettingsFile(config.SubFolder("ajustes")), _ => null, _ => throw new InvalidOperationException("a captura não chama a API")))
            {
                Editor = new EditorViewModel(scriptLabel: DemoProject.Label),
            });
        await Prepare(host, async () =>
        {
            await host.GoToAsync(NavKey.Editor);
            host.Vm.Settings.ExchangeRateText = "5,40";
            host.Vm.Settings.SpendLimitText = "300";
            host.Vm.Settings.WarnAboveText = "250";
            host.Vm.Editor.TranslatePendingCommand.Execute(null);
            await host.Vm.TranslateOpening;
            host.Settle();
        });
        return host;
    }

    private static async Task<ScreenHost> OpenWizard(string root, ThemeVariant theme)
    {
        var config = new TempFolder();
        var game = config.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Game.exe"), "MZ");
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()], appSettings: new AppSettingsFile(config.SubFolder("ajustes")), clock: new FixedClock(ScreenHost.Now));
        var start = new StartViewModel(new RecentProjectsFile(config.SubFolder("recentes")), new FakeFolderPicker(null), new FixedClock(ScreenHost.Now));
        var host = ScreenHost.Show(new MainWindowViewModel(start, newProjectWizard: () => wizard), theme, config);
        await Prepare(host, async () =>
        {
            start.NewProjectCommand.Execute(null);
            await wizard.SetGameFolderAsync(game);
            host.Settle();
        });
        return host;
    }

    /// <summary>Devolve a fonte de imagens do VNDB quando a janela da Biblioteca fecha.</summary>
    private sealed class RestoreImages(IVndbImageSource? previous) : IDisposable
    {
        public void Dispose() => VndbImages.Source = previous;
    }
}
