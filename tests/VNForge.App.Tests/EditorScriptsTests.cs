using VNForge.App.Controls;
using VNForge.App.ViewModels;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;
using static VNForge.App.Tests.EditorFixtures;

namespace VNForge.App.Tests;

/// <summary>Cabeçalho, lista de scripts e grade do Editor no nível do design v2 (artboards 1 e 2).</summary>
public class EditorScriptsTests
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["yst00013"] = "Capítulo 1 — Manhã de chuva",
        ["yst00014"] = "Capítulo 1 — Sala de aula",
    };

    private static ProjectLines TresScripts() => Lines(
        new QaReport(
            [
                new QaIssue("regra", QaSeverity.Error, "yst00013:000002", "yst00013", "marcador"),
                new QaIssue("regra", QaSeverity.Warning, "yst00013:000003", "yst00013", "espaço"),
            ],
            [],
            ["regra"]),
        ("yst00012", [Unit("yst00012", 1, null, TranslationStatus.Approved)]),
        ("yst00013",
        [
            Unit("yst00013", 1, null, TranslationStatus.Approved),
            Unit("yst00013", 2, "美咲", TranslationStatus.Machine),
            Unit("yst00013", 3, "健太", TranslationStatus.Untranslated),
            Unit("yst00013", 4, "美咲", TranslationStatus.Untranslated),
        ]),
        ("yst00014", [Unit("yst00014", 1, null, TranslationStatus.Untranslated)]));

    private static async Task<EditorViewModel> Carregado(Func<string, string?>? nomes = null, Func<string, EditorContext>? contexto = null)
    {
        var vm = new EditorViewModel(_ => TresScripts(), scriptLabel: nomes ?? (key => Names.GetValueOrDefault(key)), contextLoader: contexto);
        vm.Project = Summary();
        await vm.Loading;
        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00013");
        return vm;
    }

    [Fact]
    public async Task Cabecalho_mostra_o_capitulo_e_a_posicao_da_cena_e_o_nome_dela_como_titulo()
    {
        var vm = await Carregado();

        Assert.Equal("Capítulo 1 · cena 2 de 3", vm.Breadcrumb);
        Assert.Equal("Capítulo 1 — Manhã de chuva", vm.ScriptTitle);
    }

    [Fact]
    public async Task Sem_nome_do_script_o_titulo_vira_Cena_N_e_nunca_a_chave()
    {
        var vm = await Carregado(nomes: _ => null);

        Assert.Equal("Cena 2", vm.ScriptTitle);
        Assert.Equal("Capítulo 1 · cena 2 de 3", vm.Breadcrumb);
        Assert.Null(vm.SelectedScript!.Label);
        Assert.DoesNotContain("yst", vm.ScriptTitle + vm.Breadcrumb, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_nome_da_cena_vem_do_scripts_json_quando_ninguem_o_da_e_o_do_usuario_vale_mais()
    {
        var nomes = ScriptNames.Empty
            .WithDetected(new Dictionary<string, string?> { ["yst00012"] = "prologo", ["yst00013"] = "manha" })
            .Rename("yst00013", "Manhã de chuva");
        var vm = new EditorViewModel(_ => TresScripts()) { NamesLoader = _ => nomes };
        vm.Project = Summary();
        await vm.Loading;
        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00013");

        Assert.Equal("Manhã de chuva", vm.ScriptTitle);
        Assert.Equal("prologo", vm.Scripts.Single(s => s.Key == "yst00012").Name);
        Assert.Equal("Cena 3", vm.Scripts.Single(s => s.Key == "yst00014").Name);
    }

    [Fact]
    public async Task O_filtro_de_cenas_casa_Cena_N_numa_cena_sem_nome()
    {
        var vm = await Carregado(nomes: _ => null);

        vm.ScriptFilter = "Cena 2";

        Assert.Equal(["yst00013"], vm.Scripts.Where(s => s.IsShown).Select(s => s.Key));
    }

    [Fact]
    public async Task Quando_o_scripts_json_chega_depois_o_Cena_N_vira_o_nome_real_sem_mexer_na_posicao()
    {
        var nomes = ScriptNames.Empty;
        var vm = new EditorViewModel(_ => TresScripts()) { NamesLoader = _ => nomes };
        vm.Project = Summary();
        await vm.Loading;
        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00013");
        Assert.Equal("Cena 2", vm.ScriptTitle);

        nomes = ScriptNames.Empty.Rename("yst00013", "Manhã de chuva");
        await vm.RefreshChaptersAsync();

        Assert.Equal("Manhã de chuva", vm.ScriptTitle);
        Assert.Equal(2, vm.SelectedScript!.Position);
        Assert.Equal("Manhã de chuva", vm.Scripts.Single(s => s.Key == "yst00013").Name);
    }

    [Fact]
    public async Task Capitulo_sem_numero_da_a_Cena_3_de_5_na_sobrancelha()
    {
        string[] chaves = ["yst00001", "yst00002", "yst00003", "yst00004", "yst00005", "yst00006", "yst00007"];
        var nomes = chaves.Skip(2).Aggregate(ScriptNames.Empty, (atual, chave) => atual.WithGroup(chave, ScriptGrouping.ExtraName));
        var vm = new EditorViewModel(_ => Lines([.. chaves.Select(c => (c, new[] { Unit(c, 1, null, TranslationStatus.Approved) }))])) { NamesLoader = _ => nomes };
        vm.Project = Summary();
        await vm.Loading;

        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00005");

        Assert.Null(vm.SelectedScript.Chapter!.Number);
        Assert.Equal("Cena 3 de 5", vm.Breadcrumb);
        Assert.Equal("Cena 5", vm.ScriptTitle);
    }

    [Fact]
    public async Task Os_chips_Todas_Em_japones_Para_revisar_e_Com_aviso_trazem_a_contagem()
    {
        var vm = await Carregado();

        var rotulos = vm.Filters.Select(f => $"{f.Label} {f.CountLabel}").ToArray();

        Assert.Equal(["Todas 4", "Em japonês 2", "Para revisar 1", "Com aviso 2", "Não traduzir 0"], rotulos);
    }

    [Fact]
    public async Task A_lista_de_scripts_traz_o_nome_do_capitulo_e_o_percentual()
    {
        var vm = await Carregado();

        var atual = vm.Scripts.Single(s => s.Key == "yst00013");
        Assert.Equal("Capítulo 1 — Manhã de chuva", atual.Label);
        Assert.Equal("25%", atual.PercentLabel);
        Assert.Null(vm.Scripts.Single(s => s.Key == "yst00012").Label);
    }

    [Fact]
    public async Task O_filtro_de_scripts_casa_a_chave_ou_o_nome_sem_diferenciar_maiusculas()
    {
        var vm = await Carregado();

        vm.ScriptFilter = "SALA";
        Assert.Equal(["yst00014"], vm.VisibleScripts.Select(s => s.Key));

        vm.ScriptFilter = "yst0001";
        Assert.Equal(3, vm.VisibleScripts.Count());

        vm.ScriptFilter = string.Empty;
        Assert.Equal(3, vm.VisibleScripts.Count());
    }

    [Fact]
    public async Task Filtrar_a_lista_nao_troca_o_script_aberto()
    {
        var vm = await Carregado();
        vm.SelectedLine = vm.Lines[1];

        vm.ScriptFilter = "sala";

        Assert.Equal("yst00013", vm.SelectedScript?.Key);
        Assert.Equal(vm.Lines[1], vm.SelectedLine);
    }

    [Fact]
    public async Task O_script_em_traducao_ganha_o_icone_girando_e_so_ele()
    {
        var vm = await Carregado();

        vm.RunningScriptKey = "yst00014";

        Assert.Equal(["yst00014"], vm.Scripts.Where(s => s.IsRunning).Select(s => s.Key));

        vm.RunningScriptKey = null;
        Assert.DoesNotContain(vm.Scripts, s => s.IsRunning);
    }

    [Fact]
    public async Task O_script_em_traducao_continua_marcado_depois_de_recarregar()
    {
        var vm = await Carregado();
        vm.RunningScriptKey = "yst00014";

        vm.Reload();
        await vm.Loading;

        Assert.True(vm.Scripts.Single(s => s.Key == "yst00014").IsRunning);
    }

    [Fact]
    public async Task Conferir_arquivo_pede_a_conferencia_do_script_aberto()
    {
        var vm = await Carregado();
        var pedidos = new List<string>();
        vm.CheckRequested += (_, key) => pedidos.Add(key);

        vm.CheckFileCommand.Execute(null);

        Assert.Equal(["yst00013"], pedidos);
    }

    [Fact]
    public async Task Traduzir_com_IA_abre_a_janela_mesmo_quando_o_script_nao_tem_pendentes()
    {
        var vm = await Carregado();
        var pedidos = new List<IReadOnlyList<string>>();
        vm.TranslateRequested += (_, keys) => pedidos.Add(keys);

        Assert.Equal(2, vm.PendingLines);
        Assert.True(vm.TranslatePendingCommand.CanExecute(null));
        vm.TranslatePendingCommand.Execute(null);
        Assert.Equal(["yst00013"], Assert.Single(pedidos));

        vm.SelectedScript = vm.Scripts.Single(s => s.Key == "yst00012");
        Assert.Equal(0, vm.PendingLines);
        Assert.True(vm.TranslatePendingCommand.CanExecute(null));
    }

    [Fact]
    public async Task Traduzir_com_IA_leva_todas_as_cenas_escolhidas_na_ordem_da_lista()
    {
        var vm = await Carregado();
        var pedidos = new List<IReadOnlyList<string>>();
        vm.TranslateRequested += (_, keys) => pedidos.Add(keys);

        vm.ChosenScripts.Add(vm.Scripts.Single(s => s.Key == "yst00014"));
        vm.ChosenScripts.Add(vm.Scripts.Single(s => s.Key == "yst00013"));
        vm.TranslatePendingCommand.Execute(null);

        Assert.Equal(["yst00013", "yst00014"], Assert.Single(pedidos));
    }

    [Fact]
    public async Task Cenas_escolhidas_escondidas_pelo_filtro_nao_entram_no_pedido()
    {
        var vm = await Carregado();
        var pedidos = new List<IReadOnlyList<string>>();
        vm.TranslateRequested += (_, keys) => pedidos.Add(keys);
        vm.ChosenScripts.Add(vm.Scripts.Single(s => s.Key == "yst00013"));
        vm.ChosenScripts.Add(vm.Scripts.Single(s => s.Key == "yst00014"));

        vm.ScriptFilter = "00013";
        vm.TranslatePendingCommand.Execute(null);

        Assert.Equal(["yst00013"], Assert.Single(pedidos));
    }

    [Fact]
    public async Task Com_o_filtro_escondendo_a_cena_aberta_vai_a_unica_escolhida_que_aparece()
    {
        var vm = await Carregado();
        var pedidos = new List<IReadOnlyList<string>>();
        vm.TranslateRequested += (_, keys) => pedidos.Add(keys);
        vm.ChosenScripts.Add(vm.Scripts.Single(s => s.Key == "yst00013"));
        vm.ChosenScripts.Add(vm.Scripts.Single(s => s.Key == "yst00014"));

        vm.ScriptFilter = "00014";
        vm.TranslatePendingCommand.Execute(null);

        Assert.Equal(["yst00014"], Assert.Single(pedidos));
    }

    [Fact]
    public async Task A_linha_da_grade_mostra_o_personagem_traduzido_e_os_textos_sem_marcadores()
    {
        var ctx = new EditorContext(
            new CharacterBook([new CharacterEntry("美咲", "Misaki", CharacterGender.Female, "informal")]),
            []);
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>
        {
            ["yst00013"] = [new TextUnit("yst00013:000000", TextUnitKind.Dialogue, "美咲", "「おはよう」\\n「うん」\\p", "Bom dia.\\nSim.\\p", TranslationStatus.Machine)],
        };
        var vm = new EditorViewModel(_ => ProjectLines.Create(texts), contextLoader: _ => ctx);
        vm.Project = Summary();
        await vm.Loading;

        var linha = vm.Lines.Single();

        Assert.Equal("Misaki", linha.SpeakerName);
        Assert.Equal("「おはよう」 「うん」", linha.SourcePlain);
        Assert.Equal("Bom dia. Sim.", linha.TranslationPlain);
    }

    [Fact]
    public async Task Sem_cadastro_do_personagem_a_grade_mostra_o_nome_original()
    {
        var vm = await Carregado();

        Assert.Equal("美咲", vm.Lines[1].SpeakerName);
        Assert.Null(vm.Lines[0].SpeakerName);
    }

    [Fact]
    public async Task O_indicador_do_QA_da_linha_distingue_erro_e_aviso()
    {
        var vm = await Carregado();

        Assert.Equal(LineIssue.Error, vm.Lines[1].Issue);
        Assert.Equal(LineIssue.Warning, vm.Lines[2].Issue);
        Assert.Equal(LineIssue.None, vm.Lines[0].Issue);
    }

    [Fact]
    public async Task O_rodape_da_grade_mostra_a_distribuicao_e_a_contagem_do_script()
    {
        var vm = await Carregado();

        Assert.Equal("4 linhas nesta cena", vm.LinesSummary);
        Assert.NotNull(vm.Distribution);
        Assert.Equal(25, vm.Distribution.Approved);
        Assert.Equal(25, vm.Distribution.Machine);
        Assert.Equal(50, vm.Distribution.Pending);
    }

    [Fact]
    public async Task Conferir_arquivo_abre_a_Conferencia_no_Editor_que_o_app_troca_depois_do_construtor()
    {
        using var temp = new TempFolder();
        var start = new StartViewModel(new RecentProjectsFile(temp.SubFolder("config")), new FakeFolderPicker(SampleProjects.Create(temp.SubFolder("vn"), "Minha VN", TranslationStatus.Machine)), new FixedClock(DateTimeOffset.UnixEpoch));
        var main = new MainWindowViewModel(start) { Editor = new EditorViewModel(postToUi: acao => acao()) };
        await main.Start.OpenFolderCommand.ExecuteAsync(null);
        await main.Editor.Loading;
        Assert.NotEqual(NavKey.Conference, main.SelectedNav!.Key);

        main.Editor.CheckFileCommand.Execute(null);

        Assert.Equal(NavKey.Conference, main.SelectedNav!.Key);
    }
}
