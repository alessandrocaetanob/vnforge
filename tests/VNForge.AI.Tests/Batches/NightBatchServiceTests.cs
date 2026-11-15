using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Batches;

[Trait("Risco", "R2")]
[Trait("Risco", "R5")]
public sealed class NightBatchServiceTests : IDisposable
{
    private const string Key = "yst00001";

    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 11, 9));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectFolder _project;
    private readonly FakeMessageBatchClient _client = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 8, 23, 41, 0, TimeSpan.Zero));

    public NightBatchServiceTests()
    {
        Directory.CreateDirectory(_root);
        _project = ProjectFolder.Create(_root, new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Key, [.. Enumerable.Range(0, 45).Select(i => Line(i, i == 44 ? TranslationStatus.Approved : TranslationStatus.Untranslated))]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static TextUnit Line(int i, TranslationStatus status) =>
        new($"{Key}:{i:D6}", TextUnitKind.Dialogue, "美咲", $"原文{i}", status == TranslationStatus.Untranslated ? null : $"aprovada {i}", status);

    private static NightBatchOrder Order(string model = ClaudeModels.Opus) => new("O capítulo", model, ClaudeEffort.Medium, 20, Rate, key => key == Key);

    private NightBatchService Service(IMessageBatchClient? client = null) => new(client ?? _client, _project, _clock);

    private static string Id(int i) => $"{Key}:{i:D6}";

    private decimal CostEstimatorBrl(string model)
    {
        var selection = VNForge.AI.Translation.LineSelection.Build(_project.LoadAllText(), null, null, TranslationQuoter.IsPending);
        var config = _project.LoadConfig();
        var builder = new VNForge.AI.Translation.TranslationBatchBuilder([], new VNForge.Core.Glossary.CharacterBook([]), string.Empty, config.Ai.ContextBefore, config.Style, config.Ai.ContextAfter, []) { PreviousFromSource = true };
        var batches = selection.Pending.Chunk(20).Select(chunk => selection.BuildBatch(builder, chunk));
        return Rate.ToBrl(CostEstimator.EstimateBatches(batches, model, null, ClaudeEffort.Medium).Dollars);
    }

    [Fact]
    public async Task Gravar_de_novo_um_lote_ja_gravado_nao_chama_a_API_nem_muda_nada()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Finish(state!.Id);
        await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken);
        var gets = _client.Gets;

        var again = await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken);

        Assert.False(again.JustApplied);
        Assert.Null(again.Info);
        Assert.Equal(gets, _client.Gets);
        Assert.Equal(44, again.State.Outcome!.Translated);
    }

    [Fact]
    public async Task A_gravacao_deixa_um_ponto_de_restauracao_e_o_Desfazer_devolve_as_linhas()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Finish(state!.Id);

        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        var outcome = applied.Outcome!;
        Assert.NotNull(outcome.RestorePointId);
        Assert.Equal(44, outcome.RestorePointLines);
        var store = new RestorePointStore(_project, _clock);
        Assert.Equal("Tradução noturna · O capítulo", store.Find(outcome.RestorePointId)!.Title);
        var undone = store.Undo(outcome.RestorePointId);
        Assert.Equal(44, undone.Restored);
        Assert.All(_project.LoadText(Key).Take(44), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
    }

    [Fact]
    public async Task Linha_que_a_pessoa_traduziu_enquanto_o_lote_rodava_fica_como_ela_deixou()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        new LineEditor(_project, _clock).SetTranslation(Key, Id(1), "à mão");
        _client.Finish(state!.Id);

        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        Assert.Equal(43, applied.Outcome!.Translated);
        Assert.Equal(1, applied.Outcome.Skipped);
        Assert.Equal("à mão", _project.LoadText(Key)[1].Translation);
        Assert.Equal("[PT] 原文2", _project.LoadText(Key)[2].Translation);
    }

    [Fact]
    public async Task Falha_ao_enviar_nao_deixa_estado_no_projeto()
    {
        _client.CreateFails = new HttpRequestException("sem rede");

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().SubmitAsync(Order(), TestContext.Current.CancellationToken));

        Assert.Empty(new NightBatchStore(_project).List());
    }

    [Fact]
    public async Task Se_o_estado_nao_puder_ser_guardado_o_lote_e_cancelado_na_API()
    {
        // Um arquivo no lugar da pasta faz o Save falhar depois de o lote já existir na API.
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(Path.Combine(_project.CacheDirectory, "lote-noturno"), "no lugar da pasta");

        var error = await Assert.ThrowsAsync<NightBatchException>(() => Service().SubmitAsync(Order(), TestContext.Current.CancellationToken));

        Assert.Contains("foi cancelado na API", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, _client.Creates);
        Assert.Equal(1, _client.Cancels);
    }
}
