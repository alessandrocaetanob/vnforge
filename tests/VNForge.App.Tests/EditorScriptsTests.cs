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

        Assert.Equal(["Todas 4", "Em japonês 2", "Para revisar 1", "Com aviso 2", "Com dúvida 0", "Não traduzir 0", "Soa estranho 0"], rotulos);
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
    public async Task O_script_em_traducao_ganha_o_icone_girando_e_so_ele()
    {
        var vm = await Carregado();

        vm.RunningScriptKey = "yst00014";

        Assert.Equal(["yst00014"], vm.Scripts.Where(s => s.IsRunning).Select(s => s.Key));

        vm.RunningScriptKey = null;
        Assert.DoesNotContain(vm.Scripts, s => s.IsRunning);
    }

    [Fact]
    public async Task O_indicador_do_QA_da_linha_distingue_erro_e_aviso()
    {
        var vm = await Carregado();

        Assert.Equal(LineIssue.Error, vm.Lines[1].Issue);
        Assert.Equal(LineIssue.Warning, vm.Lines[2].Issue);
        Assert.Equal(LineIssue.None, vm.Lines[0].Issue);
    }
}
