using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Screens;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>
/// Movimento do app (issue #249): cada animação tem o caminho "Windows pede menos animação", em que nada anima. Os testes ligam e desligam
/// <see cref="MotionPreferences"/> à mão; o resto da suíte roda com animações desligadas (ver <see cref="HeadlessEntry"/>).
/// </summary>
[Collection("Motion")]
public class MotionHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private Task OnUi(Action test) => fixture.Session.Dispatch(test, TestContext.Current.CancellationToken);

    private Task OnUi(Func<Task> test) => fixture.Session.Dispatch<int>(
        async () =>
        {
            await test();
            return 0;
        },
        TestContext.Current.CancellationToken);

    private static Window Show(Control content, bool follow = true)
    {
        var window = new Window { Width = 900, Height = 600, Content = content };
        if (follow)
        {
            Motion.Follow(window);
        }

        window.Show();
        ScreenHost.Settle(window);
        return window;
    }

    // Issue #283: o aviso de mudança é estático e chega a janelas de outras sessões. Se o tratador procurasse o Dispatcher.UIThread
    // na thread de quem chamou Force, criaria um despachante nela e as sessões headless seguintes falhavam ao subir (dono errado da thread).
    // Uma thread própria muda a preferência enquanto a sessão está presa esperando por ela: nenhum outro teste de interface roda nesse intervalo.
    [Fact]
    public Task Mudar_a_preferencia_de_outra_thread_nao_cria_despachante_nela() => OnUi(() =>
    {
        var window = Show(new Border());
        try
        {
            Avalonia.Threading.Dispatcher? despachante = null;
            ExceptionDispatchInfo? falha = null;
            var outra = new Thread(() =>
            {
                try
                {
                    using (MotionPreferences.Force(true))
                    {
                    }

                    despachante = Avalonia.Threading.Dispatcher.FromThread(Thread.CurrentThread);
                }
                catch (Exception ex)
                {
                    falha = ExceptionDispatchInfo.Capture(ex);
                }
            });
            outra.Start();
            outra.Join();
            falha?.Throw();

            Assert.Null(despachante);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Interrogacao_abre_o_mapa_de_atalhos_e_esc_fecha_mas_nao_dentro_de_um_campo() => OnUi(() =>
    {
        using var temp = new TempFolder();
        var start = new StartViewModel(new RecentProjectsFile(temp.Path), new FakeFolderPicker(null), TimeProvider.System);
        var vm = new MainWindowViewModel(start);
        var window = new MainWindow { Width = 1200, Height = 800, DataContext = vm };
        try
        {
            window.Show();
            ScreenHost.Settle(window);

            window.KeyPress(Key.OemQuestion, RawInputModifiers.Shift, PhysicalKey.Slash, "?");
            Assert.True(vm.IsShortcutMapOpen);
            Assert.True(window.GetVisualDescendants().OfType<ShortcutMapView>().Single().IsVisible);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(vm.IsShortcutMapOpen);

            var box = new TextBox();
            window.Content = new StackPanel { Children = { box } };
            ScreenHost.Settle(window);
            box.Focus();
            window.KeyPress(Key.OemQuestion, RawInputModifiers.Shift, PhysicalKey.Slash, "?");
            Assert.False(vm.IsShortcutMapOpen);
        }
        finally
        {
            window.Close();
        }
    });
}
