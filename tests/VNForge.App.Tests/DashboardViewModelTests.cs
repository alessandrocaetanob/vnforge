using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O Painel lendo um projeto de verdade em disco: os números do anel, a jornada, o gasto e a troca de projeto.</summary>
public class DashboardViewModelTests
{
    private static DashboardViewModel Open(TempFolder temp, params TranslationStatus[] statuses)
    {
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", statuses);
        return new DashboardViewModel { Project = ProjectSummaryReader.Read(root) };
    }

    [Fact]
    public async Task Os_numeros_do_anel_batem_com_o_status_do_projeto()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Reviewed, TranslationStatus.Machine, TranslationStatus.Translated, TranslationStatus.Untranslated);

        await vm.ReloadAsync();

        var status = ProjectStatusReporter.Create(ProjectFolder.Open(vm.Project!.Root));
        Assert.Equal(5, status.Total.Total);
        Assert.Equal(2, vm.ReadyLines);
        Assert.Equal(2, vm.ReviewLines);
        Assert.Equal(5, vm.TotalLines);
        Assert.Equal("2 de 5 linhas prontas", vm.MapSummary);
        Assert.Equal(["Pronto 2", "Esperando revisão 2", "Ainda em japonês 1"], vm.MapLegend.Select(l => l.Text));
        var chapter = Assert.Single(vm.Chapters);
        Assert.Equal("Parte 1", chapter.Name);
        Assert.Equal(1, chapter.Number);
        Assert.Equal("yst00001", chapter.ScriptKey);
    }

    [Fact]
    public async Task Esqueleto_so_aparece_enquanto_nao_ha_capitulos_para_mostrar()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Untranslated);
        using var gate = new SemaphoreSlim(0);
        var vm = new DashboardViewModel(path =>
        {
            gate.Wait(TestContext.Current.CancellationToken);
            return ProjectDashboard.Read(ProjectFolder.Open(path));
        })
        {
            Project = ProjectSummaryReader.Read(root),
        };

        var primeira = vm.ReloadAsync();
        Assert.True(vm.ShowChaptersSkeleton);
        gate.Release();
        await primeira;
        Assert.False(vm.ShowChaptersSkeleton);

        var segunda = vm.ReloadAsync();
        Assert.True(vm.IsLoading);
        Assert.False(vm.ShowChaptersSkeleton);
        gate.Release();
        await segunda;
    }

    [Fact]
    public async Task O_passo_Conferir_diz_o_que_a_conta_deixa_de_fora()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Untranslated, TranslationStatus.Untranslated, TranslationStatus.Approved);

        await vm.ReloadAsync();

        Assert.Equal("Nenhum problema · sem contar 2 linhas sem tradução", vm.Steps[3].Subtitle);
    }

    [Fact]
    public async Task O_passo_Conferir_nao_acrescenta_nada_quando_tudo_esta_na_conta()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Reviewed);

        await vm.ReloadAsync();

        Assert.Equal("Nenhum problema", vm.Steps[3].Subtitle);
    }

    [Fact]
    public async Task A_jornada_tem_cinco_passos_e_nenhum_e_Build_ou_Injetar()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Untranslated, TranslationStatus.Untranslated, TranslationStatus.Machine, TranslationStatus.Approved);

        await vm.ReloadAsync();

        Assert.Equal(["Ler o texto", "Traduzir com a IA", "Revisar", "Conferir", "Testar no jogo"], vm.Steps.Select(s => s.Title));
        Assert.Equal([1, 2, 3, 4, 5], vm.Steps.Select(s => s.Number));
        Assert.Equal("50% traduzido · faltam 2 linhas", vm.Steps[1].Subtitle);
        Assert.Equal("1 de 2 revisadas · falta 1", vm.Steps[2].Subtitle);
        Assert.Equal("O texto do jogo ainda não foi lido", vm.Steps[0].Subtitle);
        Assert.Equal(PipelineStepState.Active, vm.Steps[0].State);
        Assert.Equal(PipelineStepState.Pending, vm.Steps[1].State);
    }

    [Fact]
    public async Task O_gasto_vem_do_registro_do_projeto_em_reais_pela_cotacao_dos_ajustes()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        vm.Context = () => new DashboardContext(new VNForge.AI.Cost.ExchangeRate(5.40m, new DateOnly(2026, 10, 22)), new VNForge.AI.Cost.BudgetLimits(300m, null), true);
        await vm.ReloadAsync();
        Assert.Equal("R$ 0,00 de R$ 300,00", vm.SpentText);

        AiUsageLog.TryRecord(ProjectFolder.Open(vm.Project!.Root), new AiUsageTotals(1_500_000, 600_000, 0, 0, 26.40m, 1));
        await vm.ReloadAsync();

        Assert.Equal("R$ 142,56 de R$ 300,00", vm.SpentText);
        Assert.Equal(142.56, vm.SpentBrl, 2);
        Assert.Equal(300, vm.LimitBrl);
        Assert.Equal("Faltam R$ 157,44 para o limite.", vm.SpendHint);
    }

    [Fact]
    public async Task Trocar_de_projeto_limpa_os_numeros_do_anterior()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Approved, TranslationStatus.Untranslated);
        await vm.ReloadAsync();
        Assert.NotEmpty(vm.Chapters);

        vm.Project = ProjectSummaryReader.Read(SampleProjects.Create(temp.SubFolder("outro"), "Outra VN", TranslationStatus.Approved));

        Assert.Empty(vm.Chapters);
        Assert.Empty(vm.Steps);
        Assert.Empty(vm.Todos);
        Assert.Equal(string.Empty, vm.MapSummary);
        Assert.Equal(string.Empty, vm.NextStepLabel);
        Assert.False(vm.HasNextStep);
    }

    [Fact]
    public async Task Projeto_ilegivel_mostra_o_erro_e_limpa_a_tela()
    {
        using var temp = new TempFolder();
        var vm = Open(temp, TranslationStatus.Machine);
        await vm.ReloadAsync();
        File.WriteAllText(Path.Combine(vm.Project!.Root, "text", "yst00001.json"), "{ quebrado");

        await vm.ReloadAsync();

        Assert.StartsWith("Não foi possível ler o projeto", vm.ErrorMessage);
        Assert.Empty(vm.Chapters);
        Assert.Empty(vm.Steps);
    }

    [Fact]
    public async Task Sem_projeto_a_tela_fica_vazia()
    {
        var vm = new DashboardViewModel();

        await vm.ReloadAsync();

        Assert.False(vm.HasProject);
        Assert.Empty(vm.Steps);
        Assert.Equal(string.Empty, vm.SpentText);
        Assert.True(vm.ShowEmptyState);
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
            if (e.PropertyName == nameof(DashboardViewModel.MapSummary) && main.Dashboard.MapSummary.Length > 0)
            {
                loaded.TrySetResult();
            }
        };
        main.SelectedNav = item;
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Same(main.Dashboard, main.CurrentPage);
        Assert.Equal("1 de 2 linhas prontas", main.Dashboard.MapSummary);
    }

    [Fact]
    public async Task A_janela_principal_passa_a_chave_e_o_limite_dos_ajustes_ao_Painel()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine);
        var main = new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(root), new FixedClock(DateTimeOffset.UnixEpoch)));
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;

        await main.Dashboard.ReloadAsync();

        Assert.False(main.ApiKeyConfigured);
        Assert.Contains(main.Dashboard.Todos, t => t.Title == "Falta a chave do Claude");
    }
}
