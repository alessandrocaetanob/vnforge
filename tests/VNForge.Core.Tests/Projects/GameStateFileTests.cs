using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

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

    [Fact]
    public void Grava_em_cache_game_state_json_e_le_de_volta_igual()
    {
        GameStateFile.Save(_project, Sample());

        Assert.Equal(Path.Combine(_project.Root, ".cache", "game-state.json"), GameStateFile.PathFor(_project));
        var loaded = GameStateFile.Load(_project);
        var expected = Sample();
        Assert.Equal(expected.InGame, loaded.InGame);
        Assert.Equal(expected.LastTest!.Steps, loaded.LastTest!.Steps);
        Assert.Equal(expected.LastTest.At, loaded.LastTest.At);
    }

    [Theory]
    [InlineData("não é json")]
    public void Arquivo_ilegivel_ou_de_outra_versao_vira_estado_vazio(string content)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(GameStateFile.PathFor(_project), content);

        Assert.Equal(GameStateFile.Empty, GameStateFile.Load(_project));
    }

    [Fact]
    public void Retrato_das_traducoes_vai_e_volta_no_json_so_com_hashes()
    {
        var snapshot = new TranslationSnapshot(new Dictionary<string, IReadOnlyDictionary<string, string>> { ["a001"] = new Dictionary<string, string> { ["1"] = "0123456789ab" } });
        var state = Sample() with { LastTest = Sample().LastTest! with { Snapshot = snapshot } };

        GameStateFile.Save(_project, state);

        var loaded = GameStateFile.Load(_project).LastTest!.Snapshot!;
        Assert.Equal("0123456789ab", loaded.Scripts["a001"]["1"]);
        Assert.Contains("\"snapshot\"", File.ReadAllText(GameStateFile.PathFor(_project)));
    }

    [Fact]
    public void Retrato_que_falhou_vai_e_volta_no_json_e_arquivo_antigo_le_como_falso()
    {
        GameStateFile.Save(_project, Sample() with { LastTest = Sample().LastTest! with { SnapshotFailed = true } });
        Assert.True(GameStateFile.Load(_project).LastTest!.SnapshotFailed);

        File.WriteAllText(GameStateFile.PathFor(_project), "{\"version\": 1, \"lastTest\": {\"at\": \"2026-10-22T23:17:00+00:00\", \"kind\": \"translation\", \"steps\": []}}");

        Assert.False(GameStateFile.Load(_project).LastTest!.SnapshotFailed);
    }
}
