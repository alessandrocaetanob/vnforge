using VNForge.AI.Settings;
using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Demo;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>A cena de exemplo da primeira vez: abre sem jogo e sem chave, traduz com a IA falsa e não deixa construir nem injetar.</summary>
[Trait("Risco", "R5")]
[Trait("Risco", "R6")]
public sealed class SampleSceneTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private StartViewModel CreateStart() =>
        new(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now), temp.SubFolder("exemplo"));

    [Fact]
    public async Task Experimentar_cria_o_projeto_na_pasta_de_exemplos_abre_e_leva_ao_Painel()
    {
        var start = CreateStart();
        await start.LoadAsync();
        ProjectSummary? dashboard = null;
        start.DashboardRequested += (_, summary) => dashboard = summary;

        await start.OpenSampleCommand.ExecuteAsync(null);

        Assert.Null(start.ErrorMessage);
        Assert.NotNull(dashboard);
        Assert.True(dashboard.IsSample);
        Assert.StartsWith(temp.SubFolder("exemplo"), dashboard.Root, StringComparison.OrdinalIgnoreCase);
        var item = Assert.Single(start.Recents);
        Assert.Equal(SampleProject.Name, item.Name);
    }

    private async Task<(MainWindowViewModel Vm, string Root)> OpenSampleAsync()
    {
        var start = CreateStart();
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(temp.SubFolder("ajustes")), _ => null);
        var vm = new MainWindowViewModel(start, new ResolvedApiKey(null, ApiKeySource.None), null, settings);
        await start.LoadAsync();
        await start.OpenSampleCommand.ExecuteAsync(null);
        start.OpenSelectedInEditorCommand.Execute(null);
        await vm.Editor.Loading;
        vm.Editor.SelectedScript = vm.Editor.Scripts.Single(s => s.Key == "yst00001");
        return (vm, start.Recents[0].Path);
    }

    [Fact]
    public async Task Traduzir_sem_chave_usa_a_IA_falsa_que_devolve_o_portugues_do_exemplo_sem_gasto()
    {
        var (vm, root) = await OpenSampleAsync();

        vm.Editor.TranslatePendingCommand.Execute(null);
        await vm.TranslateOpening;
        Assert.True(vm.Translate.CanStart);
        vm.Translate.StartCommand.Execute(null);
        await vm.TranslationRun;

        var saved = ProjectFolder.Open(root).LoadText("yst00001");
        Assert.All(saved, unit => Assert.NotEqual(TranslationStatus.Untranslated, unit.Status));
        Assert.Equal("Ei, você já terminou este livro?", saved[1].Translation);
        Assert.Equal(TranslationStatus.Machine, saved[1].Status);
        Assert.Equal(0m, AiUsageLog.Load(ProjectFolder.Open(root)).Dollars);
        Assert.Equal(0, AiUsageLog.Load(ProjectFolder.Open(root)).UnpricedRuns);
    }

    [Fact]
    public async Task A_sugestao_do_exemplo_vem_da_IA_de_demonstracao_mesmo_com_a_chave_configurada()
    {
        var (vm, _) = await OpenSampleAsync();
        vm.Editor.SelectedLine = vm.Editor.Lines.Single(l => l.Id == "yst00001:0002");
        var detail = vm.Editor.Detail!;

        await detail.SuggestCommand.ExecuteAsync(null);

        Assert.Equal("Ei, você já terminou este livro?", detail.Suggestion);
        Assert.Equal("IA de demonstração", detail.SuggestionModel);
        Assert.Contains("demonstração", detail.SuggestionSource);
        Assert.DoesNotContain("Configure a chave", detail.SuggestionSource);
        Assert.Contains("gratuito", detail.SuggestionDetails);
    }
}
