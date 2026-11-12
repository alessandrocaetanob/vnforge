using Microsoft.Extensions.Time.Testing;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class ToastServiceTests
{
    private readonly FakeTimeProvider clock = new();

    private ToastService Service() => new(clock, a => a());

    [Fact]
    public void Mantem_no_maximo_tres_avisos_e_descarta_o_mais_antigo()
    {
        var toasts = Service();
        for (var i = 1; i <= ToastService.MaxVisible + 1; i++)
        {
            toasts.Show($"Aviso {i}");
        }

        Assert.Equal(ToastService.MaxVisible, toasts.Toasts.Count);
        Assert.Equal("Aviso 2", toasts.Toasts[0].Message);
    }
}

public class ThemeSettingsTests
{

    [Fact]
    public void Escolher_o_tema_nos_ajustes_grava_e_avisa_o_app()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), file, _ => null);
        ThemeChoice? raised = null;
        settings.ThemeChanged += (_, choice) => raised = choice;
        Assert.Equal(ThemeChoice.System, settings.SelectedTheme!.Value);

        settings.SelectedTheme = settings.Themes.Single(t => t.Value == ThemeChoice.Light);

        Assert.Equal(ThemeChoice.Light, raised);
        Assert.Equal(ThemeChoice.Light, file.Load().Theme);
        Assert.Equal(Avalonia.Styling.ThemeVariant.Light, ThemeChoice.Light.ToVariant());
        Assert.Equal(Avalonia.Styling.ThemeVariant.Default, ThemeChoice.System.ToVariant());
    }
}

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

public class EmptyStatesViewModelTests
{

    [Fact]
    public async Task Editor_lendo_nao_mostra_o_estado_vazio_e_filtro_que_esvazia_oferece_ver_todas()
    {
        var vm = new EditorViewModel(_ => EditorFixtures.Lines(("s1", [EditorFixtures.Unit("s1", 1, "A", TranslationStatus.Approved)])));
        Assert.True(vm.ShowEmptyState);

        vm.Project = EditorFixtures.Summary();
        await vm.Loading;
        Assert.False(vm.ShowEmptyState);

        vm.SelectFilterCommand.Execute(vm.Filters[1]);
        Assert.True(vm.ShowEmptyState);
        Assert.Equal("Ver todas as linhas", vm.EmptyActionText);

        vm.ShowAllCommand.Execute(null);
        Assert.False(vm.ShowEmptyState);
        Assert.Null(vm.EmptyActionText);
    }
}

internal sealed class SwitchableFolderPicker(string answer) : IFolderPicker
{
    public string Answer { get; set; } = answer;

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(Answer);
}
