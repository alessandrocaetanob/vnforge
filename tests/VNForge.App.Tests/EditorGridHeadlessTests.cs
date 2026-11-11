using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

/// <summary>Ponto de entrada da sessão headless: sobe o <see cref="App"/> de verdade (tema, estilos e recursos), sem janela do sistema.</summary>
public static class HeadlessEntry
{
    // O Skia pede ao sistema uma fonte padrão; um contêiner Linux sem fontes não tem nenhuma e o FontManager lança.
    // Apontar para a fonte embutida deixa o resultado igual em qualquer máquina.
    public static AppBuilder BuildAvaloniaApp()
    {
        // O relógio das animações não anda no headless: sem isto uma página recém-trocada ficaria parada no primeiro quadro (opacidade 0).
        // Os testes de movimento ligam de novo com MotionPreferences.Force(true).
        _ = MotionPreferences.Force(false);
        return Configure();
    }

    private static AppBuilder Configure() =>
        AppBuilder.Configure<App>()
            .With(new FontManagerOptions { DefaultFamilyName = "avares://VNForge.App/Assets/Fonts#Noto Sans" })
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Uma sessão para o processo todo: o Avalonia só aceita uma instância de aplicativo por thread de UI, então as classes de teste a dividem.</summary>
public sealed class HeadlessFixture
{
    private static readonly Lazy<HeadlessUnitTestSession> Shared = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry)));

    // Sem Dispose: Session.Dispose() trava quando chamado da thread do xUnit; a thread da sessão não segura o processo.
    public ResilientHeadlessSession Session { get; } = new(Shared.Value);
}

/// <summary>
/// Guarda o critério "a grade rola sem travar com 50 mil linhas": a tela real, com a virtualização do <c>ListBox</c>,
/// carregada com linhas geradas em código. Os limites de tempo são folgados para não oscilar no CI; o que importa é a ordem de grandeza.
/// </summary>
public class EditorGridHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private const int LineCount = 50_000;
    private const int MaxRealized = 100;
    // A VM Windows da nuvem levou 2,3 s para abrir; sem virtualização seriam dezenas de segundos.
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static async Task<EditorViewModel> Carregado(int count)
    {
        var vm = new EditorViewModel(_ => Large(count));
        vm.Project = Summary();
        await vm.Loading;
        return vm;
    }

    private static Window Abrir(EditorViewModel vm)
    {
        var window = new Window { Width = 1400, Height = 900, Content = new EditorView { DataContext = vm } };
        window.Show();
        Assentar(window);
        return window;
    }

    private static void Assentar(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static ListBox Grade(Window window) => ((EditorView)window.Content!).FindControl<ListBox>("LinesList")!;

    private static int[] Realizadas(ListBox grade) =>
        [.. grade.GetRealizedContainers().Select(c => ((LineViewModel)c.DataContext!).Number).Order()];

    [Fact]
    public async Task Alt_seta_para_baixo_e_para_cima_movem_a_selecao_da_grade()
    {
        var vm = await Carregado(1_000);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                try
                {
                    Assert.True(Grade(window).ContainerFromIndex(0)!.Focus());
                    Assentar(window);

                    var passos = new List<int?>();
                    foreach (var (key, physical) in new[] { (Key.Down, PhysicalKey.ArrowDown), (Key.Down, PhysicalKey.ArrowDown), (Key.Up, PhysicalKey.ArrowUp) })
                    {
                        window.KeyPress(key, RawInputModifiers.Alt, physical, null);
                        window.KeyRelease(key, RawInputModifiers.Alt, physical, null);
                        Assentar(window);
                        passos.Add(vm.SelectedLine?.Number);
                    }

                    Assert.Equal([2, 3, 2], passos);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static string? Automation(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);

    private static Color? Cor(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var found) && found is ISolidColorBrush brush ? brush.Color : null;
}
