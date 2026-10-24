using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

/// <summary>Ponto de entrada da sessão headless: sobe o <see cref="App"/> de verdade (tema, estilos e recursos), sem janela do sistema.</summary>
public static class HeadlessEntry
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}

/// <summary>Uma sessão para o processo todo: o Avalonia só aceita uma instância de aplicativo por thread de UI, então as classes de teste a dividem.</summary>
public sealed class HeadlessFixture
{
    // Sem Dispose: Session.Dispose() trava quando chamado da thread do xUnit; a thread da sessão não segura o processo.
    private static readonly Lazy<HeadlessUnitTestSession> Shared = new(() => HeadlessUnitTestSession.StartNew(typeof(HeadlessEntry)));

    public HeadlessUnitTestSession Session { get; } = Shared.Value;
}

/// <summary>
/// Guarda o critério "a grade rola sem travar com 50 mil linhas": a tela real, com a virtualização do <c>ListBox</c>,
/// carregada com linhas geradas em código. Os limites de tempo são folgados para não oscilar no CI; o que importa é a ordem de grandeza.
/// </summary>
[Collection(HeadlessCollection.Name)]
public class EditorGridHeadlessTests(HeadlessFixture fixture)
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
    public async Task Grade_com_50_mil_linhas_so_realiza_as_linhas_da_janela_e_rola_sem_travar()
    {
        var vm = await Carregado(LineCount);

        await fixture.Session.Dispatch(
            () =>
            {
                var abrir = Stopwatch.StartNew();
                var window = Abrir(vm);
                abrir.Stop();
                var grade = Grade(window);

                Assert.True(abrir.Elapsed < Limit, $"Abrir a tela com {LineCount} linhas levou {abrir.Elapsed.TotalMilliseconds:N0} ms.");
                Assert.InRange(grade.GetRealizedContainers().Count(), 1, MaxRealized);
                Assert.Equal(LineCount, grade.ItemCount);
                Assert.Equal(1, Realizadas(grade)[0]);

                var fim = Stopwatch.StartNew();
                grade.ScrollIntoView(LineCount - 1);
                Assentar(window);
                fim.Stop();
                Assert.True(fim.Elapsed < Limit, $"Rolar até o fim levou {fim.Elapsed.TotalMilliseconds:N0} ms.");
                Assert.InRange(grade.GetRealizedContainers().Count(), 1, MaxRealized);
                Assert.Contains(LineCount, Realizadas(grade));

                var meio = Stopwatch.StartNew();
                grade.ScrollIntoView(LineCount / 2);
                Assentar(window);
                meio.Stop();
                Assert.True(meio.Elapsed < Limit, $"Rolar até o meio levou {meio.Elapsed.TotalMilliseconds:N0} ms.");
                Assert.InRange(grade.GetRealizedContainers().Count(), 1, MaxRealized);
                Assert.Contains(LineCount / 2 + 1, Realizadas(grade));

            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Trocar_o_filtro_na_tela_com_50_mil_linhas_reconstroi_a_grade_depressa()
    {
        var vm = await Carregado(LineCount);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var grade = Grade(window);

                var relogio = Stopwatch.StartNew();
                vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Filter == LineStatusFilter.Pending));
                Assentar(window);
                vm.SelectFilterCommand.Execute(vm.Filters.Single(f => f.Filter == LineStatusFilter.All));
                Assentar(window);
                relogio.Stop();

                Assert.True(relogio.Elapsed < Limit, $"Dois filtros levaram {relogio.Elapsed.TotalMilliseconds:N0} ms.");
                Assert.Equal(LineCount, grade.ItemCount);
                Assert.InRange(grade.GetRealizedContainers().Count(), 1, MaxRealized);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Selecao_acompanha_a_lista_e_a_linha_selecionada_entra_na_janela()
    {
        var vm = await Carregado(LineCount);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var grade = Grade(window);
                Assert.Equal(1, vm.SelectedLine?.Number);
                Assert.Same(vm.SelectedLine, grade.SelectedItem);

                vm.SelectNextCommand.Execute(null);
                Assentar(window);
                Assert.Equal(2, vm.SelectedLine?.Number);
                Assert.Same(vm.SelectedLine, grade.SelectedItem);

                vm.SelectedLine = vm.Lines[^1];
                Assentar(window);
                Assert.Contains(LineCount, Realizadas(grade));
                Assert.InRange(grade.GetRealizedContainers().Count(), 1, MaxRealized);
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Alt_seta_para_baixo_e_para_cima_movem_a_selecao_da_grade()
    {
        var vm = await Carregado(1_000);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
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
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Selo_de_status_mostra_o_rotulo_e_as_cores_do_status()
    {
        var vm = await Carregado(200);

        await fixture.Session.Dispatch(
            () =>
            {
                var window = Abrir(vm);
                var grade = Grade(window);

                var selos = grade.GetRealizedContainers()
                    .SelectMany(c => c.GetVisualDescendants().OfType<StatusBadge>())
                    .GroupBy(b => b.Status)
                    .ToDictionary(g => g.Key, g => g.First());

                var ia = selos[TranslationStatus.Machine];
                Assert.Equal("IA", ia.Label);
                Assert.Equal("IA", Automation(ia));
                Assert.Equal(Cor(ia, "Vnf.StatusAiBg"), (ia.Background as ISolidColorBrush)?.Color);
                Assert.Equal(Cor(ia, "Vnf.StatusAiFg"), (ia.Foreground as ISolidColorBrush)?.Color);
                Assert.Equal(20, ia.Height);

                var pendente = selos[TranslationStatus.Untranslated];
                Assert.Equal("Pendente", pendente.Label);
                Assert.Equal(Cor(pendente, "Vnf.StatusPendingBg"), (pendente.Background as ISolidColorBrush)?.Color);
                Assert.Equal(Cor(selos[TranslationStatus.Approved], "Vnf.StatusApprovedFg"), (selos[TranslationStatus.Approved].Foreground as ISolidColorBrush)?.Color);
            },
            CancellationToken.None);
    }

    private static string? Automation(Control control) => Avalonia.Automation.AutomationProperties.GetName(control);

    private static Color? Cor(Control control, string key) =>
        control.TryFindResource(key, control.ActualThemeVariant, out var found) && found is ISolidColorBrush brush ? brush.Color : null;
}
