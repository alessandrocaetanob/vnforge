using VNForge.AI.Review;
using VNForge.AI.Settings;
using VNForge.AI.Translation;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>"Traduzir com IA" com a passada de editor (#532): depois da tradução, outro pedido deixa sugestões nas linhas e não grava tradução nenhuma.</summary>
public sealed class MainWindowEditorPassTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private async Task<(MainWindowViewModel Vm, string Root)> OpenAsync(Func<string, VNForge.AI.Claude.ClaudeEffort, IEditorPassProvider> pass)
    {
        var root = StartFixtures.Create(
            temp.SubFolder("vn"),
            "Minha VN",
            lines: [("yst00001", TranslationStatus.Untranslated), ("yst00001", TranslationStatus.Untranslated), ("yst00002", TranslationStatus.Untranslated)]);
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
        var vm = new MainWindowViewModel(start, new ResolvedApiKey("chave", ApiKeySource.Vault), null, settings)
        {
            TranslationProviderFactory = (_, _) => new FakeTranslationProvider(),
            EditorPassProviderFactory = pass,
        };
        await start.OpenAsync(root);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
        return (vm, root);
    }

    private static IEditorPassProvider Firm() =>
        new FakeEditorPassProvider(u => new EditorPassProposal(u.Id, EditorSuggestionKind.Fluency, u.Translation + "!", "Mais firme."));

    private async Task Translate(MainWindowViewModel vm)
    {
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;
    }

    [Fact]
    public async Task A_opcao_vem_marcada_e_o_pedido_leva_a_escolha()
    {
        var (vm, _) = await OpenAsync((_, _) => Firm());
        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        TranslationRequest? request = null;
        vm.Translate.StartRequested += (_, r) => request = r;

        Assert.True(vm.Translate.EditorPass);
        Assert.StartsWith("+ ", vm.Translate.EditorPassPriceText);

        vm.Translate.EditorPass = false;
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;

        Assert.False(request!.EditorPass);
    }

    [Fact]
    public async Task Falha_da_passada_avisa_em_portugues_e_a_traducao_continua_gravada()
    {
        var (vm, root) = await OpenAsync((_, _) => new FakeEditorPassProvider(_ => throw new InvalidOperationException("Falha simulada.")));

        await Translate(vm);

        Assert.Equal(TranslationStatus.Machine, ProjectFolder.Open(root).LoadText("yst00001")[0].Status);
        var toast = vm.Toasts.Toasts.Last();
        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.Contains("1 cena ficou sem passada", toast.Message);
    }

    [Fact]
    public void O_aviso_da_passada_descreve_cancelamento_erro_e_limite()
    {
        Assert.Equal(ToastKind.Warning, MainWindowViewModel.DescribeEditorPass(true, null, null).Kind);
        Assert.Equal(ToastKind.Error, MainWindowViewModel.DescribeEditorPass(false, "Sem rede.", null).Kind);
        var (texto, tipo) = MainWindowViewModel.DescribeEditorPass(false, null, new EditorPassJobResult(2, 0, 0, 0, null, 0m, null));
        Assert.Equal(ToastKind.Info, tipo);
        Assert.Equal("A passada de editor leu 2 cenas e não achou ajuste.", texto);
        var limite = MainWindowViewModel.DescribeEditorPass(false, null, new EditorPassJobResult(1, 3, 0, 0, VNForge.AI.Cost.BudgetStop.LimitReached, 1m, null));
        Assert.Equal(ToastKind.Warning, limite.Kind);
        Assert.Contains("limite de gasto", limite.Text);
    }
}
