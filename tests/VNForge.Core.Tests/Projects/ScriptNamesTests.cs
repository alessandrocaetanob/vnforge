using System.Text;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ScriptNamesTests
{
    private const string Yst1 = "yst00001";
    private const string Yst2 = "yst00002";

    private static ProjectFolder NovoProjeto(string raiz) => ProjectFolder.Create(raiz, new ProjectConfig(
        "Minha VN",
        new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    [Fact]
    public void Sem_entrada_o_nome_e_nulo()
    {
        Assert.Null(ScriptNames.Empty.DisplayName(Yst1));
        Assert.Null(ScriptNames.Empty.CustomName(Yst1));
    }

    [Fact]
    public void O_nome_do_usuario_vale_mais_que_o_detectado()
    {
        var nomes = ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { [Yst1] = "manha" }).Rename(Yst1, "Capítulo 1 · Manhã");

        Assert.Equal("Capítulo 1 · Manhã", nomes.DisplayName(Yst1));
        Assert.Equal("Capítulo 1 · Manhã", nomes.CustomName(Yst1));
    }

    [Fact]
    public void Sem_nome_do_usuario_vale_o_detectado()
    {
        var nomes = ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { [Yst1] = "manha" });

        Assert.Equal("manha", nomes.DisplayName(Yst1));
        Assert.Null(nomes.CustomName(Yst1));
    }

    [Fact]
    public void Renomear_com_texto_vazio_volta_ao_nome_detectado()
    {
        var nomes = ScriptNames.Empty
            .WithDetected(new Dictionary<string, string?> { [Yst1] = "manha" })
            .Rename(Yst1, "Prólogo")
            .Rename(Yst1, "   ");

        Assert.Equal("manha", nomes.DisplayName(Yst1));
        Assert.Null(nomes.CustomName(Yst1));
    }

    [Fact]
    public void Renomear_apara_os_espacos_das_pontas()
    {
        Assert.Equal("Prólogo", ScriptNames.Empty.Rename(Yst1, "  Prólogo ").DisplayName(Yst1));
    }

    [Fact]
    public void Renomear_um_script_sem_nome_detectado_cria_a_entrada()
    {
        var nomes = ScriptNames.Empty.Rename(Yst2, "Final");

        Assert.Equal("Final", nomes.DisplayName(Yst2));
        Assert.Single(nomes.Entries);
    }

    [Fact]
    public void Renomear_ate_apagar_tudo_nao_deixa_entrada_vazia()
    {
        Assert.Empty(ScriptNames.Empty.Rename(Yst1, "A").Rename(Yst1, null).Entries);
    }

    [Fact]
    public void A_extracao_atualiza_o_detectado_e_preserva_o_nome_do_usuario()
    {
        var antes = ScriptNames.Empty
            .WithDetected(new Dictionary<string, string?> { [Yst1] = "antigo", [Yst2] = "dois" })
            .Rename(Yst1, "Prólogo");

        var depois = antes.WithDetected(new Dictionary<string, string?> { [Yst1] = "novo", [Yst2] = "dois-b" });

        Assert.Equal("Prólogo", depois.DisplayName(Yst1));
        Assert.Equal("dois-b", depois.DisplayName(Yst2));
    }

    [Fact]
    public void A_extracao_descarta_o_detectado_de_script_que_sumiu_mas_guarda_o_nome_do_usuario()
    {
        var antes = ScriptNames.Empty
            .WithDetected(new Dictionary<string, string?> { [Yst1] = "um", [Yst2] = "dois" })
            .Rename(Yst1, "Prólogo");

        var depois = antes.WithDetected(new Dictionary<string, string?>());

        Assert.Equal("Prólogo", depois.DisplayName(Yst1));
        Assert.Null(depois.DisplayName(Yst2));
        Assert.Single(depois.Entries);
    }

    [Fact]
    public void Script_sem_nome_detectado_na_extracao_perde_o_detectado_antigo()
    {
        var antes = ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { [Yst1] = "um" });

        var depois = antes.WithDetected(new Dictionary<string, string?> { [Yst1] = null });

        Assert.Null(depois.DisplayName(Yst1));
    }

    [Fact]
    public void Serializar_e_ler_devolve_as_mesmas_entradas_em_utf8_sem_bom_e_lf()
    {
        var nomes = ScriptNames.Empty
            .WithDetected(new Dictionary<string, string?> { [Yst2] = "dois", [Yst1] = "um" })
            .Rename(Yst1, "Capítulo 1 · 朝");

        var json = ScriptNamesSerializer.Serialize(nomes);
        var lido = ScriptNamesSerializer.Parse(json, "scripts.json");

        Assert.Equal(nomes.Entries, lido.Entries);
        Assert.Equal([Yst1, Yst2], lido.Entries.Select(e => e.Key));
        Assert.DoesNotContain('\r', json);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.Contains("朝", json, StringComparison.Ordinal);
        Assert.Equal(json, ScriptNamesSerializer.Serialize(lido));
    }

    [Theory]
    [InlineData("{", "malformado")]
    [InlineData("[]", "objeto")]
    [InlineData("{\"entries\":[]}", "version")]
    [InlineData("{\"version\":1}", "entries")]
    [InlineData("{\"version\":2,\"entries\":[]}", "mais nova")]
    [InlineData("{\"version\":1,\"entries\":[],\"extra\":1}", "extra")]
    [InlineData("{\"version\":1,\"entries\":[{\"name\":\"A\"}]}", "key")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"\",\"name\":\"A\"}]}", "key")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"Yst1\",\"name\":\"A\"}]}", "Yst1")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"a\",\"nome\":\"A\"}]}", "nome")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"a\",\"name\":\"A\"},{\"key\":\"a\",\"name\":\"B\"}]}", "mais de uma vez")]
    [InlineData("{\"version\":1,\"entries\":[{\"key\":\"a\"}]}", "sem nome")]
    public void Arquivo_invalido_vira_erro_em_portugues_com_o_motivo(string json, string trecho)
    {
        var ex = Assert.Throws<GlossaryFileException>(() => ScriptNamesSerializer.Parse(json, "scripts.json"));

        Assert.Contains(trecho, ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadScriptNames_sem_arquivo_devolve_vazio()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);

        Assert.Equal(Path.Combine(temp.Path, "scripts.json"), projeto.ScriptNamesPath);
        Assert.Empty(projeto.LoadScriptNames().Entries);
    }

    [Fact]
    public void SaveScriptNames_e_LoadScriptNames_fazem_a_ida_e_volta()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        var nomes = ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { [Yst1] = "um" }).Rename(Yst1, "Prólogo");

        projeto.SaveScriptNames(nomes);

        Assert.Equal(nomes.Entries, projeto.LoadScriptNames().Entries);
        Assert.NotEqual(0xEF, File.ReadAllBytes(projeto.ScriptNamesPath)[0]);
    }

    [Fact]
    public void LoadScriptNames_fora_de_utf8_vira_erro_do_arquivo()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        File.WriteAllBytes(projeto.ScriptNamesPath, [0xFF, 0xFE, 0x00]);

        var ex = Assert.Throws<GlossaryFileException>(() => projeto.LoadScriptNames());

        Assert.Contains("UTF-8", ex.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RenameScript_grava_o_nome_no_projeto()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        projeto.SaveScriptNames(ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { [Yst1] = "um" }));

        projeto.RenameScript(Yst1, "Prólogo");

        Assert.Equal("Prólogo", projeto.LoadScriptNames().DisplayName(Yst1));
        Assert.Contains("Prólogo", Encoding.UTF8.GetString(File.ReadAllBytes(projeto.ScriptNamesPath)), StringComparison.Ordinal);
    }
}
