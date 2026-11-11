using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using VNForge.AI.Bible;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Demo;
using VNForge.Core.Projects;

namespace VNForge.App.Tests.Screens;

/// <summary>
/// A janela principal aberta com um <see cref="MainWindowViewModel"/>, no tamanho do canvas do design (1440×1000), e o que ela precisa que seja descartado
/// junto (pasta de configuração, relógio). Quem abre fecha: <see cref="Dispose"/> fecha a janela. Chamar na thread da sessão headless.
/// Este arquivo é compilado também pela ferramenta de capturas (<c>tools/VNForge.Capturas</c>): não pode depender do xUnit.
/// </summary>
internal sealed class ScreenHost : IDisposable
{
    public const int Width = 1440;
    public const int Height = 1000;

    public static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 41, 0, TimeSpan.Zero);

    private readonly List<IDisposable> owned;

    private ScreenHost(MainWindow window, MainWindowViewModel vm, List<IDisposable> owned)
    {
        Window = window;
        Vm = vm;
        this.owned = owned;
    }

    public MainWindow Window { get; }

    public MainWindowViewModel Vm { get; }

    public static ScreenHost Show(MainWindowViewModel vm, ThemeVariant theme, params IDisposable[] owned)
    {
        var window = new MainWindow { Width = Width, Height = Height, RequestedThemeVariant = theme, DataContext = vm };
        window.Show();
        return new ScreenHost(window, vm, [.. owned]);
    }

    /// <summary>Abre a janela e o projeto de <paramref name="root"/> pelo Início, como a pessoa faz, e espera o Editor carregar os scripts.</summary>
    public static async Task<ScreenHost> OpenProjectAsync(string root, ThemeVariant theme, Func<StartViewModel, MainWindowViewModel>? create = null)
    {
        var config = new TempFolder();
        var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
        var vm = create?.Invoke(start) ?? new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label), BibleProviderFactory = (_, _) => new FakeBibleProvider() };
        var host = Show(vm, theme, config);
        try
        {
            await start.OpenFolderCommand.ExecuteAsync(null);
            await vm.ProjectOpening;
            await vm.Editor.Loading;
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>Vai para a página e espera a carga que ela faz em segundo plano, para o quadro sair com os dados e não com o carregando.</summary>
    public async Task GoToAsync(NavKey key)
    {
        Vm.SelectedNav = Vm.Nav.Single(n => n.Key == key);
        switch (key)
        {
            case NavKey.Editor:
                // O artboard abre o capítulo 13 numa linha da IA, com o capítulo 14 em tradução.
                Vm.Editor.RunningScriptKey = "yst00014";
                await Vm.Editor.GoToLineAsync(DemoProject.CurrentScript, "yst00013:0495");
                break;
            case NavKey.Dashboard:
                await Vm.Dashboard.ReloadAsync();
                break;
            case NavKey.Build:
                await Vm.Build.Loading;
                break;
            case NavKey.Conference:
                await Vm.ConferenceLoading;
                break;
            case NavKey.Settings:
                await Vm.Settings.EstimateLoading;
                break;
            case NavKey.Glossary:
                await Vm.Glossary.Loading;
                break;
            case NavKey.Images:
                await Vm.Images.Loading;
                break;
            case NavKey.Bible:
                await Vm.Bible.Loading;
                break;
            case NavKey.Characters:
                await Vm.Characters.Loading;
                break;
            case NavKey.Sheet:
                await Vm.Sheet.Loading;
                break;
        }

        Settle();
    }

    /// <summary>Passa a posse de um recurso para a janela: ele é descartado junto com ela.</summary>
    public void Own(IDisposable resource) => owned.Add(resource);

    public void Settle() => Settle(Window);

    public static void Settle(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    public void Dispose()
    {
        Window.Close();
        foreach (var item in owned)
        {
            item.Dispose();
        }
    }
}
