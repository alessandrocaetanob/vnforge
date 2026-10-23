using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ProjectConfigSerializerTests
{
    private const string ExemploDoDoc02 = """
        {
          // comentários e vírgula final são aceitos: o arquivo é editado à mão
          "name": "Minha VN — PT-BR",
          "game": { "path": "D:/Jogos/MinhaVN", "exe": "MinhaVN.exe" },
          "engine": { "id": "yuris", "version": 500, "scriptKey": "0xD36FAC96" },
          "languages": { "source": "ja", "target": "pt-BR" },
          "encoding": { "strategy": "cp1252" },
          "ai": { "provider": "anthropic", "model": "claude-opus-5-5", "effort": "medium", "batchSize": 30 },
        }
        """;

    [Fact]
    public void Parse_le_o_exemplo_do_doc_02()
    {
        var config = ProjectConfigSerializer.Parse(ExemploDoDoc02);

        Assert.Equal("Minha VN — PT-BR", config.Name);
        Assert.Equal("D:/Jogos/MinhaVN", config.Game.Path);
        Assert.Equal("MinhaVN.exe", config.Game.Exe);
        Assert.Equal("yuris", config.Engine.Id);
        Assert.Equal(500, config.Engine.Version);
        Assert.Equal("0xD36FAC96", config.Engine.ScriptKey);
        Assert.Equal("ja", config.Languages.Source);
        Assert.Equal("pt-BR", config.Languages.Target);
        Assert.Equal("cp1252", config.Encoding.Strategy);
        Assert.Equal(new AiConfig("anthropic", "claude-opus-5-5", "medium", 30), config.Ai);
    }

    [Fact]
    public void Parse_sem_bloco_ai_usa_os_padroes()
    {
        var json = RemoverLinha(ExemploDoDoc02, "\"ai\"");

        var config = ProjectConfigSerializer.Parse(json);

        Assert.Equal(AiConfig.Default, config.Ai);
        Assert.Equal("claude-opus-5-5", config.Ai.Model);
    }

    [Fact]
    public void Parse_sem_version_e_scriptKey_deixa_os_dois_nulos()
    {
        var json = ExemploDoDoc02.Replace(", \"version\": 500, \"scriptKey\": \"0xD36FAC96\"", "");

        var config = ProjectConfigSerializer.Parse(json);

        Assert.Null(config.Engine.Version);
        Assert.Null(config.Engine.ScriptKey);
    }

    [Fact]
    public void Serialize_e_Parse_fazem_ida_e_volta()
    {
        var original = ProjectConfigSerializer.Parse(ExemploDoDoc02);

        var texto = ProjectConfigSerializer.Serialize(original);
        var volta = ProjectConfigSerializer.Parse(texto);

        Assert.Equal(original, volta);
        Assert.Contains("Minha VN — PT-BR", texto);
        Assert.DoesNotContain('\r', texto);
        Assert.EndsWith("\n", texto);
    }

    [Fact]
    public void Serialize_omite_version_e_scriptKey_nulos()
    {
        var config = ProjectConfigSerializer.Parse(ExemploDoDoc02) with { Engine = new EngineConfig("yuris") };

        var texto = ProjectConfigSerializer.Serialize(config);

        Assert.DoesNotContain("version", texto);
        Assert.DoesNotContain("scriptKey", texto);
    }

    [Fact]
    public void Parse_campo_obrigatorio_ausente_aponta_o_campo()
    {
        var json = ExemploDoDoc02.Replace("\"name\": \"Minha VN — PT-BR\",", "");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal("$.name", erro.Field);
        Assert.Contains("$.name", erro.Message);
        Assert.Contains("obrigatório", erro.Message);
    }

    [Fact]
    public void Parse_bloco_obrigatorio_ausente_aponta_o_bloco()
    {
        var json = RemoverLinha(ExemploDoDoc02, "\"engine\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal("$.engine", erro.Field);
        Assert.Contains("obrigatório", erro.Message);
    }

    [Theory]
    [InlineData("\"id\": \"yuris\"", "\"id\": \"yuris\", \"id2\": 1", "$.engine.id2", "desconhecido")]
    [InlineData("\"id\": \"yuris\"", "\"id\": 7", "$.engine.id", "texto")]
    [InlineData("\"id\": \"yuris\"", "\"id\": \"  \"", "$.engine.id", "vazio")]
    [InlineData("\"version\": 500", "\"version\": \"500\"", "$.engine.version", "número inteiro")]
    [InlineData("\"version\": 500", "\"version\": -1", "$.engine.version", "positivo")]
    [InlineData("0xD36FAC96", "D36FAC96", "$.engine.scriptKey", "0x")]
    [InlineData("\"engine\": { \"id\": \"yuris\", \"version\": 500, \"scriptKey\": \"0xD36FAC96\" }", "\"engine\": []", "$.engine", "objeto")]
    [InlineData("\"strategy\": \"cp1252\"", "\"strategy\": \"latin\"", "$.encoding.strategy", "sjis, fold, remap, cp1252")]
    [InlineData("\"effort\": \"medium\"", "\"effort\": \"extreme\"", "$.ai.effort", "low, medium, high")]
    [InlineData("claude-opus-5-5", "claude-opus-9", "$.ai.model", "claude-opus-5-5")]
    [InlineData("\"batchSize\": 30", "\"batchSize\": 0", "$.ai.batchSize", "entre 1 e 200")]
    [InlineData("\"target\": \"pt-BR\"", "\"target\": \"\"", "$.languages.target", "vazio")]
    [InlineData("\"exe\": \"MinhaVN.exe\"", "\"exe\": null", "$.game.exe", "texto")]
    public void Parse_valor_invalido_aponta_o_campo(string trecho, string troca, string campo, string motivo)
    {
        var json = ExemploDoDoc02.Replace(trecho, troca);
        Assert.NotEqual(ExemploDoDoc02, json);

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal(campo, erro.Field);
        Assert.Contains(campo, erro.Message);
        Assert.Contains(motivo, erro.Message);
    }

    [Fact]
    public void Parse_campo_desconhecido_com_ponto_no_nome_nao_vira_caminho_aninhado()
    {
        var json = ExemploDoDoc02.Replace("\"name\"", "\"custom.option\": 1, \"name\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal("$['custom.option']", erro.Field);
    }

    [Fact]
    public void Parse_chave_de_api_no_arquivo_e_recusada_com_orientacao()
    {
        var json = ExemploDoDoc02.Replace("\"batchSize\": 30", "\"batchSize\": 30, \"apiKey\": \"sk-ant-x\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal("$.ai.apiKey", erro.Field);
        Assert.Contains("ANTHROPIC_API_KEY", erro.Message);
        Assert.DoesNotContain("sk-ant-x", erro.Message);
    }

    [Theory]
    [InlineData("api_key")]
    [InlineData("API-KEY")]
    [InlineData("anthropicApiKey")]
    [InlineData("ApiKey")]
    public void Parse_qualquer_variante_de_chave_de_api_e_recusada(string nome)
    {
        var json = ExemploDoDoc02.Replace("\"name\"", $"\"{nome}\": \"sk-ant-x\", \"name\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Contains("ANTHROPIC_API_KEY", erro.Message);
        Assert.DoesNotContain("sk-ant-x", erro.Message);
    }

    [Theory]
    [InlineData("\"provider\": \"anthropic\"", "\"provider\": \"openai\"", "$.ai.provider")]
    [InlineData("\"provider\": \"anthropic\"", "\"provider\": \"Anthropic\"", "$.ai.provider")]
    [InlineData("\"strategy\": \"cp1252\"", "\"strategy\": \"sk-ant-segredo\"", "$.encoding.strategy")]
    [InlineData("claude-opus-5-5", "sk-ant-segredo", "$.ai.model")]
    public void Parse_valor_recusado_nao_e_ecoado_na_mensagem(string trecho, string troca, string campo)
    {
        var json = ExemploDoDoc02.Replace(trecho, troca);

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal(campo, erro.Field);
        Assert.DoesNotContain("sk-ant-segredo", erro.Message);
        Assert.DoesNotContain("openai", erro.Message);
        Assert.DoesNotContain("Anthropic", erro.Message);
    }

    [Fact]
    public void Parse_provedor_desconhecido_lista_os_aceitos()
    {
        var json = ExemploDoDoc02.Replace("\"provider\": \"anthropic\"", "\"provider\": \"openai\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Contains("anthropic", erro.Message);
    }

    [Fact]
    public void Parse_json_malformado_aponta_linha_e_coluna_em_portugues()
    {
        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse("{\n  \"name\": \n"));

        Assert.Equal("$", erro.Field);
        Assert.Matches(@"linha \d+, coluna \d+", erro.Message);
        Assert.DoesNotContain("LineNumber", erro.Message);
        Assert.DoesNotContain("BytePositionInLine", erro.Message);
    }

    [Fact]
    public void Parse_raiz_que_nao_e_objeto_aponta_a_raiz()
    {
        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse("[]"));

        Assert.Equal("$", erro.Field);
    }

    [Fact]
    public void Serialize_recusa_configuracao_invalida()
    {
        var config = ProjectConfigSerializer.Parse(ExemploDoDoc02) with { Encoding = new EncodingConfig("latin") };

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Serialize(config));

        Assert.Equal("$.encoding.strategy", erro.Field);
    }

    private static string RemoverLinha(string texto, string trecho) =>
        string.Join('\n', texto.Split('\n').Where(l => !l.Contains(trecho)));
}
