using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R1")]
[Trait("Risco", "R2")]
public class ToastsFromPagesTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTimeProvider avisos = new();

    public void Dispose()
    {
        pasta.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<(MainWindowViewModel Vm, string Root)> Abrir(params TranslationStatus[] statuses)
    {
        var config = pasta.SubFolder("config");
        var raiz = SampleProjects.Create(pasta.SubFolder("projeto"), "Teste", statuses);
        var start = new StartViewModel(new RecentProjectsFile(config), new FakeFolderPicker(raiz), new FixedClock(relogio.GetUtcNow()));
        var vm = new MainWindowViewModel(start, toasts: new ToastService(avisos, a => a()))
        {
            Editor = new EditorViewModel(clock: relogio, postToUi: a => a()),
        };
        await start.OpenFolderCommand.ExecuteAsync(null);
        await vm.ProjectLoading;
        return (vm, raiz);
    }

    private static TextUnit Gravada(string raiz, int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    [Fact]
    public async Task Desfazer_recusado_porque_a_linha_mudou_mostra_aviso_de_erro_e_nao_mexe_no_arquivo()
    {
        var (vm, raiz) = await Abrir(TranslationStatus.Untranslated);
        vm.Editor.Detail!.Translation = "Confirmar";
        Assert.True(vm.Editor.FlushPendingEdits());
        var aviso = Assert.Single(vm.Toasts.Toasts);
        var projeto = ProjectFolder.Open(raiz);
        var units = projeto.LoadText("yst00001").ToList();
        units[0] = units[0] with { Translation = "Outro texto" };
        projeto.SaveText("yst00001", units);

        aviso.UndoCommand.Execute(null);

        var erro = Assert.Single(vm.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, erro.Kind);
        Assert.Contains("A linha mudou depois deste aviso; nada foi desfeito.", erro.Message);
        Assert.Equal("Outro texto", Gravada(raiz, 0).Translation);
    }

    [Fact]
    public async Task Desfazer_a_injecao_sem_poder_restaurar_explica_em_vez_de_so_navegar()
    {
        var (vm, _) = await Abrir(TranslationStatus.Translated);

        vm.ShowBuildNotice(new BuildNotice(BuildNoticeKind.Injected, "Injetado no jogo: 1 arquivo(s), com backup."));
        vm.Toasts.Toasts.Single(t => t.Key == "build").UndoCommand.Execute(null);

        var aviso = Assert.Single(vm.Toasts.Toasts);
        Assert.Equal(ToastKind.Warning, aviso.Kind);
        Assert.Contains("Não dá para restaurar", aviso.Message);
    }
}

internal sealed class SwitchableFolderPicker(string answer) : IFolderPicker
{
    public string Answer { get; set; } = answer;

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(Answer);
}
