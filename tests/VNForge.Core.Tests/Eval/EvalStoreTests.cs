using VNForge.Core.Eval;
using VNForge.Core.Projects;
using VNForge.Core.Tests.Projects;
using static VNForge.Core.Tests.Eval.EvalFixtures;

namespace VNForge.Core.Tests.Eval;

public sealed class EvalStoreTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public EvalStoreTests()
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

    [Fact]
    public void Sem_rodada_nao_ha_o_que_carregar()
    {
        Assert.Null(EvalStore.LoadLatest(_project));
        Assert.Empty(EvalStore.LoadConfigs(_project));
    }

    [Fact]
    public void Grava_a_rodada_em_cache_eval_e_le_de_volta_igual()
    {
        var round = Board() with
        {
            Pairs = [new BlindPair(1, "l1", "A", "C")],
            Answers = [new BlindAnswer(1, BlindChoice.Neither)],
            ChosenConfigId = "C",
            Sample = [new EvalSample("l1", "s1", "Misaki", "原文", "Tradução", "antes", "depois")],
        };

        EvalStore.SaveRound(_project, round);
        var loaded = EvalStore.LoadLatest(_project)!;

        Assert.True(File.Exists(Path.Combine(_project.CacheDirectory, "eval", "rounds", $"{round.Id}.json")));
        Assert.Equal(round.Id, loaded.Id);
        Assert.Equal("C", loaded.ChosenConfigId);
        Assert.Equal(BlindChoice.Neither, loaded.Answers[0].Choice);
        Assert.Equal(round.Reports.Count, loaded.Reports.Count);
        Assert.Equal(round.Reports[2].Config, loaded.Reports[2].Config);
        Assert.Equal(round.Reports[0].MeanChrf, loaded.Reports[0].MeanChrf);
        Assert.Equal("Misaki", loaded.Sample[0].Speaker);
    }

    [Fact]
    public void A_rodada_mais_recente_vence_e_arquivo_ilegivel_e_pulado()
    {
        EvalStore.SaveRound(_project, Board() with { Id = "20261101-100000" });
        EvalStore.SaveRound(_project, Board() with { Id = "20261102-100000" });
        File.WriteAllText(EvalStore.RoundPath(_project, "20261103-100000"), "{ não é json");

        Assert.Equal("20261102-100000", EvalStore.LoadLatest(_project)!.Id);
    }

    [Fact]
    public void Grava_e_le_as_configuracoes_do_projeto()
    {
        EvalConfig[] configs = [Config("A", "claude-opus-5-5", "high"), Config("B", bible: false)];

        EvalStore.SaveConfigs(_project, configs);

        Assert.Equal(configs, EvalStore.LoadConfigs(_project));
    }

    [Fact]
    public void O_id_da_rodada_ordena_como_texto()
    {
        var early = EvalStore.NewRoundId(new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero));
        var late = EvalStore.NewRoundId(new DateTimeOffset(2026, 11, 8, 21, 30, 5, TimeSpan.Zero));

        Assert.Equal("20261108-093005".Length, late.Length);
        Assert.True(string.CompareOrdinal(early, late) < 0);
    }
}
