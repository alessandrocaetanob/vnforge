using VNForge.App.ViewModels;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

/// <summary>
/// A lista de cenas do Editor por capítulo: criar um capítulo, mover cenas entre capítulos e voltar ao agrupamento automático gravam o
/// <c>group</c> no <c>scripts.json</c> (o mesmo da CLI) e o Painel mostra os totais novos sem reler o projeto.
/// </summary>
public class EditorChaptersTests
{
    private static readonly string[] Keys = ["yst00001", "yst00002", "yst00003", "yst00004", "yst00005", "yst00006"];

    private static readonly TranslationStatus[] Statuses =
    [
        TranslationStatus.Approved, TranslationStatus.Machine, TranslationStatus.Untranslated,
        TranslationStatus.Machine, TranslationStatus.Approved, TranslationStatus.Untranslated,
    ];

    /// <summary>Capítulo 1 com as cenas 1 a 3, Capítulo 2 com 4 e 5, e a 6 em "Menus e nomes" (pasta sem número).</summary>
    private static ScriptNames InitialNames() => ScriptNames.Empty.WithDetected(new Dictionary<string, DetectedScript>
    {
        ["yst00001"] = new("manha", "userscript/01_cap1"),
        ["yst00002"] = new("tarde", "userscript/01_cap1"),
        ["yst00003"] = new("noite", "userscript/01_cap1"),
        ["yst00004"] = new("sala", "userscript/02_cap2"),
        ["yst00005"] = new("rua", "userscript/02_cap2"),
        ["yst00006"] = new("menu", "userscript/system"),
    });

    private sealed class Opened(TempFolder temp, string root) : IDisposable
    {
        public string Root { get; } = root;

        public EditorViewModel Editor { get; set; } = null!;

        public DashboardViewModel Dashboard { get; set; } = null!;

        public int DashboardLoads { get; set; }

        public ScriptNames OnDisk => ProjectFolder.Open(Root).LoadScriptNames();

        public ScriptItemViewModel Scene(string key) => Editor.Scripts.Single(s => s.Key == key);

        public string[] ChapterKeys(string name) => [.. Editor.Chapters.Single(c => c.Name == name).Scripts.Select(s => s.Key)];

        public void Dispose() => temp.Dispose();
    }

    private static async Task<Opened> Open(Func<string, IReadOnlyList<string>, string?, ScriptGroupChange>? grouper = null)
    {
        var temp = new TempFolder();
        var folder = ProjectFolder.Create(temp.SubFolder("vn"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        for (var i = 0; i < Keys.Length; i++)
        {
            var status = Statuses[i];
            folder.SaveText(Keys[i], [new TextUnit($"{Keys[i]}:000001", TextUnitKind.Dialogue, null, "原文", status == TranslationStatus.Untranslated ? null : "tradução", status)]);
        }

        folder.SaveScriptNames(InitialNames());
        var summary = Summary("Minha VN", folder.Root);
        var opened = new Opened(temp, folder.Root);
        opened.Editor = new EditorViewModel(postToUi: a => a()) { Grouper = grouper };
        opened.Editor.Project = summary;
        await opened.Editor.Loading;

        opened.Dashboard = new DashboardViewModel(root =>
        {
            opened.DashboardLoads++;
            return ProjectDashboard.Read(ProjectFolder.Open(root));
        }) { Project = summary };
        await opened.Dashboard.ReloadAsync();
        opened.Editor.ChaptersChanged += (_, change) => opened.Dashboard.ApplyGrouping(change);
        return opened;
    }

    [Fact]
    public async Task A_lista_de_cenas_vem_agrupada_por_capitulo_na_ordem_da_historia_com_menus_e_nomes_no_fim()
    {
        using var open = await Open();

        Assert.Equal(["Capítulo 1", "Capítulo 2", ScriptGrouping.ExtraName], open.Editor.Chapters.Select(c => c.Name));
        Assert.Equal([1, 2, null], open.Editor.Chapters.Select(c => c.Number));
        Assert.Equal(["yst00001", "yst00002", "yst00003"], open.ChapterKeys("Capítulo 1"));
        Assert.Equal(Keys, open.Editor.SceneList.Select(s => s.Key));
        Assert.Equal(["3", "2", "1"], open.Editor.Chapters.Select(c => c.CountText));
    }

    [Fact]
    public async Task Mover_uma_cena_para_outro_capitulo_grava_o_group_e_o_Painel_mostra_os_totais_novos_sem_reler()
    {
        using var open = await Open();
        var loads = open.DashboardLoads;
        open.Editor.SelectedScript = open.Scene("yst00002");

        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.Name == "Capítulo 2"));

        Assert.Equal("Capítulo 2", open.OnDisk.Group("yst00002"));
        Assert.Null(open.OnDisk.Group("yst00001"));
        Assert.Equal(["yst00001", "yst00003"], open.ChapterKeys("Capítulo 1"));
        Assert.Equal(["yst00002", "yst00004", "yst00005"], open.ChapterKeys("Capítulo 2"));

        // Capítulo 1: 1 aprovada + 1 em japonês. Capítulo 2: 1 aprovada + 2 da IA.
        var one = open.Dashboard.Chapters.Single(c => c.Name == "Capítulo 1");
        var two = open.Dashboard.Chapters.Single(c => c.Name == "Capítulo 2");
        Assert.Equal((1d, 0d, 2d), (one.Ready, one.ToReview, one.Total));
        Assert.Equal((1d, 2d, 3d), (two.Ready, two.ToReview, two.Total));
        Assert.Equal(loads, open.DashboardLoads);
    }

