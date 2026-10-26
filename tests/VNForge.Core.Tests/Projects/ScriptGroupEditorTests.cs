using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ScriptGroupEditorTests
{
    private static string Key(int n) => $"yst{n:D5}";

    private static ProjectFolder ProjetoCom(string raiz, int scripts, params (int N, string Folder)[] pastas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        for (var n = 1; n <= scripts; n++)
        {
            projeto.SaveText(Key(n), [new TextUnit($"{Key(n)}:000001", TextUnitKind.Dialogue, null, "原文", null, TranslationStatus.Untranslated)]);
        }

        if (pastas.Length > 0)
        {
            projeto.SaveScriptNames(ScriptNames.Empty.WithDetected(pastas.ToDictionary(p => Key(p.N), p => new DetectedScript($"cena{p.N}", p.Folder), StringComparer.Ordinal)));
        }

        return projeto;
    }

    private static string[] KeysOf(ProjectFolder projeto, string grupo) =>
        [.. ProjectDashboard.Read(projeto).Chapters().Single(c => c.Name == grupo).ScriptKeys];

    [Fact]
    public void Agrupar_grava_o_grupo_no_scripts_json_e_o_painel_mostra_o_capitulo_novo()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 6);

        var mudanca = ScriptGroupEditor.Group(projeto, [Key(2), Key(3)], "  Manhã de chuva ");

        Assert.Equal([Key(2), Key(3)], mudanca.Keys);
        Assert.Equal("Manhã de chuva", mudanca.Group);
        var lido = ProjectFolder.Open(temp.Path).LoadScriptNames();
        Assert.Equal("Manhã de chuva", lido.Group(Key(2)));
        Assert.Equal("Manhã de chuva", lido.Group(Key(3)));
        Assert.Null(lido.Group(Key(1)));
        Assert.Equal([Key(2), Key(3)], KeysOf(projeto, "Manhã de chuva"));
    }

    [Fact]
    public void Os_capitulos_seguem_a_ordem_da_historia_pelo_primeiro_script_de_cada_um()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 6);

        ScriptGroupEditor.Group(projeto, [Key(4), Key(5), Key(6)], "Final");
        ScriptGroupEditor.Group(projeto, [Key(1), Key(2)], "Começo");

        var capitulos = ProjectDashboard.Read(projeto).Chapters();
        Assert.Equal(["Começo", "Parte 1", "Final"], capitulos.Select(c => c.Name));
        Assert.Equal([1, 2, 3], capitulos.Select(c => c.Number));
    }

    [Fact]
    public void Intervalo_vai_do_primeiro_ao_ultimo_script_na_ordem_numerica_e_aceita_chaves_soltas()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 8);

        var mudanca = ScriptGroupEditor.Group(projeto, [$"{Key(2)}..{Key(4)}", Key(7), Key(3)], "Cena");

        Assert.Equal([Key(2), Key(3), Key(4), Key(7)], mudanca.Keys);
    }

    [Fact]
    public void Chave_em_maiuscula_e_aceita_e_chave_desconhecida_recusa_sem_gravar_nada()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3);

        Assert.Equal([Key(1)], ScriptGroupEditor.Group(projeto, [Key(1).ToUpperInvariant()], "A").Keys);

        var antes = File.ReadAllText(projeto.ScriptNamesPath);
        var erro = Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.Group(projeto, [Key(2), "yst99999", "nada"], "B"));

        Assert.Contains("yst99999", erro.Message, StringComparison.Ordinal);
        Assert.Contains("nada", erro.Message, StringComparison.Ordinal);
        Assert.Equal(antes, File.ReadAllText(projeto.ScriptNamesPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Agrupar_sem_nome_recusa(string nome)
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 2);

        Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.Group(projeto, [Key(1)], nome));
        Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.Group(projeto, [], "A"));
    }

    [Fact]
    public void Intervalo_invertido_ou_com_ponta_desconhecida_recusa()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 5);

        Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.Group(projeto, [$"{Key(4)}..{Key(2)}"], "A"));
        Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.Group(projeto, [$"{Key(2)}..yst99999"], "A"));
    }

    [Fact]
    public void Desagrupar_devolve_os_scripts_ao_grupo_que_a_pasta_propoe()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/01_cap1"), (3, "userscript/02_cap2"));
        ScriptGroupEditor.Group(projeto, [Key(2)], "Solto");
        Assert.Equal(["Capítulo 1", "Solto", "Capítulo 2"], ProjectDashboard.Read(projeto).Chapters().Select(c => c.Name));

        ScriptGroupEditor.Ungroup(projeto, [Key(2)]);

        Assert.Equal(["Capítulo 1", "Capítulo 2"], ProjectDashboard.Read(projeto).Chapters().Select(c => c.Name));
        Assert.Null(ProjectFolder.Open(temp.Path).LoadScriptNames().Group(Key(2)));
    }

    [Fact]
    public void Renomear_o_capitulo_proposto_pela_pasta_grava_o_grupo_em_todos_os_scripts_dele()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/01_cap1"), (3, "userscript/02_cap2"));

        var mudanca = ScriptGroupEditor.RenameGroup(projeto, "capítulo 1", "Prólogo");

        Assert.Equal([Key(1), Key(2)], mudanca.Keys);
        Assert.Equal(["Prólogo", "Capítulo 2"], mudanca.Chapters.Select(c => c.Name));
        var lido = ProjectFolder.Open(temp.Path).LoadScriptNames();
        Assert.Equal("Prólogo", lido.Group(Key(1)));
        Assert.Equal("Prólogo", lido.Group(Key(2)));
        Assert.Null(lido.Group(Key(3)));
    }

    [Fact]
    public void Renomear_aceita_o_numero_do_capitulo_no_lugar_do_nome()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/02_cap2"), (3, "userscript/02_cap2"));

        var mudanca = ScriptGroupEditor.RenameGroup(projeto, "2", "Tarde");

        Assert.Equal([Key(2), Key(3)], mudanca.Keys);
        Assert.Equal(["Capítulo 1", "Tarde"], mudanca.Chapters.Select(c => c.Name));
    }

    [Fact]
    public void Renomear_para_um_nome_que_ja_existe_junta_os_dois_capitulos()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/02_cap2"), (3, "userscript/03_cap3"));

        var mudanca = ScriptGroupEditor.RenameGroup(projeto, "Capítulo 3", "Capítulo 2");

        Assert.Equal(["Capítulo 1", "Capítulo 2"], mudanca.Chapters.Select(c => c.Name));
        Assert.Equal([Key(2), Key(3)], KeysOf(projeto, "Capítulo 2"));
    }

    [Fact]
    public void Menus_e_nomes_nao_se_renomeia()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/02_cap2"), (3, "userscript/system"));

        Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.RenameGroup(projeto, ScriptGrouping.ExtraName, "Extras"));
        Assert.Null(ProjectFolder.Open(temp.Path).LoadScriptNames().Group(Key(3)));
    }

    [Fact]
    public void Renomear_grupo_inexistente_ou_para_nome_vazio_recusa_sem_gravar()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/02_cap2"));

        var erro = Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.RenameGroup(projeto, "Epílogo", "Fim"));
        Assert.Contains("Epílogo", erro.Message, StringComparison.Ordinal);
        Assert.Throws<ScriptGroupingException>(() => ScriptGroupEditor.RenameGroup(projeto, "Capítulo 1", "  "));
        Assert.Null(ProjectFolder.Open(temp.Path).LoadScriptNames().Group(Key(1)));
    }

    [Fact]
    public void Ida_e_volta_do_scripts_json_com_grupos_editados_mantem_nomes_pastas_e_grupos_e_o_texto_identico()
    {
        using var temp = new TempFolder();
        var projeto = ProjetoCom(temp.Path, 3, (1, "userscript/01_cap1"), (2, "userscript/01_cap1"), (3, "userscript/02_cap2"));
        projeto.RenameScript(Key(1), "Abertura");
        ScriptGroupEditor.Group(projeto, [Key(3)], "Manhã · 朝");
        ScriptGroupEditor.RenameGroup(projeto, "Capítulo 1", "Prólogo");
        ScriptGroupEditor.Ungroup(projeto, [Key(2)]);

        var primeiro = File.ReadAllText(projeto.ScriptNamesPath);
        var lido = ProjectFolder.Open(temp.Path).LoadScriptNames();
        projeto.SaveScriptNames(lido);

        Assert.Equal(primeiro, File.ReadAllText(projeto.ScriptNamesPath));
        Assert.Equal("Abertura", lido.CustomName(Key(1)));
        Assert.Equal("userscript/01_cap1", lido.Folder(Key(2)));
        Assert.Equal("Prólogo", lido.Group(Key(1)));
        Assert.Null(lido.Group(Key(2)));
        Assert.Equal("Manhã · 朝", lido.Group(Key(3)));
        Assert.DoesNotContain('\r', primeiro);
    }
}
