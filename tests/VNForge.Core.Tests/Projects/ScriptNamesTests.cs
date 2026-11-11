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

    [Theory]
    [InlineData("[]", "objeto")]
    [InlineData("{\"entries\":[]}", "version")]
    [InlineData("{\"version\":1}", "entries")]
    [InlineData("{\"version\":2,\"entries\":[]}", "mais nova")]
    [InlineData("{\"version\":1,\"entries\":[],\"extra\":1}", "extra")]
    [InlineData("{\"version\":1,\"entries\":[{\"name\":\"A\"}]}", "key")]
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
    public void O_grupo_e_a_pasta_vao_e_voltam_pelo_scripts_json()
    {
        var nomes = ScriptNames.Empty
            .WithDetected(new Dictionary<string, DetectedScript> { [Yst1] = new("manha", "userscript/01_cap1"), [Yst2] = new(null, "userscript/02_cap2") })
            .WithGroup(Yst1, "Manhã de chuva");

        var json = ScriptNamesSerializer.Serialize(nomes);
        var lido = ScriptNamesSerializer.Parse(json, "scripts.json");

        Assert.Contains("\"folder\": \"userscript/01_cap1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"group\": \"Manhã de chuva\"", json, StringComparison.Ordinal);
        Assert.Equal("Manhã de chuva", lido.Group(Yst1));
        Assert.Equal("userscript/02_cap2", lido.Folder(Yst2));
        Assert.Null(lido.Group(Yst2));
        Assert.Null(lido.DisplayName(Yst2));
        Assert.Equal(json, ScriptNamesSerializer.Serialize(lido));
    }
}