    [Fact]
    public async Task A_cena_aberta_e_as_marcadas_continuam_selecionadas_depois_de_mover()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00002");
        open.Editor.ChosenScripts.Add(open.Scene("yst00002"));
        open.Editor.ChosenScripts.Add(open.Scene("yst00003"));

        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.Name == "Capítulo 2"));

        Assert.Equal("yst00002", open.Editor.SelectedScript?.Key);
        Assert.Equal(["yst00002", "yst00003"], open.Editor.ChosenScripts.Select(s => s.Key).Order());
        Assert.Equal(["yst00002", "yst00003", "yst00004", "yst00005"], open.ChapterKeys("Capítulo 2"));
    }

    [Fact]
    public async Task Mover_as_cenas_marcadas_leva_todas_e_ignora_a_aberta()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00001");
        open.Editor.ChosenScripts.Add(open.Scene("yst00002"));
        open.Editor.ChosenScripts.Add(open.Scene("yst00003"));

        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.Name == "Capítulo 2"));

        Assert.Equal(["yst00001"], open.ChapterKeys("Capítulo 1"));
        Assert.Equal(["yst00002", "yst00003", "yst00004", "yst00005"], open.ChapterKeys("Capítulo 2"));
    }

    [Fact]
    public async Task Novo_capitulo_com_duas_cenas_marcadas_nasce_na_posicao_da_primeira_cena_na_ordem_da_historia()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00002");
        open.Editor.ChosenScripts.Add(open.Scene("yst00002"));
        open.Editor.ChosenScripts.Add(open.Scene("yst00005"));

        open.Editor.BeginNewChapterCommand.Execute(null);
        Assert.True(open.Editor.IsNamingChapter);
        open.Editor.NewChapterName = "  Lembrança ";
        await open.Editor.CommitNewChapterCommand.ExecuteAsync(null);

        Assert.False(open.Editor.IsNamingChapter);
        Assert.Equal("Lembrança", open.OnDisk.Group("yst00002"));
        Assert.Equal("Lembrança", open.OnDisk.Group("yst00005"));

        // A primeira cena (yst00002) vem depois da yst00001 e antes da yst00004: o capítulo novo fica entre os dois.
        Assert.Equal(["Capítulo 1", "Lembrança", "Capítulo 2", ScriptGrouping.ExtraName], open.Editor.Chapters.Select(c => c.Name));
        Assert.Equal([1, 2, 3, null], open.Editor.Chapters.Select(c => c.Number));
        Assert.Equal(["yst00002", "yst00005"], open.ChapterKeys("Lembrança"));
        Assert.Equal(["Capítulo 1", "Lembrança", "Capítulo 2", ScriptGrouping.ExtraName], open.Dashboard.Chapters.Select(c => c.Name));
        Assert.Equal(open.Editor.SceneList.Select(s => s.Key), open.Editor.Chapters.SelectMany(c => c.Scripts).Select(s => s.Key));
    }

    [Fact]
    public async Task Novo_capitulo_sem_cenas_marcadas_leva_a_cena_aberta()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00003");

        open.Editor.BeginNewChapterCommand.Execute(null);
        open.Editor.NewChapterName = "Noite";
        await open.Editor.CommitNewChapterCommand.ExecuteAsync(null);

        Assert.Equal(["yst00003"], open.ChapterKeys("Noite"));
        Assert.Equal(["yst00001", "yst00002"], open.ChapterKeys("Capítulo 1"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Novo_capitulo_sem_nome_pede_um_nome_sem_gravar_e_mantem_o_campo_aberto(string name)
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00003");
        open.Editor.BeginNewChapterCommand.Execute(null);
        open.Editor.NewChapterName = name;

        await open.Editor.CommitNewChapterCommand.ExecuteAsync(null);

        Assert.True(open.Editor.IsNamingChapter);
        Assert.Equal("Escreva um nome para o capítulo.", open.Editor.ChapterMessage);
        Assert.Null(open.OnDisk.Group("yst00003"));
    }

    [Fact]
    public async Task Cancelar_o_novo_capitulo_fecha_o_campo_sem_gravar()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00003");
        open.Editor.BeginNewChapterCommand.Execute(null);
        open.Editor.NewChapterName = "Noite";

        open.Editor.CancelNewChapterCommand.Execute(null);

        Assert.False(open.Editor.IsNamingChapter);
        Assert.Null(open.Editor.ChapterMessage);
        Assert.Null(open.OnDisk.Group("yst00003"));
    }

    [Fact]
    public async Task Voltar_ao_agrupamento_automatico_apaga_o_group_e_a_cena_volta_ao_capitulo_da_pasta()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00002");
        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.Name == "Capítulo 2"));
        Assert.Equal("Capítulo 2", open.OnDisk.Group("yst00002"));

        await open.Editor.ResetChaptersCommand.ExecuteAsync(null);

        Assert.Null(open.OnDisk.Group("yst00002"));
        Assert.Equal(["yst00001", "yst00002", "yst00003"], open.ChapterKeys("Capítulo 1"));
        Assert.Equal(["Capítulo 1", "Capítulo 2", ScriptGrouping.ExtraName], open.Dashboard.Chapters.Select(c => c.Name));
    }

    [Fact]
    public async Task Capitulo_que_perde_a_ultima_cena_some_da_lista_e_do_Painel()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00004");
        open.Editor.ChosenScripts.Add(open.Scene("yst00004"));
        open.Editor.ChosenScripts.Add(open.Scene("yst00005"));

        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.Name == "Capítulo 1"));

        Assert.Equal(["Capítulo 1", ScriptGrouping.ExtraName], open.Editor.Chapters.Select(c => c.Name));
        Assert.Equal(["Capítulo 1", ScriptGrouping.ExtraName], open.Dashboard.Chapters.Select(c => c.Name));
        Assert.Equal(Keys, open.Editor.SceneList.Select(s => s.Key));
    }

    [Fact]
    public async Task Mandar_a_cena_para_Menus_e_nomes_tira_ela_da_historia()
    {
        using var open = await Open();
        open.Editor.SelectedScript = open.Scene("yst00003");

        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.IsExtra));

        Assert.Equal(["yst00003", "yst00006"], open.ChapterKeys(ScriptGrouping.ExtraName));
        Assert.Equal(["Capítulo 1", "Capítulo 2", ScriptGrouping.ExtraName], open.Editor.Chapters.Select(c => c.Name));
    }

    [Fact]
    public async Task Falha_ao_gravar_mostra_a_mensagem_e_nao_mexe_na_lista()
    {
        using var open = await Open((_, _, _) => throw new ScriptGroupingException("Não há script com a chave 'yst00003'."));
        open.Editor.SelectedScript = open.Scene("yst00003");

        await open.Editor.MoveToChapterCommand.ExecuteAsync(open.Editor.Chapters.Single(c => c.Name == "Capítulo 2"));

        Assert.Equal("Não há script com a chave 'yst00003'.", open.Editor.ChapterMessage);
        Assert.Equal(["yst00001", "yst00002", "yst00003"], open.ChapterKeys("Capítulo 1"));
    }

    [Fact]
    public async Task Pasta_somente_leitura_mostra_o_erro_em_vez_de_derrubar_o_comando()
    {
        using var open = await Open((_, _, _) => throw new UnauthorizedAccessException("Acesso negado ao scripts.json."));
        open.Editor.SelectedScript = open.Scene("yst00003");

        await open.Editor.ResetChaptersCommand.ExecuteAsync(null);

        Assert.Equal("Acesso negado ao scripts.json.", open.Editor.ChapterMessage);
    }

    [Fact]
    public async Task Fechar_um_capitulo_esconde_as_cenas_dele_menos_a_primeira_que_leva_o_cabecalho()
    {
        using var open = await Open();
        var chapter = open.Editor.Chapters.Single(c => c.Name == "Capítulo 1");

        Assert.All(chapter.Scripts, s => Assert.True(s.IsListed));
        Assert.Equal(["yst00001", "yst00004", "yst00006"], open.Editor.SceneList.Where(s => s.IsGroupStart).Select(s => s.Key));

        chapter.IsExpanded = false;

        Assert.Equal([true, false, false], chapter.Scripts.Select(s => s.IsListed));
        Assert.Equal(["yst00001", "yst00004", "yst00006"], open.Editor.SceneList.Where(s => s.IsGroupStart).Select(s => s.Key));
    }

    [Fact]
    public async Task O_filtro_leva_o_cabecalho_para_a_primeira_cena_que_passa_e_tira_o_dos_capitulos_vazios()
    {
        using var open = await Open();

        open.Editor.ScriptFilter = "yst00003";

        Assert.Equal(["yst00003"], open.Editor.SceneList.Where(s => s.IsGroupStart).Select(s => s.Key));
        Assert.Equal(["yst00003"], open.Editor.SceneList.Where(s => s.IsListed).Select(s => s.Key));

        open.Editor.ScriptFilter = string.Empty;

        Assert.Equal(3, open.Editor.SceneList.Count(s => s.IsGroupStart));
    }

    [Fact]
    public async Task Fechar_o_capitulo_nao_tira_as_cenas_dele_do_pedido_de_traducao()
    {
        using var open = await Open();
        var pedidos = new List<IReadOnlyList<string>>();
        open.Editor.TranslateRequested += (_, keys) => pedidos.Add(keys);
        open.Editor.SelectedScript = open.Scene("yst00001");
        open.Editor.ChosenScripts.Add(open.Scene("yst00002"));
        open.Editor.ChosenScripts.Add(open.Scene("yst00003"));
        open.Editor.Chapters.Single(c => c.Name == "Capítulo 1").IsExpanded = false;

        open.Editor.TranslatePendingCommand.Execute(null);

        Assert.Equal(["yst00002", "yst00003"], Assert.Single(pedidos));
    }

    [Fact]
    public async Task Sem_projeto_a_lista_nao_tem_capitulos_e_os_comandos_nao_fazem_nada()
    {
        var vm = new EditorViewModel(postToUi: a => a());

        await vm.ResetChaptersCommand.ExecuteAsync(null);
        vm.BeginNewChapterCommand.Execute(null);

        Assert.Empty(vm.Chapters);
        Assert.Empty(vm.SceneList);
        Assert.False(vm.IsNamingChapter);
    }

    [Fact]
    public async Task Recarregar_o_projeto_mantem_os_capitulos_fechados()
    {
        using var open = await Open();
        open.Editor.Chapters.Single(c => c.Name == "Capítulo 2").IsExpanded = false;

        open.Editor.Reload();
        await open.Editor.Loading;

        Assert.False(open.Editor.Chapters.Single(c => c.Name == "Capítulo 2").IsExpanded);
        Assert.True(open.Editor.Chapters.Single(c => c.Name == "Capítulo 1").IsExpanded);
    }
}
