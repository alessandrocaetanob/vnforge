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
        ScreenHost host;
        StartViewModel start;
        MainWindowViewModel vm;
        try
        {
            start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
            vm = create?.Invoke(start) ?? new MainWindowViewModel(start) { Editor = new EditorViewModel(scriptLabel: DemoProject.Label), BibleProviderFactory = (_, _) => new FakeBibleProvider() };
            host = Show(vm, theme, config);
        }
        catch
        {
            config.Dispose();
            throw;
        }

        try
        {
            await Loaded(start.OpenFolderCommand.ExecuteAsync(null), "abrir o projeto pelo Início");
            await Loaded(vm.ProjectOpening, "abrir o projeto");
            await Loaded(vm.Editor.Loading, "carregar os scripts do Editor");
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
                RequireDemoLine(RunningScript, null);
                RequireDemoLine(DemoProject.CurrentScript, CurrentLine);
                Vm.Editor.RunningScriptKey = RunningScript;
                await Loaded(Vm.Editor.GoToLineAsync(DemoProject.CurrentScript, CurrentLine), "a página Editor");
                break;
            case NavKey.Dashboard:
                await Loaded(Vm.Dashboard.ReloadAsync(), "a página Painel");
                break;
            case NavKey.Build:
                await Loaded(Vm.Build.Loading, "a página Build");
                break;
            case NavKey.Conference:
                await Loaded(Vm.ConferenceLoading, "a página Conferência");
                break;
            case NavKey.Settings:
                await Loaded(Vm.Settings.EstimateLoading, "a página Ajustes");
                break;
            case NavKey.Glossary:
                await Loaded(Vm.Glossary.Loading, "a página Glossário");
                break;
            case NavKey.Images:
                await Loaded(Vm.Images.Loading, "a página Imagens");
                break;
            case NavKey.Bible:
                await Loaded(Vm.Bible.Loading, "a página Bíblia");
                break;
            case NavKey.Characters:
                await Loaded(Vm.Characters.Loading, "a página Personagens");
                break;
            case NavKey.Sheet:
                await Loaded(Vm.Sheet.Loading, "a página Ficha do jogo");
                break;
        }

        Settle();
    }

    /// <summary>O prazo de cada carga em segundo plano. Sobra para o computador ocupado e ainda assim acaba antes de o teste inteiro travar sem dizer onde.</summary>
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(60);

    private const string RunningScript = "yst00014";

    private const string CurrentLine = "yst00013:0495";

    /// <summary>
    /// Espera a carga com prazo. Fica aqui e não em <c>Wait</c> porque este arquivo também é compilado pela ferramenta de capturas, que não tem o xUnit.
    /// </summary>
    private static async Task Loaded(Task loading, string what)
    {
        try
        {
            await loading.WaitAsync(LoadTimeout);
        }
        catch (TimeoutException) when (!loading.IsCompleted)
        {
            throw new TimeoutException($"{what}: a carga não terminou em {LoadTimeout.TotalSeconds:0} s.");
        }
    }

    /// <summary>As chaves fixas do Editor existem no projeto de demonstração; se o gerador mudar, o erro diz qual sumiu.</summary>
    private static void RequireDemoLine(string scriptKey, string? lineId)
    {
        if (!DemoProject.Texts.TryGetValue(scriptKey, out var units))
        {
            throw new InvalidOperationException($"O script '{scriptKey}', que a página Editor abre, não existe no projeto de demonstração.");
        }

        if (lineId is not null && !units.Any(u => u.Id == lineId))
        {
            throw new InvalidOperationException($"A linha '{lineId}', que a página Editor abre, não existe no script '{scriptKey}' do projeto de demonstração.");
        }
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
