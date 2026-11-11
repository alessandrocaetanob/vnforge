using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

public class StartViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StartViewModel Create(TempFolder temp, string? pickerAnswer = null, FakeFolderPicker? picker = null) =>
        new(new RecentProjectsFile(temp.SubFolder("config")), picker ?? new FakeFolderPicker(pickerAnswer), new FixedClock(Now));

    [Fact]
    public async Task Texto_corrompido_mostra_erro_e_nao_entra_nos_recentes()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        File.WriteAllText(Path.Combine(root, "text", "yst00001.json"), "{ quebrado");
        var vm = Create(temp, pickerAnswer: root);

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(vm.Recents);
    }

    [Fact]
    public async Task Recente_cuja_pasta_sumiu_aparece_indisponivel_com_o_aviso()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        await Create(temp, pickerAnswer: root).OpenFolderCommand.ExecuteAsync(null);
        Directory.Delete(root, recursive: true);

        var vm = Create(temp);
        await vm.LoadAsync();

        var item = Assert.Single(vm.Recents);
        Assert.False(item.IsAvailable);
        Assert.Contains("não foi encontrada", item.Problem);
        Assert.False(vm.OpenSelectedInEditorCommand.CanExecute(null));
    }

    [Fact]
    public async Task Remover_dos_recentes_tira_o_item_e_grava()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, pickerAnswer: root);
        await vm.OpenFolderCommand.ExecuteAsync(null);

        await vm.RemoveSelectedCommand.ExecuteAsync(null);

        Assert.Empty(vm.Recents);
        var outro = Create(temp);
        await outro.LoadAsync();
        Assert.Empty(outro.Recents);
    }

    [Fact]
    public async Task Comandos_do_detalhe_sem_projeto_selecionado_nao_abrem_nada_e_nao_avisam_ninguem()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        await vm.LoadAsync();
        var avisos = 0;
        vm.DashboardRequested += (_, _) => avisos++;
        vm.NextStepRequested += (_, _) => avisos++;
        vm.EditorRequested += (_, _) => avisos++;

        await vm.OpenSelectedCommand.ExecuteAsync(null);
        await vm.OpenNextStepCommand.ExecuteAsync(null);
        await vm.OpenRecentCommand.ExecuteAsync(null);
        vm.OpenSelectedInEditorCommand.Execute(null);

        Assert.Equal(0, avisos);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Com_um_projeto_selecionado_e_ninguem_ouvindo_os_comandos_de_abrir_funcionam_do_mesmo_jeito()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine, TranslationStatus.Untranslated);
        var vm = Create(temp, pickerAnswer: root);
        await vm.LoadAsync();
        await vm.OpenFolderCommand.ExecuteAsync(null);
        var item = Assert.Single(vm.Recents);

        await vm.OpenSelectedCommand.ExecuteAsync(null);
        await vm.OpenRecentCommand.ExecuteAsync(item);
        await vm.OpenNextStepCommand.ExecuteAsync(null);
        vm.OpenSelectedInEditorCommand.Execute(null);
        vm.NewProjectCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(item.Path, vm.Selected!.Path);
    }
}

public class StartViewModelFalhasTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Falha_ao_gravar_os_recentes_nao_impede_abrir_o_projeto()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        // A "pasta de configuração" é um arquivo: criar o recent.json dentro dela dá IOException.
        var bloqueio = Path.Combine(temp.Path, "config-arquivo");
        File.WriteAllText(bloqueio, "x");
        var vm = new StartViewModel(new RecentProjectsFile(bloqueio), new FakeFolderPicker(root), new FixedClock(Now));
        ProjectSummary? aberto = null;
        vm.ProjectOpened += (_, s) => aberto = s;

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal("Minha VN", aberto?.Name);
        Assert.Contains("lista de recentes", vm.NoticeMessage);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Item_com_problema_usa_o_nome_da_pasta_e_mostra_travessao_no_progresso()
    {
        using var temp = new TempFolder();
        var config = new RecentProjectsFile(temp.SubFolder("config"));
        config.Add(Path.Combine(temp.Path, "sumiu"), Now);
        var vm = new StartViewModel(config, new FakeFolderPicker(null), new FixedClock(Now));

        await vm.LoadAsync();

        var item = Assert.Single(vm.Recents);
        Assert.Equal("sumiu", item.Name);
        Assert.Equal("—", item.PercentLabel);
        Assert.Equal("—", item.Lines);
        Assert.Equal("—", item.ModelLabel);
        Assert.Equal(item.Path, item.Subtitle);
        Assert.Equal(100, item.Distribution.Pending);
    }

    [Fact]
    public async Task Item_disponivel_descreve_a_distribuicao_para_leitor_de_tela()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved, TranslationStatus.Untranslated);
        var vm = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));

        await vm.OpenAsync(root);

        var item = vm.Selected!;
        Assert.Equal("Aprovadas 50%, revisadas 0%, traduzidas 0%, IA 0%, pendentes 50%", item.DistributionDescription);
        Assert.Equal("Claude Opus 5.5 · esforço médio", item.ModelLabel);
        Assert.Equal("1", item.Scripts);
        Assert.Equal("Remover acentos (fold)", item.EncodingLabel);
        Assert.StartsWith("YU-RIS 5.x · YPF v500 · ", item.Subtitle);
    }
}
