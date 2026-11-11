using VNForge.App.Services;
using VNForge.Core.Projects;

namespace VNForge.App.Tests;

public sealed class ReleasePublisherVersionTests
{
    private static ProjectFolder CreateProject(TempFolder temp, string? savedVersion, bool exeWithVersion)
    {
        var game = Directory.CreateDirectory(Path.Combine(temp.Path, "jogo")).FullName;
        var exe = Path.Combine(game, "game.exe");
        if (exeWithVersion)
        {
            File.Copy(typeof(ReleasePublisherVersionTests).Assembly.Location, exe);
        }
        else
        {
            File.WriteAllText(exe, "MZ");
        }

        return ProjectFolder.Create(Path.Combine(temp.Path, "projeto"), new ProjectConfig(
            "teste",
            new GameConfig(game, "game.exe") { Version = savedVersion },
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
    }

    [Fact]
    public void Executavel_sem_versao_devolve_a_do_projeto_como_referencia()
    {
        using var temp = new TempFolder();
        var project = CreateProject(temp, "1.00", exeWithVersion: false);

        var read = new ReleasePublisher([]).Inspect(project.Root, TestContext.Current.CancellationToken);

        Assert.Null(read.GameVersion);
        Assert.Equal("1.00", read.ReferenceVersion);
    }

    [Fact]
    public void Sem_versao_no_executavel_nem_no_projeto_nao_ha_versao_nenhuma()
    {
        using var temp = new TempFolder();
        var project = CreateProject(temp, null, exeWithVersion: false);

        var read = new ReleasePublisher([]).Inspect(project.Root, TestContext.Current.CancellationToken);

        Assert.Null(read.GameVersion);
        Assert.Null(read.ReferenceVersion);
    }

    [Fact]
    public void Executavel_com_versao_a_devolve_como_a_da_pasta_e_a_do_projeto_vale_como_referencia()
    {
        using var temp = new TempFolder();
        var project = CreateProject(temp, "0.9", exeWithVersion: true);

        var read = new ReleasePublisher([]).Inspect(project.Root, TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(read.GameVersion));
        Assert.Equal("0.9", read.ReferenceVersion);
    }
}
