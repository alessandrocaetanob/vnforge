using VNForge.Core.Engines;
using VNForge.Core.Glossary;
using VNForge.Core.Memory;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Memory;

/// <summary>O glossário pessoal (#537): termos que valem em toda VN, guardados fora dos projetos e usados como ponto de partida num projeto novo.</summary>
[Trait("Risco", "R2")]
public sealed class PersonalGlossaryTests : IDisposable
{
    private readonly TempFolder temp = new();

    public void Dispose() => temp.Dispose();

    private string Config => Path.Combine(temp.Path, "config");

    private PersonalGlossary Pessoal() => new(Config);

    private ProjectFolder NovoProjeto(string pasta, params GlossaryTerm[] termos)
    {
        var projeto = ProjectFolder.Create(Path.Combine(temp.Path, pasta), new ProjectConfig(
            pasta,
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        if (termos.Length > 0)
        {
            projeto.SaveGlossary(termos);
        }

        return projeto;
    }

    [Fact]
    public void Arquivo_ilegivel_e_recusado_sem_ser_sobrescrito()
    {
        Directory.CreateDirectory(Config);
        var caminho = Path.Combine(Config, PersonalGlossary.FileName);
        File.WriteAllText(caminho, "{\"version\": 9, \"entries\": []}");
        var pessoal = Pessoal();

        var erro = Assert.Throws<MemoryFileException>(() => pessoal.Upsert(new PersonalTerm("先輩", "senpai")));

        Assert.Contains("versão 9", erro.Message);
        Assert.Contains("\"version\": 9", File.ReadAllText(caminho));
    }

    private void EscreverArquivo(string json)
    {
        Directory.CreateDirectory(Config);
        File.WriteAllText(Path.Combine(Config, PersonalGlossary.FileName), json);
    }
}
