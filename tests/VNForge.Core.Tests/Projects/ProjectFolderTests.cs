using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ProjectFolderTests
{
    private static ProjectConfig NovaConfig() => new(
        "Minha VN",
        new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
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

        Assert.True(File.Exists(Path.Combine(raiz, "vnforge.json")));
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
    public void Create_e_Open_devolvem_a_mesma_configuracao()
    {
        using var temp = new TempFolder();
        var config = NovaConfig();
        ProjectFolder.Create(temp.Path, config);

        var aberto = ProjectFolder.Open(temp.Path);

        Assert.Equal(config, aberto.LoadConfig());
    }

    [Fact]
    public void Create_nao_sobrescreve_projeto_existente()
    {
        using var temp = new TempFolder();
        ProjectFolder.Create(temp.Path, NovaConfig());

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectFolder.Create(temp.Path, NovaConfig()));

        Assert.Contains("vnforge.json", erro.Message);
    }

    [Fact]
    public void Create_com_configuracao_invalida_nao_cria_nada()
    {
        using var temp = new TempFolder();
        var raiz = Path.Combine(temp.Path, "p");
        var invalida = NovaConfig() with { Name = " " };

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectFolder.Create(raiz, invalida));

        Assert.Equal("$.name", erro.Field);
        Assert.False(Directory.Exists(raiz));
    }

    [Fact]
    public void Open_sem_vnforge_json_explica_o_que_falta()
    {
        using var temp = new TempFolder();

        var erro = Assert.Throws<ProjectConfigException>(() => ProjectFolder.Open(temp.Path));

        Assert.Contains("vnforge.json", erro.Message);
        Assert.Contains(temp.Path, erro.Message);
    }

    [Fact]
    public void LoadConfig_com_arquivo_invalido_aponta_o_campo()
    {
        using var temp = new TempFolder();
        var projeto = ProjectFolder.Create(temp.Path, NovaConfig());
        var texto = File.ReadAllText(projeto.ConfigPath).Replace("\"fold\"", "\"latin\"");
        File.WriteAllText(projeto.ConfigPath, texto);

        var erro = Assert.Throws<ProjectConfigException>(() => projeto.LoadConfig());

        Assert.Equal("$.encoding.strategy", erro.Field);
    }

    [Fact]
    public void SaveConfig_substitui_o_arquivo_e_nao_deixa_temporarios()
    {
        using var temp = new TempFolder();
        var projeto = ProjectFolder.Create(temp.Path, NovaConfig());
        var nova = NovaConfig() with { Name = "Outro nome" };

        projeto.SaveConfig(nova);

        Assert.Equal(nova, projeto.LoadConfig());
        Assert.Equal(["vnforge.json"], Directory.GetFiles(temp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void EnsureDirectories_recria_pastas_que_faltam()
    {
        using var temp = new TempFolder();
        ProjectFolder.Create(temp.Path, NovaConfig());
        Directory.Delete(Path.Combine(temp.Path, "build"));

        var projeto = ProjectFolder.Open(temp.Path);
        projeto.EnsureDirectories();

        Assert.True(Directory.Exists(projeto.BuildDirectory));
    }
}
