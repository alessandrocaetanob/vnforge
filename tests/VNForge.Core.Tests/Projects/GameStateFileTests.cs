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
    public void Sem_arquivo_o_estado_e_vazio()
    {
        Assert.Equal(GameStateFile.Empty, GameStateFile.Load(_project));
    }

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

    [Fact]
    public void O_json_usa_nomes_em_camelCase_e_tipos_em_texto()
    {
        GameStateFile.Save(_project, Sample());

        var text = File.ReadAllText(GameStateFile.PathFor(_project));

        Assert.Contains("\"inGame\"", text);
        Assert.Contains("\"kind\": \"accentTest\"", text);
        Assert.Contains("\"step\": \"check\"", text);
        Assert.Contains("\"backupDirectory\": \"2026-10-22_2316\"", text);
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("{\"version\": 99}")]
    [InlineData("{\"version\": 1, \"inGame\": {\"kind\": \"outro\"}}")]
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
    public void Arquivo_antigo_sem_retrato_le_com_o_retrato_nulo()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(GameStateFile.PathFor(_project), "{\"version\": 1, \"lastTest\": {\"at\": \"2026-10-22T23:17:00+00:00\", \"kind\": \"translation\", \"steps\": []}}");

        var loaded = GameStateFile.Load(_project);

        Assert.NotNull(loaded.LastTest);
        Assert.Null(loaded.LastTest.Snapshot);
    }

    [Fact]
    public void Limpar_o_jogo_guarda_o_ultimo_teste()
    {
        GameStateFile.Save(_project, Sample());

        GameStateFile.TryClearInGame(_project);

        var loaded = GameStateFile.Load(_project);
        Assert.Null(loaded.InGame);
        Assert.NotNull(loaded.LastTest);
    }

    [Fact]
    public void Limpar_sem_nada_no_jogo_nao_cria_arquivo()
    {
        GameStateFile.TryClearInGame(_project);

        Assert.False(File.Exists(GameStateFile.PathFor(_project)));
    }
}
