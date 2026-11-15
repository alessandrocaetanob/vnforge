using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
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
    public void Create_grava_vnforge_json_e_as_quatro_pastas()
    {
        using var temp = new TempFolder();
        var raiz = Path.Combine(temp.Path, "minha-vn-ptbr");

        var projeto = ProjectFolder.Create(raiz, NovaConfig());

        Assert.True(File.Exists(Path.Combine(raiz, ManifestFile)));
        foreach (var pasta in new[] { "text", ".cache", "build", "backup" })
        {
            Assert.True(Directory.Exists(Path.Combine(raiz, pasta)), pasta);
        }

        Assert.Equal(Path.Combine(raiz, "text"), projeto.TextDirectory);
        Assert.Equal(Path.Combine(raiz, ".cache"), projeto.CacheDirectory);
        Assert.Equal(Path.Combine(raiz, "build"), projeto.BuildDirectory);
        Assert.Equal(Path.Combine(raiz, "backup"), projeto.BackupDirectory);
    }

    [Fact]
    public void Create_grava_gitignore_com_as_pastas_de_trabalho()
    {
        using var temp = new TempFolder();

        ProjectFolder.Create(temp.Path, NovaConfig());

        var linhas = File.ReadAllLines(Path.Combine(temp.Path, ".gitignore"));
        Assert.Equal([".cache/", "build/", "backup/"], linhas.Where(l => l.Length > 0 && !l.StartsWith('#')));
    }

    [Fact]
    public void Create_nao_sobrescreve_gitignore_existente()
    {
        using var temp = new TempFolder();
        var gitignore = Path.Combine(temp.Path, ".gitignore");
        File.WriteAllText(gitignore, "meu\n");

        ProjectFolder.Create(temp.Path, NovaConfig());

        Assert.Equal("meu\n", File.ReadAllText(gitignore));
    }

    [Fact]
    public void Open_sem_vnforge_json_explica_o_que_falta()
    {
        using var temp = new TempFolder();

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectFolder.Open(temp.Path));

        Assert.Contains(ManifestFile, erro.Message);
        Assert.Contains(temp.Path, erro.Message);
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
