using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O botão "Traduzir com IA" do Editor abre a janela, e "Traduzir" roda o pedido com o provedor falso e mostra o resultado.</summary>
public sealed class MainWindowTranslateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private async Task<(MainWindowViewModel Vm, string Root)> OpenAsync(bool key = true, string? spendLimit = null)
    {
        var root = StartFixtures.Create(
            temp.SubFolder("vn"),
            "Minha VN",
            lines: [("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Approved), ("yst00002", TranslationStatus.Untranslated)]);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
        if (spendLimit is not null)
        {
            settings.SpendLimitText = spendLimit;
        }

        var vm = new MainWindowViewModel(
            start,
            new ResolvedApiKey(key ? "chave" : null, key ? ApiKeySource.Vault : ApiKeySource.None),
            settings: settings)
        {
            TranslationProviderFactory = (_, _) => new FakeTranslationProvider(),
        };
        await start.OpenAsync(root);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
        return (vm, root);
    }

    [Fact]
    public async Task O_botao_do_Editor_abre_a_janela_com_a_cena_aberta()
    {
        var (vm, _) = await OpenAsync();

        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        Assert.True(vm.Translate.IsOpen);
        Assert.Equal(TranslationScope.Scene, vm.Translate.SelectedScope.Scope);
        Assert.Equal(2, vm.Translate.Lines);
        Assert.True(vm.Translate.CanStart);
    }

    [Fact]
    public async Task Traduzir_roda_o_pedido_grava_como_IA_e_avisa_o_resultado_em_portugues()
    {
        var (vm, root) = await OpenAsync();
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;

        var saved = ProjectFolder.Open(root).LoadText("yst00001");
        Assert.False(vm.Translate.IsOpen);
        Assert.Equal([TranslationStatus.Machine, TranslationStatus.Machine, TranslationStatus.Approved], saved.Select(u => u.Status));
        Assert.Equal(TranslationStatus.Untranslated, ProjectFolder.Open(root).LoadText("yst00002")[0].Status);
        var toast = Assert.Single(vm.Toasts.Toasts);
        Assert.Equal(ToastKind.Success, toast.Kind);
        Assert.Contains("2 linhas traduzidas pela IA", toast.Message);
        Assert.Equal(BackgroundTaskState.Completed, vm.TaskService.Tasks.Single().State);
    }

    [Fact]
    public async Task Pedido_acima_do_limite_nao_deixa_traduzir_e_nada_e_gravado()
    {
        var (vm, root) = await OpenAsync(spendLimit: "0,01");
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        Assert.True(vm.Translate.IsOverLimit);
        Assert.False(vm.Translate.StartCommand.CanExecute(null));
        Assert.Equal(TranslationStatus.Untranslated, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
    }

    [Fact]
    public async Task Sem_chave_a_janela_abre_mas_nao_deixa_traduzir()
    {
        var (vm, _) = await OpenAsync(key: false);

        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        Assert.True(vm.Translate.KeyMissing);
        Assert.False(vm.Translate.CanStart);
    }

    [Fact]
    public async Task Definir_agora_leva_ao_Glossario_na_aba_Personagens()
    {
        var (vm, root) = await OpenAsync();
        ProjectFolder.Open(root).SaveCharacters([new("ハル", "Haru")]);
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        vm.Translate.DefineGendersCommand.Execute(null);

        Assert.False(vm.Translate.IsOpen);
        Assert.Equal(NavKey.Glossary, vm.SelectedNav?.Key);
        Assert.Equal(GlossaryTab.Characters, vm.Glossary.SelectedTab);
    }

    [Fact]
    public async Task Abrir_Ajustes_pelo_aviso_de_chave_fecha_a_janela()
    {
        var (vm, _) = await OpenAsync(key: false);
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        vm.Translate.OpenSettingsCommand.Execute(null);

        Assert.False(vm.Translate.IsOpen);
        Assert.Equal(NavKey.Settings, vm.SelectedNav?.Key);
    }
}
