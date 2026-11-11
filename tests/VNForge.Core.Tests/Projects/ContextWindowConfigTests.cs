using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ContextWindowConfigTests
{
    private static string Json(string ai) => $$"""
        {
          "name": "Teste",
          "game": { "path": "D:/Jogos/X", "exe": "X.exe" },
          "engine": { "id": "yuris" },
          "languages": { "source": "ja", "target": "pt-BR" },
          "encoding": { "strategy": "cp1252" },
          "ai": { "provider": "anthropic", "model": "claude-opus-5-5", "effort": "medium", "batchSize": 30{{ai}} }
        }
        """;

    [Fact]
    public void Ida_e_volta_guarda_a_janela_e_o_padrao_nao_e_gravado()
    {
        var custom = ProjectConfigSerializer.Parse(Json(", \"contextBefore\": 15, \"contextAfter\": 0"));
        var texto = ProjectConfigSerializer.Serialize(custom);

        Assert.Equal(custom, ProjectConfigSerializer.Parse(texto));
        Assert.Contains("\"contextBefore\": 15", texto);
        Assert.Contains("\"contextAfter\": 0", texto);

        var padrao = ProjectConfigSerializer.Serialize(ProjectConfigSerializer.Parse(Json("")));
        Assert.DoesNotContain("context", padrao);
    }

    [Theory]
    [InlineData(", \"contextAfter\": -1", "$.ai.contextAfter")]
    [InlineData(", \"contextAfter\": \"10\"", "$.ai.contextAfter")]
    public void Valor_fora_dos_limites_aponta_o_campo(string ai, string campo)
    {
        var erro = Assert.Throws<ProjectConfigException>(() => ProjectConfigSerializer.Parse(Json(ai)));

        Assert.Equal(campo, erro.Field);
    }
}
