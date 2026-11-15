using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R5")]
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

    [Theory]
    [InlineData("\"id\": \"yuris\"", "\"id\": \"yuris\", \"id2\": 1", "$.engine.id2", "desconhecido")]
    [InlineData("\"id\": \"yuris\"", "\"id\": 7", "$.engine.id", "texto")]
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
    public void Parse_valor_recusado_nao_e_ecoado_na_mensagem(string trecho, string troca, string campo)
    {
        var json = ExemploDoDoc02.Replace(trecho, troca);

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(json));

        Assert.Equal(campo, erro.Field);
        Assert.DoesNotContain("sk-ant-segredo", erro.Message);
        Assert.DoesNotContain("openai", erro.Message);
        Assert.DoesNotContain("Anthropic", erro.Message);
    }

    private static string RemoverLinha(string texto, string trecho) =>
        string.Join('\n', texto.Split('\n').Where(l => !l.Contains(trecho)));
}
