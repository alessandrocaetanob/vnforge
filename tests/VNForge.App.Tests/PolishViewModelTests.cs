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
    public void Aviso_some_sozinho_depois_do_tempo()
    {
        var toasts = Service();
        toasts.Show("Linha 1 gravada.");

        clock.Advance(ToastService.Lifetime - TimeSpan.FromMilliseconds(1));
        Assert.Single(toasts.Toasts);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty(toasts.Toasts);
    }

    [Fact]
    public void Aviso_com_a_mesma_chave_substitui_o_anterior()
    {
        var toasts = Service();
        toasts.Show("Linha 1 gravada.", key: "line-save");
        toasts.Show("Linha 2 gravada.", key: "line-save");

        Assert.Equal("Linha 2 gravada.", Assert.Single(toasts.Toasts).Message);
    }

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

    [Fact]
    public void Desfazer_roda_a_acao_e_fecha_o_aviso()
    {
        var toasts = Service();
        var undone = 0;
        var toast = toasts.Show("Linha 1 aprovada.", undo: () => undone++);

        Assert.True(toast.CanUndo);
        Assert.Equal("Desfazer", toast.UndoLabel);
        toast.UndoCommand.Execute(null);

        Assert.Equal(1, undone);
        Assert.Empty(toasts.Toasts);
    }

    [Fact]
    public void Sem_acao_nao_ha_desfazer()
    {
        var toast = Service().Show("Build pronto.");

        Assert.False(toast.CanUndo);
        Assert.Null(toast.UndoLabel);
    }
}

public class ThemeSettingsTests
{
    [Fact]
    public void Tema_padrao_e_o_do_sistema_e_o_arquivo_guarda_a_escolha()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.Path);
        Assert.Equal(ThemeChoice.System, file.Load().Theme);

        file.Save(new AppSettings(3, ThemeChoice.Dark));

        Assert.Equal(new AppSettings(3, ThemeChoice.Dark), file.Load());
    }

    [Fact]
    public void Tema_ilegivel_volta_ao_sistema_sem_perder_a_concorrencia()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.Path);
        File.WriteAllText(file.Path, """{ "concurrency": 4, "theme": "roxo" }""");

        Assert.Equal(new AppSettings(4, ThemeChoice.System), file.Load());
    }

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

    [Fact]
    public void Mudar_a_concorrencia_nao_perde_o_tema()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), file, _ => null);
        settings.SelectedTheme = settings.Themes.Single(t => t.Value == ThemeChoice.Dark);

        settings.Concurrency = 5;

        Assert.Equal(new AppSettings(5, ThemeChoice.Dark), file.Load());
    }
}

public class ToastsFromPagesTests : IDisposable
{
    private readonly TempFolder pasta = new();
    private readonly FakeTimeProvider relogio = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTimeProvider avisos = new();

