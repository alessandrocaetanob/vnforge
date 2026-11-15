using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public class ProjectTextLockTests
{
    private const string Key = "yst00001";

    private static ProjectFolder NewProject(string root) => ProjectFolder.Create(root, new ProjectConfig(
        "Minha VN",
        new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default));

    [Fact]
    public void Duas_instancias_da_mesma_pasta_dividem_a_trava()
    {
        using var temp = new TempFolder();
        var first = NewProject(temp.Path);
        var second = ProjectFolder.Open(temp.Path);

        Assert.Same(ProjectTextLock.For(first), ProjectTextLock.For(second));
    }

    [Fact]
    public void Pastas_diferentes_tem_travas_diferentes()
    {
        using var one = new TempFolder();
        using var two = new TempFolder();

        Assert.NotSame(ProjectTextLock.For(NewProject(one.Path)), ProjectTextLock.For(NewProject(two.Path)));
    }
}
