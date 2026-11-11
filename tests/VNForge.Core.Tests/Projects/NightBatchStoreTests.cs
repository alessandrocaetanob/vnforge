using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class NightBatchStoreTests : IDisposable
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 8, 23, 41, 0, TimeSpan.Zero);

    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;
    private readonly NightBatchStore _store;

    public NightBatchStoreTests()
    {
        _project = ProjectFolder.Create(Path.Combine(_temp.Path, "projeto"), new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _store = new NightBatchStore(_project);
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Linha(int n, string? traducao = null, TranslationStatus status = TranslationStatus.Untranslated) =>
        new($"yst00001:{n:D6}", TextUnitKind.Dialogue, null, $"原文{n}", traducao, status);

    private static NightBatchState Lote(string id, DateTimeOffset at, params TextUnit[] lines) =>
        new(id, "O capítulo", "claude-opus-5-5", "Careful", "Medium", at, 13.46m, ["yst00001"], [new NightBatchRequest("r00001", [.. lines.Select(NightBatchLine.Of)])]);

    [Fact]
    public void O_resultado_gravado_volta_na_leitura_e_fecha_o_lote()
    {
        var applied = Lote("msgbatch_01", Agora, Linha(0)) with
        {
            Phase = NightBatchPhase.Applied,
            EndedAt = Agora.AddHours(5),
            Outcome = new NightBatchOutcome(1, 2, 3, 4, "ponto-1", 1, 0.5m, ["yst00001:000009"]),
        };
        _store.Save(applied);

        var read = _store.Find("msgbatch_01")!;

        Assert.False(read.IsOpen);
        Assert.Equal(Agora.AddHours(5), read.EndedAt);
        Assert.Equal(new NightBatchOutcome(1, 2, 3, 4, "ponto-1", 1, 0.5m, ["yst00001:000009"]).Failed, read.Outcome!.Failed);
        Assert.Equal(["yst00001:000009"], read.Outcome.UnfinishedIds);
        Assert.Equal("ponto-1", read.Outcome.RestorePointId);
    }

    [Fact]
    public void Lista_do_mais_novo_para_o_mais_antigo_e_Open_so_traz_os_que_esperam()
    {
        _store.Save(Lote("msgbatch_a", Agora, Linha(0)));
        _store.Save(Lote("msgbatch_b", Agora.AddDays(1), Linha(1)) with { Phase = NightBatchPhase.Applied });
        _store.Save(Lote("msgbatch_c", Agora.AddDays(2), Linha(2)) with { Phase = NightBatchPhase.Canceling });

        Assert.Equal(["msgbatch_c", "msgbatch_b", "msgbatch_a"], _store.List().Select(s => s.Id));
        Assert.Equal(["msgbatch_c", "msgbatch_a"], _store.Open().Select(s => s.Id));
    }

    [Fact]
    public void Arquivo_ilegivel_fica_de_fora_sem_derrubar_a_lista()
    {
        _store.Save(Lote("msgbatch_a", Agora, Linha(0)));
        File.WriteAllText(Path.Combine(_store.StoreDirectory, "quebrado.json"), "{ isto não é json");

        Assert.Equal(["msgbatch_a"], _store.List().Select(s => s.Id));
        Assert.Null(_store.Find("quebrado"));
    }

    [Fact]
    public void Sem_a_pasta_nao_ha_lote()
    {
        Assert.Empty(_store.List());
        Assert.Null(_store.Find("msgbatch_x"));
    }

    [Fact]
    public void A_linha_so_confere_se_continua_como_no_envio()
    {
        var sent = NightBatchLine.Of(Linha(0));

        Assert.True(sent.Matches(Linha(0)));
        Assert.False(sent.Matches(Linha(0, "à mão", TranslationStatus.Translated)));
        Assert.False(sent.Matches(Linha(0) with { Source = "outro original" }));
    }

    [Fact]
    public void A_previa_do_desfazer_conta_o_que_voltaria_sem_gravar_nada()
    {
        _project.SaveText("yst00001", [Linha(0), Linha(1)]);
        var clock = new FakeTimeProvider(Agora);
        var recorder = RestoreScope.TryBegin(_project, "Tradução noturna", null, RestorePointStore.IsAiWrite, clock);
        var origin = new TranslationOrigin("anthropic", "m", Agora);
        _project.SaveText("yst00001", [Linha(0) with { Translation = "zero", Status = TranslationStatus.Machine, Origin = origin }, Linha(1) with { Translation = "um", Status = TranslationStatus.Machine, Origin = origin }]);
        var point = RestoreScope.TryComplete(recorder)!;
        new LineEditor(_project, clock).SetTranslation("yst00001", "yst00001:000001", "à mão");
        var store = new RestorePointStore(_project, clock);

        var preview = store.Preview(point.Id);

        Assert.Equal(1, preview.Restored);
        Assert.Equal(1, preview.EditedSince);
        Assert.Equal("zero", _project.LoadText("yst00001")[0].Translation);
        Assert.NotNull(store.Find(point.Id));
    }

    [Fact]
    public void Sem_gravador_ou_sem_mudanca_o_escopo_nao_devolve_ponto()
    {
        _project.SaveText("yst00001", [Linha(0)]);

        Assert.Null(RestoreScope.TryComplete(null));
        Assert.Null(RestoreScope.TryComplete(RestoreScope.TryBegin(_project, "Nada mudou")));
    }
}
