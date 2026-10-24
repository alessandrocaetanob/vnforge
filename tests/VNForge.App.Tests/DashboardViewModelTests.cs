using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class DashboardViewModelTests
{
    private static DashboardViewModel Open(TempFolder temp, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        return new DashboardViewModel { Project = ProjectSummaryReader.Read(root) };
    }

    [Fact]
    public async Task Os_numeros_batem_com_o_status_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Reviewed, TranslationStatus.Machine, TranslationStatus.Untranslated);

        await vm.ReloadAsync();

        var status = ProjectStatusReporter.Create(ProjectFolder.Open(vm.Project!.Root));
        Assert.Equal(4, status.Total.Total);
        Assert.Equal("4", vm.LinesLabel);
        Assert.Equal("75% com tradução", vm.ProgressLabel);
        var file = Assert.Single(vm.Files);
        Assert.Equal("yst00001", file.Name);
        Assert.Equal("4 linhas", file.LinesLabel);
        Assert.Equal("75%", file.PercentLabel);
        Assert.Equal(
            ["Aprovadas 25% · 1", "Revisadas 25% · 1", "Traduzidas 0% · 0", "IA 25% · 1", "Pendentes 25% · 1"],
            vm.Legend);
    }

    [Fact]
    public async Task Etapas_mostram_as_pendencias_em_linhas()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Untranslated, TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Approved);

        await vm.ReloadAsync();

        Assert.Equal(["Extrair", "Traduzir", "Conferir", "Build", "Injetar"], vm.Stages.Select(s => s.Label));
        Assert.Equal([1, 2, 3, 4, 5], vm.Stages.Select(s => s.Number));
        Assert.Equal("2 pendentes", vm.Stages.Single(s => s.Key == PipelineStageKey.Translate).StatusText);
        Assert.Equal("1 pendentes", vm.Stages.Single(s => s.Key == PipelineStageKey.Review).StatusText);
        Assert.Equal("Pendente", vm.Stages.Single(s => s.Key == PipelineStageKey.Build).StatusText);
    }

    [Fact]
    public async Task Personagens_listam_o_falante_e_as_linhas_sem_falante()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Untranslated);

        await vm.ReloadAsync();

        var row = Assert.Single(vm.Characters);
        Assert.Equal("(sem falante)", row.Name);
        Assert.Equal("2 linhas", row.LinesLabel);
    }

    [Fact]
    public async Task Custo_de_IA_vem_do_registro_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        Assert.StartsWith("Nenhuma chamada", await ReloadAndGetCost(vm));

        AiUsageLog.TryRecord(ProjectFolder.Open(vm.Project!.Root), new AiUsageTotals(1_500_000, 600_000, 0, 0, 2.1m, 1));

        Assert.Equal("US$ 2,10 · 2,1 M tokens em 1 execução", await ReloadAndGetCost(vm));
    }

    private static async Task<string> ReloadAndGetCost(DashboardViewModel vm)
    {
        await vm.ReloadAsync();
        return vm.CostLabel;
    }

    [Theory]
    [InlineData(0, "US$ 0,00")]
    [InlineData(0.004, "US$ 0,0040")]
    [InlineData(1234.5, "US$ 1.234,50")]
    public void Dolares_em_pt_BR(double value, string expected) => Assert.Equal(expected, DashboardViewModel.Dollars((decimal)value));

    [Theory]
    [InlineData(420, "420 tokens")]
    [InlineData(850_000, "850 mil tokens")]
    [InlineData(2_100_000, "2,1 M tokens")]
    public void Tokens_em_pt_BR(long value, string expected) => Assert.Equal(expected, DashboardViewModel.Tokens(value));

    [Fact]
    public async Task Projeto_ilegivel_mostra_o_erro_e_limpa_a_tela()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        await vm.ReloadAsync();
        File.WriteAllText(Path.Combine(vm.Project!.Root, "text", "yst00001.json"), "{ quebrado");

        await vm.ReloadAsync();

        Assert.StartsWith("Não foi possível ler o projeto", vm.ErrorMessage);
        Assert.Empty(vm.Files);
        Assert.Empty(vm.Stages);
    }

    [Fact]
    public async Task Sem_projeto_a_tela_fica_vazia()
    {
        var vm = new DashboardViewModel();

        await vm.ReloadAsync();

        Assert.False(vm.HasProject);
        Assert.Empty(vm.Stages);
        Assert.Equal(string.Empty, vm.CostLabel);
    }

    [Fact]
    public async Task So_Traduzir_e_Conferir_abrem_o_Editor()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        await vm.ReloadAsync();
        var requested = new List<PipelineStageKey>();
        vm.StageRequested += (_, key) => requested.Add(key);

        foreach (var stage in vm.Stages)
        {
            vm.OpenStageCommand.Execute(stage);
        }

        Assert.Equal([PipelineStageKey.Translate, PipelineStageKey.Review], requested);
    }

    [Fact]
    public async Task Abrir_o_Painel_pela_navegacao_le_o_projeto_aberto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Untranslated);
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        var item = main.Nav.Single(n => n.Key == NavKey.Dashboard);
        Assert.False(item.IsEnabled);

        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        Assert.True(item.IsEnabled);
        main.SelectedNav = item;
        await main.Dashboard.ReloadAsync();

        Assert.Same(main.Dashboard, main.CurrentPage);
        Assert.Equal("2", main.Dashboard.LinesLabel);
    }
}
