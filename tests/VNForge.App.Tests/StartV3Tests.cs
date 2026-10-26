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
    public void Soltar_uma_pasta_pede_o_Novo_projeto_com_ela()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var vm = Create(temp);
        string? chosen = null;
        vm.GameFolderChosen += (_, folder) => chosen = folder;

        vm.DropFolder(game);

        Assert.Equal(game, chosen);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Soltar_um_arquivo_vale_pela_pasta_dele()
    {
        using var temp = new TempFolder();
        var game = temp.SubFolder("jogo");
        var exe = Path.Combine(game, "Jogo.exe");
        File.WriteAllText(exe, "MZ");
        var vm = Create(temp);
        string? chosen = null;
        vm.GameFolderChosen += (_, folder) => chosen = folder;

        vm.DropFolder(exe);

        Assert.Equal(game, chosen);
    }

    [Fact]
    public void Soltar_uma_pasta_que_nao_existe_avisa_e_nao_abre_o_assistente()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        var raised = false;
        vm.GameFolderChosen += (_, _) => raised = true;

        vm.DropFolder(Path.Combine(temp.Path, "sumiu"));

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

        vm.Start.DropFolder(game);

        Assert.Same(wizard, vm.CurrentPage);
        await vm.WizardPreparing;
        Assert.Equal(Path.GetFullPath(game), wizard.GameFolder);
    }

    [Fact]
    public void A_barra_de_status_do_Inicio_mostra_so_a_versao_e_a_chave()
    {
        using var temp = new TempFolder();
        var vm = new MainWindowViewModel(Create(temp));

        Assert.True(vm.IsStartPage);
        Assert.StartsWith("VNForge ", vm.AppVersionText, StringComparison.Ordinal);
        Assert.False(vm.Start.ApiKeyConfigured);
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
