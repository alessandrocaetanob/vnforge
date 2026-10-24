using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.Services;
using VNForge.App.Tests.Rendering;
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
        ScreenRenderer.Settle(window);
        return window;
    }

    [Fact]
    public void Preferencia_forcada_vale_ate_ser_descartada()
    {
        using (MotionPreferences.Force(true))
        {
            Assert.True(MotionPreferences.AnimationsEnabled);
            using (MotionPreferences.Force(false))
            {
                Assert.False(MotionPreferences.AnimationsEnabled);
            }

            Assert.True(MotionPreferences.AnimationsEnabled);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Janela_segue_a_preferencia_com_a_classe_motion(bool enabled) => OnUi(() =>
    {
        using var _ = MotionPreferences.Force(enabled);
        var window = Show(new Border());
        try
        {
            Assert.Equal(enabled, window.Classes.Contains(Motion.MotionClass));

            using var __ = MotionPreferences.Force(!enabled);
            ScreenRenderer.Settle(window);
            Assert.Equal(!enabled, window.Classes.Contains(Motion.MotionClass));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void Transicao_de_pagina_dura_150_ms_com_8_px_de_deslocamento()
    {
        var animation = FadeSlideTransition.CreateEnter();

        Assert.Equal(TimeSpan.FromMilliseconds(150), animation.Duration);
        Assert.Equal(150, FadeSlideTransition.Duration.TotalMilliseconds);
        Assert.Equal(8, FadeSlideTransition.Offset);
        var first = animation.Children[0].Setters.OfType<Avalonia.Styling.Setter>().ToDictionary(s => s.Property!);
        Assert.Equal(0d, first[Avalonia.Visual.OpacityProperty].Value);
        Assert.Equal(8d, first[TranslateTransform_Y].Value);
    }

    private static Avalonia.AvaloniaProperty TranslateTransform_Y => Avalonia.Media.TranslateTransform.YProperty;

    [Fact]
    public Task Sem_animacoes_a_troca_de_pagina_e_imediata() => OnUi(async () =>
    {
        using var _ = MotionPreferences.Force(false);
        var from = new Border();
        var to = new Border { IsVisible = false, Opacity = 0.3 };

        var start = new FadeSlideTransition().Start(from, to, forward: true, CancellationToken.None);

        Assert.True(start.IsCompleted);
        await start;
        Assert.True(to.IsVisible);
        Assert.False(from.IsVisible);
        Assert.Null(to.RenderTransform);
    });

    [Fact]
    public Task Barras_de_progresso_deslizam_so_com_animacoes() => OnUi(() =>
    {
        foreach (var enabled in new[] { true, false })
        {
            using var _ = MotionPreferences.Force(enabled);
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 40, Width = 200 };
            var window = Show(bar);
            try
            {
                var indicator = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
                Assert.Equal(enabled, indicator.Transitions is { Count: > 0 });
            }
            finally
            {
                window.Close();
            }
        }
    });

    [Fact]
    public Task Barra_de_distribuicao_desliza_ao_mudar_e_respeita_a_preferencia() => OnUi(() =>
    {
        var before = new StatusDistribution(10, 0, 0, 0, 90);
        var after = new StatusDistribution(60, 0, 0, 0, 40);
        foreach (var enabled in new[] { true, false })
        {
            using var _ = MotionPreferences.Force(enabled);
            var bar = new DistributionBar { Height = 8, Value = before };
            var window = Show(bar);
            try
            {
                Assert.False(bar.IsSliding);

                bar.Value = after;

                Assert.Equal(enabled, bar.IsSliding);
            }
            finally
            {
                window.Close();
            }
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Linha_aprovada_pulsa_so_com_animacoes(bool enabled) => OnUi(() =>
    {
        using var _ = MotionPreferences.Force(enabled);
        var line = new LineViewModel(new LineEntry("s1", 1, EditorFixtures.Unit("s1", 1, "A", TranslationStatus.Translated), []));
        var row = new Grid { DataContext = line };
        Motion.SetPulseOnApproval(row, line.Status);
        var window = Show(row);
        try
        {
            Motion.SetPulseOnApproval(row, TranslationStatus.Approved);

            Assert.Equal(enabled, row.Classes.Contains(Motion.PulseClass));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Contenedor_reaproveitado_para_outra_linha_aprovada_nao_pulsa() => OnUi(() =>
    {
        using var _ = MotionPreferences.Force(true);
        var row = new Grid { DataContext = new object() };
        Motion.SetPulseOnApproval(row, TranslationStatus.Translated);

        row.DataContext = new object();
        Motion.SetPulseOnApproval(row, TranslationStatus.Approved);

        Assert.DoesNotContain(Motion.PulseClass, row.Classes);
    });

    [Fact]
    public Task Barra_de_distribuicao_solta_a_animacao_ao_sair_da_tela() => OnUi(() =>
    {
        using var _ = MotionPreferences.Force(true);
        var bar = new DistributionBar { Height = 8, Value = new StatusDistribution(10, 0, 0, 0, 90) };
        var window = Show(bar);
        try
        {
            bar.Value = new StatusDistribution(60, 0, 0, 0, 40);
            Assert.True(bar.IsSliding);

            window.Content = null;
            ScreenRenderer.Settle(window);

            Assert.False(bar.IsSliding);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Barra_de_tarefas_indeterminada_so_anima_com_animacoes(bool enabled) => OnUi(() =>
    {
        using var _ = MotionPreferences.Force(enabled);
        var service = new BackgroundTaskService();
        var gate = new TaskCompletionSource();
        var item = service.Start("Extrair", (_, _) => gate.Task);
        var view = new TaskBar { DataContext = new TaskBarViewModel(service) };
        var window = Show(view);
        try
        {
            var indicator = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Indicator");
            var staticBar = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("static-working"));

            Assert.True(item.IsIndeterminate);
            Assert.Equal(enabled, indicator.IsVisible);
            Assert.Equal(!enabled, staticBar.IsVisible);
        }
        finally
        {
            window.Close();
            gate.SetResult();
        }
    });

    [Fact]
    public Task Esqueleto_monta_as_faixas_pedidas() => OnUi(() =>
    {
        var skeleton = new Skeleton { Rows = 4 };
        var window = Show(skeleton);
        try
        {
            Assert.Equal(4, skeleton.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("skeleton-bar")));

            skeleton.Rows = 2;
            ScreenRenderer.Settle(window);
            Assert.Equal(2, skeleton.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("skeleton-bar")));
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Estado_vazio_mostra_a_frase_e_so_tem_botao_com_acao() => OnUi(() =>
    {
        var state = new EmptyState { Message = "Nenhum projeto." };
        var window = Show(state);
        try
        {
            var text = state.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Message");
            var button = state.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Action");
            Assert.Equal("Nenhum projeto.", text.Text);
            Assert.False(button.IsVisible);

            var clicks = 0;
            state.ActionText = "Abrir projeto";
            state.ActionCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => clicks++);
            ScreenRenderer.Settle(window);

            Assert.True(button.IsVisible);
            button.Command!.Execute(null);
            Assert.Equal(1, clicks);
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
            ScreenRenderer.Settle(window);

            window.KeyPress(Key.OemQuestion, RawInputModifiers.Shift, PhysicalKey.Slash, "?");
            Assert.True(vm.IsShortcutMapOpen);
            Assert.True(window.GetVisualDescendants().OfType<ShortcutMapView>().Single().IsVisible);

            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.False(vm.IsShortcutMapOpen);

            var box = new TextBox();
            window.Content = new StackPanel { Children = { box } };
            ScreenRenderer.Settle(window);
            box.Focus();
            window.KeyPress(Key.OemQuestion, RawInputModifiers.Shift, PhysicalKey.Slash, "?");
            Assert.False(vm.IsShortcutMapOpen);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public Task Aviso_aparece_no_canto_da_janela_com_desfazer() => OnUi(() =>
    {
        using var temp = new TempFolder();
        var start = new StartViewModel(new RecentProjectsFile(temp.Path), new FakeFolderPicker(null), TimeProvider.System);
        var toasts = new ToastService(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(), a => a());
        var vm = new MainWindowViewModel(start, toasts: toasts);
        var window = new MainWindow { Width = 1200, Height = 800, DataContext = vm };
        try
        {
            window.Show();
            toasts.Show("Linha 12 gravada.", ToastKind.Success, undo: () => { });
            ScreenRenderer.Settle(window);

            var host = window.GetVisualDescendants().OfType<ToastHost>().Single();
            var texts = host.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Linha 12 gravada.", texts);
            Assert.Contains(host.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Desfazer"));
        }
        finally
        {
            window.Close();
        }
    });
}
