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
        var file = Assert.Single(vm.Scripts);
        Assert.Equal("yst00001", file.Key);
        Assert.Equal("4", file.LinesLabel);
        Assert.Equal("50%", file.PercentLabel);
        Assert.Equal(new StatusDistribution(25, 25, 0, 25, 25), vm.Distribution);
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
        Assert.Equal("1 pendente", vm.Stages.Single(s => s.Key == PipelineStageKey.Review).StatusText);
        Assert.Equal("50% · 2 pendentes", vm.Stages.Single(s => s.Key == PipelineStageKey.Translate).Subtitle);
        Assert.Equal("Pendente", vm.Stages.Single(s => s.Key == PipelineStageKey.Build).StatusText);
    }

    [Fact]
    public async Task Personagens_listam_o_falante_e_as_linhas_sem_falante()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Untranslated);

        await vm.ReloadAsync();

        var row = Assert.Single(vm.Characters);
        Assert.Equal("Narração", row.Name);
        Assert.True(row.IsNarration);
        Assert.Equal("2", row.LinesLabel);
    }

    [Fact]
    public async Task Gasto_de_IA_vem_do_registro_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        await vm.ReloadAsync();
        Assert.Equal("US$ 0,00", vm.AiValue);
        Assert.Equal("Nenhuma chamada de IA registrada · Claude Opus 5.5", vm.AiDescription);

        AiUsageLog.TryRecord(ProjectFolder.Open(vm.Project!.Root), new AiUsageTotals(1_500_000, 600_000, 0, 0, 2.1m, 1));
        await vm.ReloadAsync();

        Assert.Equal("US$ 2,10", vm.AiValue);
        Assert.Equal("1,5 M tokens de entrada · 600 mil de saída · Claude Opus 5.5", vm.AiDescription);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_os_numeros_do_anterior()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Untranslated);
        await vm.ReloadAsync();
        Assert.NotEmpty(vm.Scripts);

        vm.Project = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Approved));

        Assert.Empty(vm.Scripts);
        Assert.Empty(vm.Stages);
        Assert.Empty(vm.Steps);
        Assert.Equal(string.Empty, vm.LinesLabel);
        Assert.Equal(string.Empty, vm.ContinueLabel);
        Assert.False(vm.CanContinue);
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
        Assert.Empty(vm.Scripts);
        Assert.Empty(vm.Stages);
    }

    [Fact]
    public async Task Sem_projeto_a_tela_fica_vazia()
    {
        var vm = new DashboardViewModel();

        await vm.ReloadAsync();

        Assert.False(vm.HasProject);
        Assert.Empty(vm.Stages);
        Assert.Equal(string.Empty, vm.AiValue);
    }

    [Fact]
    public async Task Etapas_com_trabalho_pendente_pedem_a_tela_delas_e_Extrair_nao()
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

        Assert.Equal([PipelineStageKey.Translate, PipelineStageKey.Review, PipelineStageKey.Build, PipelineStageKey.Inject], requested);
        Assert.False(vm.Stages.Single(s => s.Key == PipelineStageKey.Extract).IsActionable);
    }

    [Fact]
    public void Build_e_Injetar_levam_a_tela_delas_mesmo_concluidos()
    {
        var done = new PipelineStageViewModel(4, new PipelineStage(PipelineStageKey.Build, true, 0));
        var pending = new PipelineStageViewModel(5, new PipelineStage(PipelineStageKey.Inject, false, 1));

        Assert.True(done.IsActionable);
        Assert.True(pending.IsActionable);
        Assert.Equal("Pendente", pending.StatusText);
    }

    [Theory]
    [InlineData(PipelineStageKey.Translate, NavKey.Editor)]
    [InlineData(PipelineStageKey.Review, NavKey.Conference)]
    [InlineData(PipelineStageKey.Build, NavKey.Build)]
    [InlineData(PipelineStageKey.Inject, NavKey.Build)]
    public async Task O_pedido_de_uma_etapa_navega_para_a_tela_dela(PipelineStageKey stage, NavKey expected)
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Dashboard.ReloadAsync();

        main.Dashboard.OpenStageCommand.Execute(main.Dashboard.Stages.Single(s => s.Key == stage));
        await main.ConferenceLoading;

        Assert.Equal(expected, main.SelectedNav!.Key);
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
        await main.Editor.Loading;
        Assert.True(item.IsEnabled);
        var loaded = new TaskCompletionSource();
        main.Dashboard.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.LinesLabel) && main.Dashboard.LinesLabel.Length > 0)
            {
                loaded.TrySetResult();
            }
        };
        main.SelectedNav = item;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Same(main.Dashboard, main.CurrentPage);
        Assert.Equal("2", main.Dashboard.LinesLabel);
    }
}
