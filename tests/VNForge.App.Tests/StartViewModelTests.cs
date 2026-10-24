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
    public void Novo_projeto_avisa_que_o_assistente_ainda_nao_existe_e_indica_a_CLI()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        vm.NewProjectCommand.Execute(null);

        Assert.Contains("vnf init", vm.NoticeMessage);
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
