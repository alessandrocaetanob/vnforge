using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ProjectFolderTests
{
    private const string ManifestFile = "vnforge.json";
    private static readonly string GamePath = Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN");

    private static ProjectConfig NovaConfig() => new(
        "Minha VN",
        new GameConfig(GamePath, "MinhaVN.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default);

    [Fact]
    public void Create_nao_sobrescreve_projeto_existente()
    {
        using var temp = new TempFolder();
        ProjectFolder.Create(temp.Path, NovaConfig());

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectFolder.Create(temp.Path, NovaConfig()));

        Assert.Contains(ManifestFile, erro.Message);
    }

    [Fact]
    public void LoadConfig_recusa_bytes_que_nao_sao_UTF8()
    {
        using var temp = new TempFolder();
        var projeto = ProjectFolder.Create(temp.Path, NovaConfig());
        var bytes = File.ReadAllBytes(projeto.ConfigPath);
        bytes[Array.IndexOf(bytes, (byte)'M')] = 0xFF;
        File.WriteAllBytes(projeto.ConfigPath, bytes);

        var erro = Assert.Throws<ProjectConfigException>(() => projeto.LoadConfig());

        Assert.Contains("UTF-8", erro.Message);
    }
}
