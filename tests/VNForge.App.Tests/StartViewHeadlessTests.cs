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
    public async Task Projeto_aberto_mostra_Abrir_projeto_e_o_botao_do_proximo_passo()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [("yst00013", TranslationStatus.Approved)]);
        var vm = await OpenAsync(temp, root);

        var labels = await ShowAsync(vm, w => DetailButtons(w).Where(b => b.IsEffectivelyEnabled).Select(b => (string)b.Content!).ToArray());

        Assert.Contains("Abrir projeto", labels);
        Assert.Contains("Testar no jogo", labels);
    }

    [Fact]
    public async Task Projeto_sem_suporte_mostra_o_aviso_e_o_botao_Abrir_projeto_desabilitado()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Segunda VN", engineId: "kirikiri");
        var vm = await OpenAsync(temp, root);

        var (disabled, text) = await ShowAsync(vm, w => (
            DetailButtons(w).Where(b => !b.IsEffectivelyEnabled).Select(b => (string)b.Content!).ToArray(),
            string.Join(' ', w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Inlines is { Count: > 0 } ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text))));

        Assert.Contains("Abrir projeto", disabled);
        Assert.Contains("O VNForge ainda não sabe ler este jogo.", text);
        Assert.Contains("Sem suporte", text);
    }

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
    public async Task Lista_de_recentes_ilegivel_mostra_o_aviso_com_o_nome_do_arquivo()
    {
        using var temp = new TempFolder();
        var config = new RecentProjectsFile(temp.SubFolder("config"));
        File.WriteAllText(config.Path, "{ quebrado");
        var vm = new StartViewModel(config, new FakeFolderPicker(null), new FixedClock(Now));
        await vm.LoadAsync();

        var text = await ShowAsync(vm, w => string.Join(' ', w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
            .Select(t => t.Inlines is { Count: > 0 } ? string.Concat(t.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text)));

        Assert.Contains("Não foi possível ler os projetos recentes.", text);
        Assert.DoesNotContain(config.Path, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(config.Path), text);
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
