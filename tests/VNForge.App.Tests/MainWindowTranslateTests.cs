using VNForge.AI.Cost;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O botão "Traduzir com IA" do Editor abre a janela, e "Traduzir" roda o pedido com o provedor falso e mostra o resultado.</summary>
[Trait("Risco", "R5")]
[Trait("Risco", "R6")]
public sealed class MainWindowTranslateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private async Task<(MainWindowViewModel Vm, string Root)> OpenAsync(bool key = true, string? spendLimit = null, Func<string, VNForge.AI.Claude.ClaudeEffort, ITranslationProvider>? factory = null, BackgroundTaskService? tasks = null)
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
            tasks,
            settings)
        {
            TranslationProviderFactory = factory ?? ((_, _) => new FakeTranslationProvider()),
        };
        // Estes testes são da tradução; a passada de editor (#532) tem os dela em MainWindowEditorPassTests.
        vm.Translate.EditorPass = false;
        await start.OpenAsync(root);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
        return (vm, root);
    }

    [Fact]
    public async Task Uma_unica_cena_escolhida_que_nao_e_a_aberta_e_a_que_a_janela_traduz()
    {
        var (vm, _) = await OpenAsync();
        vm.Editor.ChosenScripts.Add(vm.Editor.Scripts.Single(s => s.Key == "yst00002"));

        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;

        Assert.Equal(TranslationScope.Scene, vm.Translate.SelectedScope.Scope);
        Assert.Equal(1, vm.Translate.Lines);
    }

    [Fact]
    public async Task A_traducao_da_IA_deixa_um_ponto_de_restauracao_e_o_Desfazer_da_lista_devolve_as_linhas()
    {
        var (vm, root) = await OpenAsync();
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;

        var entry = Assert.Single(vm.Tasks.Entries);
        Assert.True(entry.CanUndo);
        Assert.Equal(2, entry.Steps[0].Restore!.LineCount);
        Assert.Equal(TranslationStatus.Machine, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);

        await entry.UndoCommand.ExecuteAsync(null);

        var restored = ProjectFolder.Open(root).LoadText("yst00001");
        Assert.Equal([TranslationStatus.Untranslated, TranslationStatus.Untranslated, TranslationStatus.Approved], restored.Select(u => u.Status));
        Assert.False(entry.CanUndo);
        Assert.Equal("2 linhas voltaram ao que eram.", entry.UndoMessage);
        Assert.Contains("2 linhas voltaram", vm.Toasts.Toasts.Last().Message);
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
    public async Task Segundo_pedido_durante_uma_traducao_em_andamento_e_recusado()
    {
        var release = new TaskCompletionSource();
        var (vm, root) = await OpenAsync(factory: (_, _) => new GatedProvider(release.Task));
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        vm.Translate.StartCommand.Execute(null);
        var first = vm.TranslationRun;

        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;

        Assert.Contains("Já há uma tradução em andamento", Assert.Single(vm.Toasts.Toasts).Message);
        release.SetResult();
        await first;
        Assert.Equal(TranslationStatus.Machine, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
    }

    private sealed class BlockingProvider(TaskCompletionSource started) : ITranslationProvider
    {
        public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        }
    }

    private sealed class GatedProvider(Task gate) : ITranslationProvider
    {
        public async Task<IReadOnlyList<TranslationResult>> TranslateAsync(TranslationBatch batch, CancellationToken ct)
        {
            await gate;
            return [.. batch.Lines.Select(u => new TranslationResult(u.Id, "x"))];
        }
    }
}
