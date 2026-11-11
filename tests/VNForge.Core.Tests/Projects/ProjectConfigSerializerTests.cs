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
    public void Parse_bloco_obrigatorio_ausente_aponta_o_bloco()
    {
        var json = RemoverLinha(ExemploDoDoc02, "\"engine\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal("$.engine", erro.Field);
        Assert.Contains("obrigatório", erro.Message);
    }

    [Theory]
    [InlineData("\"id\": \"yuris\"", "\"id\": 7", "$.engine.id", "texto")]
    [InlineData("\"id\": \"yuris\"", "\"id\": \"  \"", "$.engine.id", "vazio")]
    [InlineData("\"version\": 500", "\"version\": -1", "$.engine.version", "positivo")]
    [InlineData("0xD36FAC96", "D36FAC96", "$.engine.scriptKey", "0x")]
    [InlineData("\"engine\": { \"id\": \"yuris\", \"version\": 500, \"scriptKey\": \"0xD36FAC96\" }", "\"engine\": []", "$.engine", "objeto")]
    [InlineData("\"batchSize\": 30", "\"batchSize\": 0", "$.ai.batchSize", "entre 1 e 200")]
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

    [Theory]
    [InlineData("API-KEY")]
    public void Parse_qualquer_variante_de_chave_de_api_e_recusada(string nome)
    {
        var json = ExemploDoDoc02.Replace("\"name\"", $"\"{nome}\": \"sk-ant-x\", \"name\"");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Contains("ANTHROPIC_API_KEY", erro.Message);
        Assert.DoesNotContain("sk-ant-x", erro.Message);
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
    public void Titulo_do_jogo_faz_ida_e_volta_com_original_e_traducao()
    {
        var json = ExemploDoDoc02.Replace(
            "\"game\": { \"path\": \"D:/Jogos/MinhaVN\", \"exe\": \"MinhaVN.exe\" }",
            "\"game\": { \"path\": \"D:/Jogos/MinhaVN\", \"exe\": \"MinhaVN.exe\", \"title\": { \"original\": \"原題\", \"translation\": \"Meu título\" } }");

        var config = ProjectConfigSerializer.Parse(json);
        var volta = ProjectConfigSerializer.Parse(ProjectConfigSerializer.Serialize(config));

        Assert.Equal(new GameTitleConfig("原題", "Meu título"), config.Game.Title);
        Assert.Equal(config, volta);
    }

    [Fact]
    public void Titulo_do_jogo_sem_original_aponta_o_campo()
    {
        var json = ExemploDoDoc02.Replace(
            "\"exe\": \"MinhaVN.exe\" }",
            "\"exe\": \"MinhaVN.exe\", \"title\": { \"translation\": \"x\" } }");

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal("$.game.title.original", erro.Field);
    }

    [Fact]
    public void Versao_do_jogo_faz_ida_e_volta_no_vnforge_json()
    {
        var config = ProjectConfigSerializer.Parse(ExemploDoDoc02);
        config = config with { Game = config.Game with { Version = "1.01" } };

        var texto = ProjectConfigSerializer.Serialize(config);

        Assert.Contains("\"version\": \"1.01\"", texto, StringComparison.Ordinal);
        Assert.Equal("1.01", ProjectConfigSerializer.Parse(texto).Game.Version);
    }

    [Fact]
    public void Versao_do_jogo_longa_demais_e_recusada()
    {
        var config = ProjectConfigSerializer.Parse(ExemploDoDoc02);
        config = config with { Game = config.Game with { Version = new string('1', GameConfig.MaxVersionLength + 1) } };

        Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Serialize(config));
    }

    private static string RemoverLinha(string texto, string trecho) =>
        string.Join('\n', texto.Split('\n').Where(l => !l.Contains(trecho)));
}