    public void Dispose() => pasta.Dispose();

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
        await vm.Editor.Loading;
        return (vm, raiz);
    }

    private static TextUnit Gravada(string raiz, int n) => ProjectFolder.Open(raiz).LoadText("yst00001")[n];

    [Fact]
    public async Task Gravar_a_traducao_avisa_com_desfazer_que_devolve_o_texto_anterior()
    {
        var (vm, raiz) = await Abrir(TranslationStatus.Untranslated, TranslationStatus.Untranslated);
        vm.Editor.Detail!.Translation = "Confirmar";
        Assert.True(vm.Editor.FlushPendingEdits());

        var aviso = Assert.Single(vm.Toasts.Toasts);
        Assert.Equal("Linha 1 gravada.", aviso.Message);
        Assert.True(aviso.CanUndo);

        aviso.UndoCommand.Execute(null);
        await vm.Editor.Loading;

        Assert.Equal(TranslationStatus.Untranslated, Gravada(raiz, 0).Status);
        Assert.False(Gravada(raiz, 0).HasTranslation);
        Assert.Empty(vm.Toasts.Toasts);
    }

    [Fact]
    public async Task Aprovar_avisa_que_a_linha_foi_aprovada_e_desfazer_restaura_o_status()
    {
        var (vm, raiz) = await Abrir(TranslationStatus.Translated, TranslationStatus.Untranslated);
        vm.Editor.Detail!.ApproveCommand.Execute(null);

        var aviso = vm.Toasts.Toasts.Single(t => t.Key == "line-save");
        Assert.Equal("Linha 1 aprovada.", aviso.Message);
        Assert.Equal(TranslationStatus.Approved, Gravada(raiz, 0).Status);

        aviso.UndoCommand.Execute(null);
        await vm.Editor.Loading;

        Assert.Equal(TranslationStatus.Translated, Gravada(raiz, 0).Status);
    }

    [Fact]
    public async Task Gravacao_sem_mudanca_de_texto_ou_status_nao_avisa()
    {
        var (vm, _) = await Abrir(TranslationStatus.Translated);
        vm.Editor.Detail!.Notes = "uma nota";
        Assert.True(vm.Editor.FlushPendingEdits());

        Assert.Empty(vm.Toasts.Toasts);
    }

    [Fact]
    public async Task Mapa_de_atalhos_abre_e_fecha()
    {
        var (vm, _) = await Abrir(TranslationStatus.Translated);
        Assert.False(vm.IsShortcutMapOpen);

        vm.ToggleShortcutMapCommand.Execute(null);
        Assert.True(vm.IsShortcutMapOpen);

        vm.CloseShortcutMapCommand.Execute(null);
        Assert.False(vm.IsShortcutMapOpen);
    }

    [Fact]
    public void Mapa_de_atalhos_lista_os_atalhos_do_app()
    {
        var teclas = ShortcutMap.Groups.SelectMany(g => g.Entries).Select(e => string.Join('+', e.Keys)).ToList();

        Assert.Contains("Ctrl+Enter", teclas);
        Assert.Contains("?", teclas);
    }
}

public class EmptyStatesViewModelTests
{
    [Fact]
    public async Task Painel_sem_projeto_mostra_o_estado_vazio_com_a_acao_de_abrir()
    {
        var dashboard = new DashboardViewModel();
        var requested = 0;
        dashboard.StartRequested += (_, _) => requested++;

        Assert.True(dashboard.ShowEmptyState);
        Assert.Equal("Abrir projeto", dashboard.EmptyActionText);

        dashboard.OpenStartCommand.Execute(null);
        Assert.Equal(1, requested);
        await dashboard.ReloadAsync();
    }

    [Fact]
    public async Task Painel_com_projeto_lendo_mostra_esqueleto_e_nao_o_estado_vazio()
    {
        var gate = new TaskCompletionSource();
        var dashboard = new DashboardViewModel(_ =>
        {
            gate.Task.Wait();
            throw new IOException("falha");
        })
        {
            Project = EditorFixtures.Summary(),
        };

        var reload = dashboard.ReloadAsync();
        Assert.True(dashboard.IsLoading);
        Assert.False(dashboard.ShowEmptyState);

        gate.SetResult();
        await reload;
        Assert.False(dashboard.IsLoading);
        Assert.Contains("falha", dashboard.ErrorMessage);
        Assert.False(dashboard.ShowEmptyState);
    }

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

    [Fact]
    public async Task Conferencia_so_fica_limpa_depois_de_conferir_sem_achar_nada()
    {
        using var temp = new TempFolder();
        var raiz = SampleProjects.Create(temp.Path, "Teste", TranslationStatus.Approved);
        var vm = new ConferenceViewModel { Project = EditorFixtures.Summary("Teste", raiz) };
        Assert.False(vm.IsClear);

        await vm.RefreshAsync();

        Assert.True(vm.IsClear);
        Assert.False(vm.ShowEmptyText);
    }
}
