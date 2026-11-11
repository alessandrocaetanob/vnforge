using VNForge.App.Services;
using VNForge.Core.Projects;
using VNForge.Core.Publishing;

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
    public void Executavel_com_versao_vence_a_do_projeto_na_tela_como_no_patch()
    {
        using var temp = new TempFolder();
        var project = CreateProject(temp, "0.9", exeWithVersion: true);

        var read = new ReleasePublisher([]).Inspect(project.Root, TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(read.GameVersion));
        Assert.NotEqual("0.9", read.GameVersion);
        Assert.Equal(read.GameVersion, read.ReferenceVersion);
        Assert.Equal(ReleaseService.ResolveGameVersion(null, ReleaseService.DetectGameVersion(project.Root, project.LoadConfig()), "0.9"), read.ReferenceVersion);
    }
}
