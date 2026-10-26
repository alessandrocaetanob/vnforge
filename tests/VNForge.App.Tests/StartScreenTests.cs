using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

/// <summary>Os dados do detalhe do projeto, do rodapé e dos avisos da tela Início (artboard "Início" do canvas v2).</summary>
public class StartScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static StartViewModel Create(TempFolder temp) =>
        new(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(null), new FixedClock(Now));

    private static (string Key, TranslationStatus Status)[] Lines(string key, params TranslationStatus[] statuses) =>
        [.. statuses.Select(s => (key, s))];

    [Fact]
    public async Task Detalhe_mostra_motor_modelo_com_esforco_acentuacao_linhas_e_scripts()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [.. Lines("yst00001", TranslationStatus.Approved, TranslationStatus.Untranslated), .. Lines("yst00002", TranslationStatus.Machine)]);
        var vm = Create(temp);

        await vm.OpenAsync(root);

        var item = vm.Selected!;
        Assert.Equal("YU-RIS 5.x · YPF v500", item.EngineLabel);
        Assert.Equal("Claude Opus 5.5 · esforço médio", item.ModelLabel);
        Assert.Equal("CP1252 (patch opcional da fonte)", item.EncodingLabel);
        Assert.Equal("3", item.Lines);
        Assert.Equal("2", item.Scripts);
        Assert.StartsWith("YU-RIS 5.x · YPF v500 · ", item.Subtitle);
    }

    [Fact]
    public async Task Haiku_aparece_sem_esforco_como_no_artboard()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Teste YPF", model: "claude-haiku-5-5");
        var vm = Create(temp);

        await vm.OpenAsync(root);

        Assert.Equal("Claude Haiku 5.5", vm.Selected!.ModelLabel);
    }

    [Fact]
    public async Task Projeto_sem_build_diz_que_ainda_nao_houve()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN");
        var vm = Create(temp);

        await vm.OpenAsync(root);

        Assert.Equal("Nenhum build ainda", vm.Selected!.LastBuildLabel);
    }

    [Theory]
    [InlineData(14, "há 2 dias · 14 scripts")]
    [InlineData(1, "há 2 dias · 1 script")]
    [InlineData(0, "há 2 dias · ida e volta sem tradução: arquivo idêntico")]
    [InlineData(null, "há 2 dias")]
    public async Task Ultimo_build_mostra_a_data_relativa_e_os_scripts_trocados(int? replaced, string expected)
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN");
        StartFixtures.AddBuild(root, Now.AddDays(-2), replaced);
        var vm = Create(temp);

        await vm.OpenAsync(root);

        Assert.Equal(expected, vm.Selected!.LastBuildLabel);
    }

    [Fact]
    public async Task Continuar_aponta_para_o_ultimo_script_editado()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: [.. Lines("yst00001", TranslationStatus.Approved), .. Lines("yst00002", TranslationStatus.Machine)]);
        File.SetLastWriteTimeUtc(Path.Combine(root, "text", "yst00001.json"), Now.UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(root, "text", "yst00002.json"), Now.UtcDateTime.AddDays(-1));
        var vm = Create(temp);

        await vm.OpenAsync(root);

        Assert.Equal("yst00001", vm.Selected!.ResumeScript);
        Assert.Equal("Continuar em yst00001", vm.Selected.ResumeLabel);
        Assert.True(vm.OpenSelectedInEditorCommand.CanExecute(null));
    }

    [Fact]
    public async Task Projeto_sem_scripts_continua_no_Editor_sem_citar_script()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Recem extraido");
        var vm = Create(temp);

        await vm.OpenAsync(root);

        Assert.Null(vm.Selected!.ResumeScript);
        Assert.Equal("Continuar no Editor", vm.Selected.ResumeLabel);
    }

    [Fact]
    public async Task Continuar_entrega_o_resumo_com_o_script_ao_Editor()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: Lines("yst00007", TranslationStatus.Approved));
        var vm = Create(temp);
        await vm.OpenAsync(root);
        ProjectSummary? pedido = null;
        vm.EditorRequested += (_, s) => pedido = s;

        vm.OpenSelectedInEditorCommand.Execute(null);

        Assert.Equal("yst00007", pedido?.LastEditedScript);
    }

    [Fact]
    public async Task Abrir_projeto_do_detalhe_reabre_o_selecionado_e_pede_o_Painel()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: Lines("yst00001", TranslationStatus.Approved));
        var vm = Create(temp);
        await vm.OpenAsync(root);
        var eventos = new List<string>();
        vm.ProjectOpened += (_, s) => eventos.Add("aberto " + s.Name);
        vm.DashboardRequested += (_, s) => eventos.Add("painel " + s.Name);

        await vm.OpenSelectedCommand.ExecuteAsync(null);

        Assert.Equal(["aberto Minha VN", "painel Minha VN"], eventos);
    }

    [Fact]
    public async Task Abrir_projeto_do_detalhe_nao_pede_o_Painel_se_a_pasta_ficou_ilegivel()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: Lines("yst00001", TranslationStatus.Approved));
        var vm = Create(temp);
        await vm.OpenAsync(root);
        File.WriteAllText(Path.Combine(root, "text", "yst00001.json"), "{ quebrado");
        var pedidos = 0;
        vm.DashboardRequested += (_, _) => pedidos++;

        await vm.OpenSelectedCommand.ExecuteAsync(null);

        Assert.Equal(0, pedidos);
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public async Task Projeto_de_motor_sem_suporte_aparece_com_o_aviso_e_nao_abre()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Segunda VN", engineId: "kirikiri");
        var vm = Create(temp);

        await vm.OpenAsync(root);

        var item = vm.Selected!;
        Assert.True(item.IsUnsupported);
        Assert.False(item.IsSupported);
        Assert.Equal("KiriKiri/KAG · XP3", item.EngineLabel);
        Assert.Equal("Sem suporte", item.RowStatusLabel);
        Assert.True(item.HasStatusWarning);
        Assert.Equal("A pasta foi reconhecida como KiriKiri/KAG. Ele ainda não pode ser traduzido por aqui.", item.UnsupportedDetail);
        Assert.False(vm.OpenSelectedCommand.CanExecute(null));
        Assert.False(vm.OpenSelectedInEditorCommand.CanExecute(null));
    }

    [Fact]
    public async Task Projeto_indisponivel_marca_a_linha_e_so_deixa_remover()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN");
        var vm = Create(temp);
        await vm.OpenAsync(root);
        Directory.Delete(root, recursive: true);
        await vm.LoadAsync();

        var item = vm.Selected!;
        Assert.True(item.HasProblem);
        Assert.Equal("Indisponível", item.RowStatusLabel);
        Assert.True(item.HasStatusWarning);
        Assert.False(item.IsSupported);
        Assert.False(item.IsUnsupported);
        Assert.False(vm.OpenSelectedCommand.CanExecute(null));
        Assert.True(vm.RemoveSelectedCommand.CanExecute(null));
    }

    [Fact]
    public async Task Projeto_disponivel_mostra_a_porcentagem_na_linha()
    {
        using var temp = new TempFolder();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN", lines: Lines("yst00001", TranslationStatus.Approved, TranslationStatus.Untranslated));
        var vm = Create(temp);

        await vm.OpenAsync(root);

        Assert.Equal("50%", vm.Selected!.RowStatusLabel);
        Assert.False(vm.Selected.HasStatusWarning);
    }

    [Fact]
    public async Task Rodape_diz_quantos_projetos_sem_citar_pasta()
    {
        using var temp = new TempFolder();
        var parent = temp.SubFolder("Traducoes");
        var a = StartFixtures.Create(Path.Combine(parent, "a"), "A");
        var b = StartFixtures.Create(Path.Combine(parent, "b"), "B");
        var vm = Create(temp);

        await vm.OpenAsync(a);
        await vm.OpenAsync(b);

        Assert.Equal(parent, vm.DefaultFolder);
        Assert.Equal("2 projetos", vm.RecentsSummary);
    }

    [Fact]
    public async Task Rodape_sem_projetos_nao_cita_pasta()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        await vm.LoadAsync();

        Assert.Null(vm.DefaultFolder);
        Assert.Equal("Nenhum projeto", vm.RecentsSummary);
    }

    [Fact]
    public async Task Lista_de_recentes_ilegivel_avisa_com_o_caminho_do_arquivo()
    {
        using var temp = new TempFolder();
        var config = new RecentProjectsFile(temp.SubFolder("config"));
        File.WriteAllText(config.Path, "{ isto não é json");
        var vm = new StartViewModel(config, new FakeFolderPicker(null), new FixedClock(Now));

        await vm.LoadAsync();

        Assert.Empty(vm.Recents);
        Assert.Contains(config.Path, vm.RecentsProblem);
        Assert.True(vm.HasRecentsProblem);
    }

    [Fact]
    public async Task Aviso_da_lista_ilegivel_some_quando_um_projeto_a_substitui()
    {
        using var temp = new TempFolder();
        var config = new RecentProjectsFile(temp.SubFolder("config"));
        File.WriteAllText(config.Path, "{ isto não é json");
        var vm = new StartViewModel(config, new FakeFolderPicker(null), new FixedClock(Now));
        await vm.LoadAsync();
        var root = StartFixtures.Create(temp.SubFolder("vn"), "Minha VN");

        await vm.OpenAsync(root);

        Assert.Null(vm.RecentsProblem);
        Assert.False(vm.HasRecentsProblem);
        Assert.Single(vm.Recents);
    }

    [Fact]
    public async Task Lista_legivel_nao_tem_aviso()
    {
        using var temp = new TempFolder();
        var vm = Create(temp);

        await vm.LoadAsync();

        Assert.Null(vm.RecentsProblem);
    }
}
