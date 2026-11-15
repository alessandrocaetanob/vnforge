using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R2")]
public sealed class GameStateFileTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public GameStateFileTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static GameStateFile Sample() => new(
        GameStateFile.CurrentVersion,
        new GameInGame(GameTestKind.AccentTest, new DateTimeOffset(2026, 10, 22, 23, 16, 0, TimeSpan.Zero), "2026-10-22_2316", null, "abc123"),
        new GameLastTest(new DateTimeOffset(2026, 10, 22, 23, 17, 0, TimeSpan.Zero), GameTestKind.Translation, [new GameTestStepTime(GameTestStep.Check, 120), new GameTestStepTime(GameTestStep.Open, 15)]));

    [Theory]
    [InlineData("não é json")]
    public void Arquivo_ilegivel_ou_de_outra_versao_vira_estado_vazio(string content)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(GameStateFile.PathFor(_project), content);

        Assert.Equal(GameStateFile.Empty, GameStateFile.Load(_project));
    }
}
