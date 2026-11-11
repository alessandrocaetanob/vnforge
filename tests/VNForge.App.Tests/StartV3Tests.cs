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
}
