using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>O Início da v3: a parte pronta da lista, o próximo passo do Painel, a pasta solta que abre o Novo projeto e a barra de status.</summary>
public class StartV3Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StartViewModel Create(TempFolder temp, string? pickerAnswer = null) =>
        new(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(pickerAnswer), new FixedClock(Now));

    private static string Project(TempFolder temp) =>
        StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines:
        [
            ("yst00001", TranslationStatus.Approved),
            ("yst00001", TranslationStatus.Reviewed),
            ("yst00002", TranslationStatus.Machine),
            ("yst00002", TranslationStatus.Untranslated),
        ]);

    [Fact]
    public async Task A_lista_mostra_linhas_e_a_parte_pronta_que_o_anel_usa()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        await vm.OpenAsync(Project(temp));

        var item = vm.Selected!;
        Assert.Equal("4 linhas", item.LinesLabel);
        Assert.Equal("50%", item.ReadyLabel);
        Assert.Equal((2d, 1d, 4d), (item.ReadyLines, item.ReviewLines, item.TotalLines));
        Assert.Equal("50%", item.ReadyColumnLabel);
        Assert.Equal("Caprichada", item.QualityLabel);
        Assert.Equal("1 projeto", vm.RecentsSummary);
    }

    [Fact]
    public async Task O_cartao_usa_o_proximo_passo_e_a_frase_de_horas_do_Painel()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        await vm.OpenAsync(Project(temp));
        await vm.DetailLoading;

        Assert.True(vm.Detail.HasNextStep);
        Assert.StartsWith("Revisar ", vm.Detail.NextStepLabel, StringComparison.Ordinal);
        Assert.Contains("revisão", vm.Detail.PaceText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_botao_do_proximo_passo_abre_o_projeto_e_entrega_o_destino_do_Painel()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        await vm.OpenAsync(Project(temp));
        await vm.DetailLoading;
        var opened = 0;
        DashboardNavigation? target = null;
        vm.ProjectOpened += (_, _) => opened++;
        vm.NextStepRequested += (_, request) => target = request;

        await vm.OpenNextStepCommand.ExecuteAsync(null);

        Assert.Equal(1, opened);
        Assert.Equal(NavKey.Editor, target?.Target);
    }

    [Fact]
    public async Task Soltar_uma_pasta_pede_o_Novo_projeto_com_ela()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var vm = Create(temp);
        string? chosen = null;
        vm.GameFolderChosen += (_, folder) => chosen = folder;

        await vm.DropFolderAsync(game);

        Assert.Equal(game, chosen);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Soltar_um_arquivo_vale_pela_pasta_dele()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var exe = Path.Combine(game, "Jogo.exe");
        File.WriteAllText(exe, "MZ");
        var vm = Create(temp);
        string? chosen = null;
        vm.GameFolderChosen += (_, folder) => chosen = folder;

        await vm.DropFolderAsync(exe);

        Assert.Equal(game, chosen);
    }

    [Fact]
    public async Task Soltar_uma_pasta_que_nao_existe_avisa_e_nao_abre_o_assistente()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var raised = false;
        vm.GameFolderChosen += (_, _) => raised = true;

        await vm.DropFolderAsync(Path.Combine(temp.Path, "sumiu"));

        Assert.False(raised);
        Assert.Contains("não existe", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Escolher_a_pasta_trata_a_resposta_do_seletor_como_uma_pasta_solta()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var vm = Create(temp, game);
        string? chosen = null;
        vm.GameFolderChosen += (_, folder) => chosen = folder;

        await vm.ChooseGameFolderCommand.ExecuteAsync(null);

        Assert.Equal(game, chosen);
    }

    [Fact]
    public void Na_primeira_vez_sao_quatro_passos_numerados()
    {
        Assert.Equal([1, 2, 3, 4], StartViewModel.FirstSteps.Select(s => s.Number));
        Assert.Equal(["Apontar a pasta do jogo", "Escolher os acentos", "Traduzir", "Testar no jogo"], StartViewModel.FirstSteps.Select(s => s.Title));
    }

    [Fact]
    public void A_lista_da_primeira_vez_diz_se_a_chave_esta_guardada()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        Assert.Contains("Sem ela", vm.ApiKeyNeedText, StringComparison.Ordinal);
        vm.ApiKeyConfigured = true;
        Assert.Contains("já está guardada", vm.ApiKeyNeedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pasta_solta_abre_o_assistente_com_a_pasta_preenchida()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, "Jogo.exe"), "MZ");
        var wizard = new NewProjectWizardViewModel(new FakeFolderPicker(null), [new FakeEngine()]);
        var vm = new MainWindowViewModel(Create(temp), newProjectWizard: () => wizard);

        await vm.Start.DropFolderAsync(game);

        Assert.Same(wizard, vm.CurrentPage);
        await vm.WizardPreparing;
        Assert.Equal(Path.GetFullPath(game), wizard.GameFolder);
    }

    [Fact]
    public async Task Soltar_a_pasta_de_um_projeto_do_VNForge_abre_o_projeto_em_vez_do_assistente()
    {
        using var temp = new TempFolder();
        var root = Project(temp);
        var vm = Create(temp);
        var chosen = false;
        ProjectSummary? opened = null;
        vm.GameFolderChosen += (_, _) => chosen = true;
        vm.ProjectOpened += (_, summary) => opened = summary;

        await vm.DropFolderAsync(root);

        Assert.False(chosen);
        Assert.Equal("Minha VN", opened?.Name);
        Assert.Equal("Minha VN", vm.Selected?.Name);
    }

    [Fact]
    public async Task Soltar_varias_pastas_avisa_que_so_a_primeira_vale()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        await vm.DropFolderAsync(temp.SubFolder("jogo"), dropped: 3);

        Assert.Contains("Só a primeira", vm.NoticeMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_proximo_passo_aberto_pelo_Inicio_cai_no_script_do_passo_e_nao_no_primeiro()
    {
        using var temp = new TempFolder();
        var vm = new MainWindowViewModel(Create(temp));
        await vm.Start.OpenAsync(Project(temp));
        await vm.Start.DetailLoading;
        var target = vm.Start.Detail.NextStepTarget;
        Assert.Equal("yst00002", target?.ScriptKey);

        await vm.Start.OpenNextStepCommand.ExecuteAsync(null);
        await vm.LineNavigation;

        Assert.Equal(NavKey.Editor, vm.SelectedNav?.Key);
        Assert.Equal(target?.ScriptKey, vm.Editor.SelectedScript?.Key);
    }

    [Fact]
    public async Task Voltar_ao_Inicio_le_o_projeto_de_novo()
    {
        using var temp = new TempFolder();
        var vm = new MainWindowViewModel(Create(temp));
        await vm.Start.OpenAsync(Project(temp));
        await vm.Start.DetailLoading;
        var before = vm.Start.Selected;
        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Dashboard);

        vm.SelectedNav = vm.Nav[0];
        for (var i = 0; i < 200 && ReferenceEquals(vm.Start.Selected, before); i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        Assert.NotSame(before, vm.Start.Selected);
        Assert.Equal(before?.Name, vm.Start.Selected?.Name);
    }

    [Fact]
    public async Task Percorrer_a_lista_depressa_le_so_o_ultimo_projeto()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        foreach (var name in new[] { "a", "b", "c" })
        {
            await vm.OpenAsync(StartFixtures.Create(temp.SubFolder(name), name.ToUpperInvariant(), lines: [("yst00001", TranslationStatus.Machine)]));
        }

        vm.Selected = vm.Recents.Single(r => r.Name == "A");
        vm.Selected = vm.Recents.Single(r => r.Name == "B");
        vm.Selected = vm.Recents.Single(r => r.Name == "C");
        await vm.DetailLoading;

        Assert.Equal("C", vm.Detail.Project?.Name);
        Assert.True(vm.Detail.HasNextStep);
    }

    [Fact]
    public async Task A_barra_de_status_do_Inicio_mostra_so_a_versao_e_a_chave_e_as_outras_telas_voltam_com_o_resto()
    {
        using var temp = new TempFolder();
        var vm = new MainWindowViewModel(Create(temp));
        await vm.Start.OpenAsync(Project(temp));

        Assert.True(vm.IsStartPage);
        Assert.StartsWith("VNForge ", vm.AppVersionText, StringComparison.Ordinal);
        Assert.False(vm.Start.ApiKeyConfigured);
        Assert.NotEmpty(vm.EncodingText);

        vm.SelectedNav = vm.Nav.Single(n => n.Key == NavKey.Dashboard);

        Assert.False(vm.IsStartPage);
    }

    [Fact]
    public async Task Projeto_que_sumiu_e_o_erro_de_abrir_nao_mostram_o_caminho_da_pasta()
    {
        using var temp = new TempFolder();
        var root = Project(temp);
        var vm = Create(temp);
        await vm.OpenAsync(root);
        await vm.DetailLoading;
        Directory.Delete(root, recursive: true);
        await vm.LoadAsync();

        Assert.True(vm.Selected?.HasProblem);
        Assert.DoesNotContain(root, vm.Selected?.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(root, vm.Selected?.Path, StringComparison.OrdinalIgnoreCase);

        var empty = temp.SubFolder("vazia");
        await vm.OpenAsync(empty);
        Assert.DoesNotContain(empty, vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("não foi aberto", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_trilho_fica_desabilitado_ate_um_projeto_abrir()
    {
        using var temp = new TempFolder();
        var vm = new MainWindowViewModel(Create(temp));

        Assert.All(vm.Nav.Where(n => n.Key is not (NavKey.Start or NavKey.Settings)), n => Assert.False(n.IsEnabled));
        await vm.Start.OpenAsync(Project(temp));
        Assert.All(vm.Nav.Where(n => n.Key is not (NavKey.Start or NavKey.Settings or NavKey.Glossary)), n => Assert.True(n.IsEnabled));
    }
}
