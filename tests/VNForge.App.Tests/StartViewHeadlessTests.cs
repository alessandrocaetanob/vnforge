using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.App.Views;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Abre a tela real do Início (tema, estilos e bindings compilados) nos três estados do detalhe: aberto, sem suporte e indisponível.</summary>
public class StartViewHeadlessTests(HeadlessFixture fixture) : IClassFixture<HeadlessFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static async Task<StartViewModel> OpenAsync(TempFolder temp, params string[] roots)
    {
        var vm = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
        foreach (var root in roots)
        {
            await vm.OpenAsync(root);
        }

        return vm;
    }

    private Task<T> ShowAsync<T>(StartViewModel vm, Func<Window, T> inspect) =>
        fixture.Session.Dispatch(
            () =>
            {
                var window = new Window { Width = 1440, Height = 900, Content = new StartView { DataContext = vm } };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    return inspect(window);
                }
                finally
                {
                    window.Close();
                }
            },
            TestContext.Current.CancellationToken);

    private static Button[] DetailButtons(Window window) =>
        [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Content is string)];

    [Fact]
    public async Task Projeto_indisponivel_oferece_remover_da_lista()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN");
        var vm = await OpenAsync(temp, root);
        Directory.Delete(root, recursive: true);
        await vm.LoadAsync();

        var labels = await ShowAsync(vm, w => DetailButtons(w).Select(b => (string)b.Content!).ToArray());

        Assert.Contains("Remover da lista", labels);
    }

    [Fact]
    public async Task Detalhe_usa_o_anel_e_a_lista_a_barra_de_tres_estados()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [("yst00001", TranslationStatus.Approved)]);
        var vm = await OpenAsync(temp, root);

        var (rings, bars) = await ShowAsync(vm, w => (w.GetVisualDescendants().OfType<ProgressRing>().Count(r => r.IsEffectivelyVisible), w.GetVisualDescendants().OfType<Seg3Bar>().Count(b => b.IsEffectivelyVisible)));

        Assert.Equal(1, rings);
        Assert.Equal(1, bars);
    }
}
