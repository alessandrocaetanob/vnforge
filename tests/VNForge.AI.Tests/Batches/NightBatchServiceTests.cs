using Microsoft.Extensions.Time.Testing;
using VNForge.AI.Batches;
using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Batches;

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
    public async Task Sem_linha_pendente_nao_envia_nada()
    {
        _project.SaveText(Key, [Line(0, TranslationStatus.Approved)]);

        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);

        Assert.Null(state);
        Assert.Equal(0, _client.Creates);
        Assert.Empty(new NightBatchStore(_project).List());
    }

    [Fact]
    public async Task Enquanto_o_lote_roda_a_consulta_so_informa_o_andamento_e_nao_grava()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);

        var check = await Service().CheckAsync(state!.Id, TestContext.Current.CancellationToken);

        Assert.False(check.JustApplied);
        Assert.Equal(MessageBatchStatus.InProgress, check.Info!.Status);
        Assert.Equal(0, check.Info.Done);
        Assert.Equal(NightBatchPhase.Processing, check.State.Phase);
        Assert.All(_project.LoadText(Key).Take(44), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
    }

    [Fact]
    public async Task Outra_instancia_retoma_o_lote_do_disco_e_grava_o_resultado_como_da_IA()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Finish(state!.Id);
        _clock.Advance(TimeSpan.FromHours(5));

        var resumed = new NightBatchService(_client, ProjectFolder.Open(_project.Root), _clock);
        var check = await resumed.CheckAsync(state.Id, TestContext.Current.CancellationToken);

        Assert.True(check.JustApplied);
        Assert.Equal(NightBatchPhase.Applied, check.State.Phase);
        Assert.Equal(44, check.State.Outcome!.Translated);
        var units = _project.LoadText(Key);
        Assert.Equal("[PT] 原文0", units[0].Translation);
        Assert.Equal(TranslationStatus.Machine, units[0].Status);
        Assert.Equal("anthropic", units[0].Origin!.Provider);
        Assert.Equal(ClaudeModels.Opus, units[0].Origin!.Model);
        Assert.Equal(_clock.GetUtcNow(), units[0].Origin!.At);
        Assert.Equal("aprovada 44", units[44].Translation);
        Assert.Empty(resumed.Open());
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
    public async Task Resposta_que_nao_traz_os_ids_do_pedido_conta_como_erro_e_nao_grava()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Reply = _ => new ClaudeReply("end_turn", """{"translations":[{"id":"outro","translation":"x"}]}""", new ClaudeUsage(10, 5, 0, 0));
        _client.Finish(state!.Id);

        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        Assert.Equal(0, applied.Outcome!.Translated);
        Assert.Equal(44, applied.Outcome.Failed);
        Assert.Null(applied.Outcome.RestorePointId);
    }

    [Fact]
    public async Task Cancelar_marca_o_estado_e_o_que_nao_voltou_fica_sem_traducao()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);

        var canceling = await Service().CancelAsync(state!.Id, TestContext.Current.CancellationToken);
        _client.Finish(state.Id);
        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        Assert.Equal(NightBatchPhase.Canceling, canceling.Phase);
        Assert.Equal(1, _client.Cancels);
        Assert.Equal(NightBatchPhase.Applied, applied.Phase);
        Assert.Equal(0, applied.Outcome!.Translated);
        Assert.Equal(44, applied.Outcome.Failed);
    }

    [Fact]
    public async Task Lote_de_outro_projeto_ou_inexistente_diz_isso_em_portugues()
    {
        var error = await Assert.ThrowsAsync<NightBatchException>(() => Service().CheckAsync("msgbatch_nao_existe", TestContext.Current.CancellationToken));

        Assert.Contains("não está neste projeto", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Falha_ao_enviar_nao_deixa_estado_no_projeto()
    {
        _client.CreateFails = new HttpRequestException("sem rede");

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().SubmitAsync(Order(), TestContext.Current.CancellationToken));

        Assert.Empty(new NightBatchStore(_project).List());
    }

    [Fact]
    public async Task Resposta_que_parou_por_recusa_marca_as_linhas_como_recusadas_e_nao_como_erro()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Reply = _ => new ClaudeReply("refusal", string.Empty, new ClaudeUsage(10, 0, 0, 0));
        _client.Finish(state!.Id);

        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        Assert.Equal(0, applied.Outcome!.Translated);
        Assert.Equal(44, applied.Outcome.Refused);
        Assert.Equal(0, applied.Outcome.Failed);
        Assert.Equal(44, applied.Outcome.UnfinishedIds.Count);
        Assert.Null(applied.Outcome.RestorePointId);
        Assert.All(_project.LoadText(Key).Take(44), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
        Assert.True(applied.Outcome.Dollars > 0m);
    }

    [Fact]
    public async Task Resposta_cortada_por_max_tokens_conta_como_erro_e_ainda_cobra_os_tokens()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Reply = _ => new ClaudeReply("max_tokens", """{"translations":[{"id":"x","trans""", new ClaudeUsage(1000, 500, 0, 0));
        _client.Finish(state!.Id);

        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        Assert.Equal(0, applied.Outcome!.Translated);
        Assert.Equal(44, applied.Outcome.Failed);
        Assert.Equal(0, applied.Outcome.Refused);
        Assert.Equal(0.021m, applied.Outcome.Dollars);
        Assert.All(_project.LoadText(Key).Take(44), u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
    }

    [Theory]
    [InlineData(MessageBatchOutcome.Errored)]
    [InlineData(MessageBatchOutcome.Expired)]
    [InlineData(MessageBatchOutcome.Canceled)]
    public async Task Pedido_errored_expired_ou_canceled_deixa_as_linhas_dele_sem_traducao_e_o_resto_e_gravado(MessageBatchOutcome outcome)
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.OutcomeOf = item => item.CustomId == "r00003" ? outcome : MessageBatchOutcome.Succeeded;
        _client.Finish(state!.Id);

        var applied = (await Service().CheckAsync(state.Id, TestContext.Current.CancellationToken)).State;

        Assert.Equal(40, applied.Outcome!.Translated);
        Assert.Equal(4, applied.Outcome.Failed);
        Assert.Equal(Enumerable.Range(40, 4).Select(Id), applied.Outcome.UnfinishedIds);
        var units = _project.LoadText(Key);
        Assert.Equal(TranslationStatus.Machine, units[39].Status);
        Assert.Equal(TranslationStatus.Untranslated, units[40].Status);
        Assert.Equal(NightBatchPhase.Applied, applied.Phase);
        Assert.NotNull(applied.Outcome.RestorePointId);
    }

    [Fact]
    public async Task Dois_processos_vendo_o_lote_terminado_gravam_uma_vez_so()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Finish(state!.Id);

        var checks = await Task.WhenAll(
            Task.Run(() => Service().CheckAsync(state.Id, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken),
            Task.Run(() => Service().CheckAsync(state.Id, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));

        Assert.Equal(1, checks.Count(c => c.JustApplied));
        var saved = new NightBatchStore(_project).Find(state.Id)!;
        Assert.Equal(NightBatchPhase.Applied, saved.Phase);
        Assert.Equal(44, saved.Outcome!.Translated);
        Assert.NotNull(saved.Outcome.RestorePointId);
        Assert.Equal(0.021m, AiUsageLog.Load(_project).Dollars);
        Assert.Single(new RestorePointStore(_project, _clock).List());
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

    [Fact]
    public async Task Se_o_estado_nao_puder_ser_guardado_e_o_cancelamento_tambem_falha_a_mensagem_diz_como_cancelar()
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(Path.Combine(_project.CacheDirectory, "lote-noturno"), "no lugar da pasta");
        _client.CancelFails = new HttpRequestException("sem rede");

        var error = await Assert.ThrowsAsync<NightBatchException>(() => Service().SubmitAsync(Order(), TestContext.Current.CancellationToken));

        Assert.DoesNotContain("nada será cobrado", error.Message, StringComparison.Ordinal);
        Assert.Contains("pode seguir rodando na API", error.Message, StringComparison.Ordinal);
        Assert.Contains("vnf translate --cancel-batch msgbatch_fake0001", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelar_um_lote_que_a_API_ja_terminou_nao_chama_o_cancelamento_e_deixa_o_estado_aberto()
    {
        var state = await Service().SubmitAsync(Order(), TestContext.Current.CancellationToken);
        _client.Finish(state!.Id);

        var after = await Service().CancelAsync(state.Id, TestContext.Current.CancellationToken);

        Assert.Equal(0, _client.Cancels);
        Assert.Equal(NightBatchPhase.Processing, after.Phase);
        Assert.True(after.IsOpen);
    }
}
