using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VNForge.App.Services;
using VNForge.App.Tests.Screens;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>As teclas do modo revisão (Enter, E, M, seta, Esc) pelos <c>KeyBinding</c> da tela, na janela do app e sem tocar no mouse.</summary>
public sealed class ReviewModeKeyTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enter_aprova_e_grava_a_aprovacao_com_hora()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Pressionar(root, Key.Enter, PhysicalKey.Enter, (vm, review) =>
        {
            Assert.Equal(1, review.Position);
            Assert.True(review.Blocks[0].IsApproved);
            Assert.Equal(TranslationStatus.Approved, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
            var record = Assert.Single(SessionsFile.Load(ProjectFolder.Open(root)).Approvals);
            Assert.Equal("yst00001:000000", record.UnitId);
            Assert.Equal(Now, record.At);
        });
    }

    [Fact]
    public async Task E_sai_do_modo_revisao_com_a_linha_aberta_no_editor()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Pressionar(root, Key.E, PhysicalKey.E, (vm, _) =>
        {
            Assert.False(vm.Editor.IsReviewing);
            Assert.Equal("yst00001:000000", vm.Editor.SelectedLine!.Id);
        });
    }

    [Fact]
    public async Task Esc_volta_ao_editor_sem_mexer_nas_linhas()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Pressionar(root, Key.Escape, PhysicalKey.Escape, (vm, _) =>
        {
            Assert.False(vm.Editor.IsReviewing);
            Assert.Equal(TranslationStatus.Machine, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
        });
    }

    [Fact]
    public async Task As_teclas_continuam_valendo_depois_da_primeira_porque_o_foco_fica_na_tela()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Pressionar(root, [(Key.Right, PhysicalKey.ArrowRight, true), (Key.Enter, PhysicalKey.Enter, true)], (vm, review) =>
        {
            Assert.Equal(2, review.Position);
            Assert.True(review.Blocks[1].IsApproved);
        });
    }

    [Fact]
    public async Task Com_a_janela_Traduzir_com_IA_aberta_as_teclas_da_revisao_nao_agem()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Rodar(
            root,
            (window, vm) =>
            {
                vm.Translate.IsOpen = true;
                ScreenHost.Settle(window);

                Teclar(window, [(Key.Enter, PhysicalKey.Enter, true), (Key.Right, PhysicalKey.ArrowRight, true)]);
            },
            (vm, review) =>
            {
                Assert.True(vm.Editor.IsReviewing);
                Assert.Equal(0, review.Position);
                Assert.Equal(0, review.ApprovedCount);
            });
    }

    [Fact]
    public async Task Tecla_presa_de_uma_revisao_anterior_nao_trava_a_revisao_nova()
    {
        using var temp = new TempFolder();
        var root = CreateProject(temp.SubFolder("projeto"));

        await Rodar(
            root,
            (window, vm) =>
            {
                Teclar(window, [(Key.Enter, PhysicalKey.Enter, false)]);
                vm.Editor.StartReview();
                ScreenHost.Settle(window);

                Teclar(window, [(Key.Enter, PhysicalKey.Enter, false)]);
            },
            (vm, _) =>
            {
                Assert.Equal(1, vm.Editor.Review!.ApprovedCount);
                Assert.Equal(1, vm.Editor.Review.Position);
            });
    }

    private static int Inscritos(object alvo, string evento, Type dono, Type ouvinte) =>
        ((MulticastDelegate?)dono.GetField(evento, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(alvo))?
        .GetInvocationList().Count(d => d.Target?.GetType() == ouvinte) ?? 0;

    private static int InscritosPropertyChanged(object alvo, Type ouvinte) =>
        typeof(Avalonia.AvaloniaObject).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs>))
            .Select(f => (MulticastDelegate?)f.GetValue(alvo))
            .Sum(d => d?.GetInvocationList().Count(h => h.Target?.GetType() == ouvinte) ?? 0);

    private Task Pressionar(string root, Key key, PhysicalKey physical, Action<MainWindowViewModel, ReviewModeViewModel> verificar) =>
        Pressionar(root, [(key, physical, true)], verificar);

    private Task Pressionar(string root, (Key Key, PhysicalKey Physical, bool Release)[] teclas, Action<MainWindowViewModel, ReviewModeViewModel> verificar) =>
        Rodar(root, (window, _) => Teclar(window, teclas), verificar);

    private static void Teclar(MainWindow window, (Key Key, PhysicalKey Physical, bool Release)[] teclas)
    {
        foreach (var (key, physical, release) in teclas)
        {
            window.KeyPress(key, RawInputModifiers.None, physical, null);
            if (release)
            {
                window.KeyRelease(key, RawInputModifiers.None, physical, null);
            }

            ScreenHost.Settle(window);
        }
    }

    private async Task Rodar(string root, Action<MainWindow, MainWindowViewModel> acao, Action<MainWindowViewModel, ReviewModeViewModel> verificar)
    {
        var log = new ProjectApprovalLog(root);
        await fixture.Session.Dispatch<int>(
            async () =>
            {
                using var config = new TempFolder();
                var start = new StartViewModel(new RecentProjectsFile(config.Path), new FakeFolderPicker(root), new FixedClock(Now));
                var vm = new MainWindowViewModel(start) { Editor = new EditorViewModel(clock: new FixedClock(Now), postToUi: action => action()) { ApprovalLogFactory = _ => log } };
                var window = new MainWindow { Width = ScreenHost.Width, Height = ScreenHost.Height, DataContext = vm };
                try
                {
                    window.Show();
                    await start.OpenFolderCommand.ExecuteAsync(null);
                    await vm.ProjectOpening;
                    await vm.Editor.Loading;
                    vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Editor);
                    ScreenHost.Settle(window);

                    vm.Editor.StartReviewCommand.Execute(null);
                    ScreenHost.Settle(window);
                    var review = vm.Editor.Review!;
                    var view = window.GetVisualDescendants().OfType<ReviewModeView>().Single(v => v.IsEffectivelyVisible);
                    Assert.True(view.FindControl<Grid>("Root")!.IsFocused, "a tela do modo revisão recebe o foco ao abrir");

                    acao(window, vm);

                    log.Saving.GetAwaiter().GetResult();
                    verificar(vm, review);
                }
                finally
                {
                    window.Close();
                }

                return 0;
            },
            TestContext.Current.CancellationToken);
    }

    private static string CreateProject(string parent)
    {
        var root = SampleProjects.Create(parent, "Teste", TranslationStatus.Machine, TranslationStatus.Machine, TranslationStatus.Machine);
        return root;
    }
}
