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
    public async Task Sem_recentes_a_lista_fica_vazia_e_nada_e_selecionado()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        await vm.LoadAsync();

        Assert.Empty(vm.Recents);
        Assert.Null(vm.Selected);
        Assert.False(vm.HasRecents);
    }

    [Fact]
    public async Task Abrir_projeto_criado_pela_CLI_mostra_nome_e_progresso()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN — PT-BR",
            TranslationStatus.Approved, TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated);
        var vm = Create(temp, pickerAnswer: root);
        await vm.LoadAsync();

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        var item = Assert.Single(vm.Recents);
        Assert.Same(item, vm.Selected);
        Assert.Equal("Minha VN — PT-BR", item.Name);
        Assert.Equal("75%", item.PercentLabel);
        Assert.Equal(50, item.Distribution.Approved);
        Assert.Equal(25, item.Distribution.Machine);
        Assert.Equal(25, item.Distribution.Pending);
        Assert.Equal("4", item.Lines);
        Assert.Equal("agora", item.WhenLabel);
    }

    [Fact]
    public async Task Projeto_aberto_vai_para_os_recentes_e_sobrevive_a_um_novo_ViewModel()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        await Create(temp, pickerAnswer: root).OpenFolderCommand.ExecuteAsync(null);

        var outro = Create(temp);
        await outro.LoadAsync();

        Assert.Equal("Minha VN", Assert.Single(outro.Recents).Name);
        Assert.Equal("Minha VN", outro.Selected?.Name);
    }

    [Fact]
    public async Task Abrir_dispara_ProjectOpened_com_o_resumo()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, pickerAnswer: root);
        ProjectSummary? aberto = null;
        vm.ProjectOpened += (_, s) => aberto = s;

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal("Minha VN", aberto?.Name);
    }

    [Fact]
    public async Task Cancelar_o_seletor_nao_muda_nada()
    {
        using var temp = new TempFolder();
        var picker = new FakeFolderPicker(null);
        var vm = Create(temp, picker: picker);

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Equal(1, picker.Calls);
        Assert.Empty(vm.Recents);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task Pasta_que_nao_e_projeto_mostra_erro_e_nao_entra_nos_recentes()
    {
        using var temp = new TempFolder();
        var vm = Create(temp, pickerAnswer: temp.SubFolder("vazia"));

        await vm.OpenFolderCommand.ExecuteAsync(null);

        Assert.Contains("não é um projeto do VNForge", vm.ErrorMessage);
        Assert.Empty(vm.Recents);
    }

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
        Assert.Contains("não foi encontrado", item.Problem);
        Assert.False(vm.OpenSelectedInEditorCommand.CanExecute(null));
    }

    [Fact]
    public async Task Abrir_no_Editor_dispara_EditorRequested_para_o_selecionado()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var vm = Create(temp, pickerAnswer: root);
        await vm.OpenFolderCommand.ExecuteAsync(null);
        ProjectSummary? pedido = null;
        vm.EditorRequested += (_, s) => pedido = s;

        vm.OpenSelectedInEditorCommand.Execute(null);

        Assert.Equal("Minha VN", pedido?.Name);
    }

    [Fact]
    public void Novo_projeto_pede_o_assistente_e_limpa_os_avisos()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);
        vm.NoticeMessage = "aviso antigo";
        var pedidos = 0;
        vm.NewProjectRequested += (_, _) => pedidos++;

        vm.NewProjectCommand.Execute(null);

        Assert.Equal(1, pedidos);
        Assert.Null(vm.NoticeMessage);
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
    public async Task Abrir_dois_projetos_deixa_o_ultimo_no_topo_e_selecionado()
    {
        using var temp = new TempFolder();
        var a = SampleProjects.Create(temp.SubFolder("a"), "A", TranslationStatus.Approved);
        var b = SampleProjects.Create(temp.SubFolder("b"), "B", TranslationStatus.Untranslated);
        var config = new RecentProjectsFile(temp.SubFolder("config"));
        var vm = new StartViewModel(config, new FakeFolderPicker(null), new FixedClock(Now));

        await vm.OpenAsync(a);
        await vm.OpenAsync(b);

        Assert.Equal(["A", "B"], vm.Recents.Select(i => i.Name).Order());
        Assert.Equal("B", vm.Selected?.Name);
        Assert.StartsWith("2 projetos", vm.RecentsSummary);
    }

    [Fact]
    public async Task Cargas_simultaneas_nao_duplicam_itens()
    {
        using var temp = new TempFolder();
        var root = SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Approved);
        var config = new RecentProjectsFile(temp.SubFolder("config"));
        config.Add(root, Now);
        var vm = new StartViewModel(config, new FakeFolderPicker(null), new FixedClock(Now));

        await Task.WhenAll(vm.LoadAsync(), vm.LoadAsync(), vm.LoadAsync());

        Assert.Single(vm.Recents);
        Assert.StartsWith("1 projeto", vm.RecentsSummary);
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
