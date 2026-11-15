using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public class StyleConfigTests
{
    private const string Base = """
        {
          "name": "Minha VN",
          "game": { "path": "D:/Jogos/MinhaVN", "exe": "MinhaVN.exe" },
          "engine": { "id": "yuris" },
          "languages": { "source": "ja", "target": "pt-BR" },
          "encoding": { "strategy": "cp1252" }
          STYLE
        }
        """;

    private static ProjectConfig Parse(string style = "") => ProjectConfigSerializer.Parse(Base.Replace("STYLE", style));

    [Fact]
    public void O_delimitador_da_fala_vem_sem_sinal_e_faz_ida_e_volta_com_guillemets()
    {
        Assert.Equal(SpeakerDelimiterStyle.None, StyleConfig.Default.SpeakerDelimiter);
        Assert.Equal(SpeakerDelimiterStyle.None, Parse().Style.SpeakerDelimiter);
        var guillemets = Parse() with { Style = StyleConfig.Default with { SpeakerDelimiter = SpeakerDelimiterStyle.Guillemets } };

        var json = ProjectConfigSerializer.Serialize(guillemets);

        Assert.Contains("\"speakerDelimiter\": \"guillemets\"", json);
        Assert.Equal(SpeakerDelimiterStyle.Guillemets, ProjectConfigSerializer.Parse(json).Style.SpeakerDelimiter);
        Assert.Equal(SpeakerDelimiterStyle.Guillemets, Parse(", \"style\": { \"speakerDelimiter\": \"guillemets\" }").Style.SpeakerDelimiter);
        Assert.Equal(SpeakerDelimiterStyle.None, Parse(", \"style\": { \"speakerDelimiter\": \"none\" }").Style.SpeakerDelimiter);
    }
}
