using System.Text;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
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
    public void SaveScriptNames_e_LoadScriptNames_fazem_a_ida_e_volta()
    {
        using var temp = new TempFolder();
        var projeto = NovoProjeto(temp.Path);
        var nomes = ScriptNames.Empty.WithDetected(new Dictionary<string, string?> { [Yst1] = "um" }).Rename(Yst1, "Prólogo");

        projeto.SaveScriptNames(nomes);

        Assert.Equal(nomes.Entries, projeto.LoadScriptNames().Entries);
        Assert.NotEqual(0xEF, File.ReadAllBytes(projeto.ScriptNamesPath)[0]);
    }
}
