using VNForge.App.Services;
using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

/// <summary>Cenas sem linhas (#381): o Editor e o Painel as escondem por padrão, contam quantas e, mostradas, aparecem como prontas.</summary>
public class EmptyScenesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Capítulo 1 com a cena 1 (uma linha da IA); "Vazios" com as cenas 2 e 3, sem linhas; Capítulo 3 com a cena 4 (uma linha em japonês).</summary>
    private static ScriptNames Names() => ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
    {
        ["yst00001"] = new("manha", "userscript/01_cap1"),
        ["yst00002"] = new("vazia1", "userscript/02_vazios"),
        ["yst00003"] = new("vazia2", "userscript/02_vazios"),
        ["yst00004"] = new("noite", "userscript/03_cap3"),
    });

    private static ProjectFolder CreateProject(TempFolder temp)
    {
        var folder = ProjectFolder.Create(temp.SubFolder("vn"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        folder.SaveText("yst00001", [new TextUnit("yst00001:000001", TextUnitKind.Dialogue, null, "原文", "tradução", TranslationStatus.Machine)]);
        folder.SaveText("yst00002", []);
        folder.SaveText("yst00003", []);
        folder.SaveText("yst00004", [new TextUnit("yst00004:000001", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        folder.SaveScriptNames(Names());
        return folder;
    }

    private static async Task<EditorViewModel> OpenEditor(ProjectFolder folder)
    {
        var editor = new EditorViewModel(postToUi: a => a()) { Project = Summary("Minha VN", folder.Root) };
        await editor.Loading;
        return editor;
    }

    private static async Task<DashboardViewModel> OpenDashboard(ProjectFolder folder, string? lastEdited = null, Action? onLoad = null)
    {
        var summary = Summary("Minha VN", folder.Root) with { LastEditedScript = lastEdited };
        var vm = new DashboardViewModel(root =>
        {
            onLoad?.Invoke();
            return ProjectDashboard.Read(ProjectFolder.Open(root));
        }, new FixedClock(Now)) { Project = summary };
        await vm.ReloadAsync();
        return vm;
    }

    [Fact]
    public async Task No_Editor_as_cenas_vazias_vem_escondidas_com_a_contagem()
    {
        using var temp = new TempFolder();
        var editor = await OpenEditor(CreateProject(temp));

        Assert.True(editor.HideEmptyScenes);
        Assert.Equal(2, editor.HiddenEmptyScenes);
        Assert.Equal("2 cenas vazias escondidas", editor.HiddenEmptyScenesText);
        Assert.True(editor.HasHiddenEmptyScenes);
        Assert.False(editor.Scripts.Single(s => s.Key == "yst00002").IsListed);
        Assert.False(editor.Scripts.Single(s => s.Key == "yst00003").IsListed);
        Assert.True(editor.Scripts.Single(s => s.Key == "yst00001").IsListed);
        Assert.True(editor.Scripts.Single(s => s.Key == "yst00004").IsListed);
        Assert.All(editor.Scripts.Where(s => s.Key is "yst00002" or "yst00003"), s => Assert.False(s.IsGroupStart));
    }

    [Fact]
    public async Task No_Editor_com_a_opcao_desligada_a_cena_vazia_aparece_marcada_como_pronta()
    {
        using var temp = new TempFolder();
        var editor = await OpenEditor(CreateProject(temp));

        editor.HideEmptyScenes = false;

        var empty = editor.Scripts.Single(s => s.Key == "yst00002");
        Assert.True(empty.IsListed);
        Assert.True(empty.IsEmpty);
        Assert.True(empty.IsComplete);
        Assert.Equal(100, empty.CompletedPercent);
        Assert.True(empty.IsGroupStart);
        Assert.Equal(0, editor.HiddenEmptyScenes);
        Assert.Equal(string.Empty, editor.HiddenEmptyScenesText);
        Assert.False(editor.HasHiddenEmptyScenes);
    }

    [Fact]
    public async Task No_Editor_mostrar_e_esconder_de_novo_volta_ao_estado_anterior()
    {
        using var temp = new TempFolder();
        var editor = await OpenEditor(CreateProject(temp));

        editor.ToggleEmptyScenesCommand.Execute(null);
        Assert.False(editor.HideEmptyScenes);
        Assert.True(editor.Scripts.Single(s => s.Key == "yst00003").IsListed);

        editor.ToggleEmptyScenesCommand.Execute(null);
        Assert.True(editor.HideEmptyScenes);
        Assert.False(editor.Scripts.Single(s => s.Key == "yst00003").IsListed);
    }

    [Fact]
    public async Task No_Editor_a_cena_vazia_aberta_continua_selecionada_mesmo_escondida()
    {
        using var temp = new TempFolder();
        var editor = await OpenEditor(CreateProject(temp));
        editor.HideEmptyScenes = false;
        var empty = editor.Scripts.Single(s => s.Key == "yst00002");
        editor.SelectedScript = empty;

        editor.HideEmptyScenes = true;

        Assert.Same(empty, editor.SelectedScript);
        Assert.False(empty.IsListed);
    }

    [Fact]
    public async Task No_Editor_o_filtro_de_cenas_nao_traz_de_volta_a_cena_vazia_escondida()
    {
        using var temp = new TempFolder();
        var editor = await OpenEditor(CreateProject(temp));

        editor.ScriptFilter = "vazia";

        Assert.All(editor.Scripts.Where(s => s.Key is "yst00002" or "yst00003"), s => Assert.False(s.IsListed));
    }

    [Fact]
    public async Task No_Painel_o_capitulo_sem_linhas_vem_escondido_com_a_contagem_de_cenas()
    {
        using var temp = new TempFolder();
        var vm = await OpenDashboard(CreateProject(temp));

        Assert.True(vm.HideEmptyScenes);
        Assert.Equal(["Capítulo 1", "Capítulo 3"], vm.Chapters.Select(c => c.Name));
        Assert.Equal("2 cenas vazias escondidas", vm.HiddenEmptyScenesText);
        Assert.True(vm.HasHiddenEmptyScenes);
    }

    [Fact]
    public async Task No_Painel_mostrado_o_capitulo_vazio_nao_e_o_voce_esta_aqui_nem_o_proximo_passo()
    {
        using var temp = new TempFolder();
        var vm = await OpenDashboard(CreateProject(temp), lastEdited: "yst00002");

        vm.HideEmptyScenes = false;

        Assert.Equal(["Capítulo 1", "Vazios", "Capítulo 3"], vm.Chapters.Select(c => c.Name));
        var empty = vm.Chapters.Single(c => c.Name == "Vazios");
        Assert.True(empty.IsEmpty);
        Assert.False(empty.IsHere);
        Assert.Equal(string.Empty, empty.Note);
        Assert.Equal(string.Empty, vm.HiddenEmptyScenesText);
        Assert.Equal("Revisar o capítulo 1", vm.NextStepLabel);
        Assert.Equal("yst00001", vm.NextStepTarget?.ScriptKey);
    }

    [Fact]
    public async Task No_Painel_com_o_capitulo_aberto_vazio_o_proximo_passo_vai_para_o_primeiro_com_trabalho()
    {
        using var temp = new TempFolder();
        var folder = CreateProject(temp);
        folder.SaveText("yst00001", []);
        var vm = await OpenDashboard(folder, lastEdited: "yst00001");

        vm.HideEmptyScenes = false;

        Assert.Equal("Traduzir o capítulo 3", vm.NextStepLabel);
        Assert.Equal("yst00004", vm.NextStepTarget?.ScriptKey);
        Assert.All(vm.Chapters.Where(c => c.IsEmpty), c => Assert.False(c.IsHere));
    }

    [Fact]
    public async Task No_Painel_alternar_a_opcao_reaplica_os_capitulos_sem_reler_o_projeto()
    {
        using var temp = new TempFolder();
        var loads = 0;
        var vm = await OpenDashboard(CreateProject(temp), onLoad: () => loads++);

        vm.ToggleEmptyScenesCommand.Execute(null);
        Assert.False(vm.HideEmptyScenes);
        Assert.Equal(3, vm.Chapters.Count);
        vm.ToggleEmptyScenesCommand.Execute(null);
        Assert.Equal(2, vm.Chapters.Count);
        Assert.Equal(1, loads);
    }

    private static MainWindowViewModel Window(TempFolder temp, string config)
    {
        var settings = new SettingsViewModel(new MemoryApiKeyStore(), new AppSettingsFile(config), _ => null);
        return new MainWindowViewModel(new StartViewModel(new RecentProjectsFile(temp.SubFolder("recentes")), new FakeFolderPicker(temp.Path), new FixedClock(Now)), settings: settings);
    }

    [Fact]
    public void A_escolha_vale_para_o_Painel_e_o_Editor_e_fica_no_app_json()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        var main = Window(temp, config);
        Assert.True(main.Dashboard.HideEmptyScenes);
        Assert.True(main.Editor.HideEmptyScenes);

        main.Editor.HideEmptyScenes = false;

        Assert.False(main.Dashboard.HideEmptyScenes);
        Assert.False(new AppSettingsFile(config).Load().HideEmptyScenes);

        main.Dashboard.HideEmptyScenes = true;

        Assert.True(main.Editor.HideEmptyScenes);
        Assert.True(new AppSettingsFile(config).Load().HideEmptyScenes);
    }

    [Fact]
    public void A_escolha_gravada_vale_ao_abrir_o_app_de_novo()
    {
        using var temp = new TempFolder();
        var config = temp.SubFolder("config");
        Window(temp, config).Dashboard.HideEmptyScenes = false;

        var reopened = Window(temp, config);

        Assert.False(reopened.Dashboard.HideEmptyScenes);
        Assert.False(reopened.Editor.HideEmptyScenes);
    }

    [Fact]
    public void O_app_json_sem_a_chave_esconde_as_cenas_vazias_e_o_padrao_nao_grava_a_chave()
    {
        using var temp = new TempFolder();
        var file = new AppSettingsFile(temp.SubFolder("config"));

        file.Save(AppSettings.Default);

        Assert.True(file.Load().HideEmptyScenes);
        Assert.DoesNotContain("hideEmptyScenes", File.ReadAllText(file.Path), StringComparison.Ordinal);
    }
}
