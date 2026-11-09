using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Bible;

/// <summary>O resumo da cena (#530) entra em cada pedido de tradução das linhas dela.</summary>
public sealed partial class SceneSummaryInRequestTests
{
    [System.Text.RegularExpressions.GeneratedRegex("\"id\":\"([^\"]+)\"")]
    private static partial System.Text.RegularExpressions.Regex RequestId();

    private sealed class StubClient : IClaudeMessageClient
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public Task<ClaudeReply> CompleteAsync(ClaudeRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var ids = RequestId().Matches(request.User).Select(m => m.Groups[1].Value).Distinct();
            var items = string.Join(",", ids.Select(id => $"{{\"id\":\"{id}\",\"translation\":\"T-{id}\"}}"));
            return Task.FromResult(new ClaudeReply("end_turn", $"{{\"translations\":[{items}]}}"));
        }
    }

    private static TextUnit Unit(string id, string source) =>
        new(id, TextUnitKind.Dialogue, null, source, null, TranslationStatus.Untranslated);

    private static GameBible Bible() => GameBible.From(
    [
        new BibleScene("yst1")
        {
            Name = "Atraso no portão",
            Summary = "Kenta e Ren chegam atrasados e o professor os espera.",
            Tone = ["cômico leve", "tenso no fim"],
            Jokes = ["o guarda-chuva de Ren"],
            Relations = [new BibleRelation("Kenta e Ren", "Amigos de infância")],
        },
        new BibleScene("yst2") { Summary = "Intervalo no pátio." },
        new BibleScene("yst3"),
    ]) with
    {
        Synopsis = "Uma cidade onde sempre chove.",
    };

    private static TranslationBatchBuilder Builder(GameBible? bible) => new([], new CharacterBook([])) { Bible = bible! };

    private static async Task<ClaudeRequest> Translate(TranslationBatch batch)
    {
        var client = new StubClient();
        await new ClaudeTranslationProvider(client).TranslateAsync(batch, TestContext.Current.CancellationToken);
        return Assert.Single(client.Requests);
    }

    [Fact]
    public void O_lote_leva_a_cena_das_linhas_e_a_sinopse_do_jogo()
    {
        var batch = Builder(Bible()).Build([Unit("yst1:000001", "遅いぞ"), Unit("yst1:000002", "ごめん")]);

        var scene = Assert.Single(batch.Scenes);
        Assert.Equal("yst1", scene.Key);
        Assert.Equal("Uma cidade onde sempre chove.", batch.Synopsis);
    }

    [Fact]
    public void Linhas_de_duas_cenas_levam_as_duas_na_ordem_em_que_aparecem()
    {
        var batch = Builder(Bible()).Build([Unit("yst2:000001", "a"), Unit("yst1:000001", "b"), Unit("yst2:000002", "c")]);

        Assert.Equal(["yst2", "yst1"], batch.Scenes.Select(s => s.Key));
    }

    [Fact]
    public void Cena_sem_conteudo_ou_fora_da_biblia_nao_entra()
    {
        var batch = Builder(Bible()).Build([Unit("yst3:000001", "a"), Unit("yst9:000001", "b")]);

        Assert.Empty(batch.Scenes);
    }

    [Fact]
    public void Sem_biblia_o_lote_e_o_de_antes()
    {
        var batch = Builder(null).Build([Unit("yst1:000001", "a")]);

        Assert.Empty(batch.Scenes);
        Assert.Equal(string.Empty, batch.Synopsis);
    }

    [Fact]
    public async Task O_resumo_o_tom_as_piadas_e_as_relacoes_vao_no_bloco_do_lote_sem_cache()
    {
        var batch = Builder(Bible()).Build([Unit("yst1:000001", "遅いぞ")]);

        var request = await Translate(batch);

        Assert.Contains("Kenta e Ren chegam atrasados e o professor os espera.", request.BatchContext, StringComparison.Ordinal);
        Assert.Contains("Atraso no portão", request.BatchContext, StringComparison.Ordinal);
        Assert.Contains("linhas com id yst1:", request.BatchContext, StringComparison.Ordinal);
        Assert.Contains("Tom: cômico leve; tenso no fim", request.BatchContext, StringComparison.Ordinal);
        Assert.Contains("o guarda-chuva de Ren", request.BatchContext, StringComparison.Ordinal);
        Assert.Contains("Kenta e Ren: Amigos de infância", request.BatchContext, StringComparison.Ordinal);
        Assert.DoesNotContain("Kenta e Ren chegam", request.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sinopse_vai_no_bloco_estavel_que_fica_no_cache()
    {
        var request = await Translate(Builder(Bible()).Build([Unit("yst1:000001", "遅いぞ")]));

        Assert.Contains("Uma cidade onde sempre chove.", request.System, StringComparison.Ordinal);
        Assert.DoesNotContain("Uma cidade onde sempre chove.", request.BatchContext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_bloco_estavel_nao_muda_de_uma_cena_para_outra()
    {
        var first = await Translate(Builder(Bible()).Build([Unit("yst1:000001", "a")]));
        var second = await Translate(Builder(Bible()).Build([Unit("yst2:000001", "b")]));

        Assert.Equal(first.System, second.System);
        Assert.NotEqual(first.BatchContext, second.BatchContext);
    }

    [Fact]
    public async Task Sem_biblia_o_pedido_nao_tem_secao_de_cena()
    {
        var request = await Translate(Builder(null).Build([Unit("yst1:000001", "a")]));

        Assert.DoesNotContain("Cena", request.FullSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("Sinopse", request.FullSystem, StringComparison.Ordinal);
    }

    [Fact]
    public void ForLines_mantem_a_cena_e_a_sinopse()
    {
        var batch = Builder(Bible()).Build([Unit("yst1:000001", "a"), Unit("yst1:000002", "b")]);

        var single = batch.ForLines([batch.Lines[0]]);

        Assert.Equal(batch.Scenes, single.Scenes);
        Assert.Equal(batch.Synopsis, single.Synopsis);
    }

    private static TranslationBatch BatchWithWindow(GameBible? bible, int before, int after) =>
        new TranslationBatchBuilder([], new CharacterBook([]), maxPreviousLines: before, maxNextLines: after) { Bible = bible! }.Build([Unit("yst1:000001", "a")]);

    [Fact]
    public void A_chave_do_cache_leva_a_janela_estreita_e_as_cenas_ao_mesmo_tempo()
    {
        var standard = TranslationCache.ContextFor("m", BatchWithWindow(Bible(), 2, 2));
        var narrow = TranslationCache.ContextFor("m", BatchWithWindow(Bible(), 1, 1));
        var narrowEdited = TranslationCache.ContextFor("m", BatchWithWindow(Bible().WithScene(Bible().Scene("yst1")!.EditSummary("Outro resumo.")), 1, 1));

        Assert.NotEqual(standard, narrow);
        Assert.NotEqual(narrow, narrowEdited);
        Assert.Contains("janela:1/1", narrow, StringComparison.Ordinal);
        Assert.Contains("Kenta e Ren chegam atrasados", narrow, StringComparison.Ordinal);
    }

    [Fact]
    public void Sem_biblia_a_janela_padrao_mantem_a_chave_de_antes_e_a_estreita_a_muda()
    {
        var plain = TranslationCache.ContextFor("m", new TranslationBatch([Unit("yst1:000001", "a")]));

        Assert.Equal(plain, TranslationCache.ContextFor("m", BatchWithWindow(null, 2, 2)));
        Assert.NotEqual(plain, TranslationCache.ContextFor("m", BatchWithWindow(null, 1, 1)));
    }

    [Fact]
    public void ForLines_mantem_a_janela_estreita_junto_das_cenas()
    {
        var batch = new TranslationBatchBuilder([], new CharacterBook([]), maxPreviousLines: 1, maxNextLines: 3) { Bible = Bible() }
            .Build([Unit("yst1:000001", "a"), Unit("yst1:000002", "b")]);

        var single = batch.ForLines([batch.Lines[0]]);

        Assert.Equal(new ContextWindow(1, 3), single.Window);
        Assert.Equal(batch.Scenes, single.Scenes);
        Assert.Equal(TranslationCache.ContextFor("m", batch), TranslationCache.ContextFor("m", single));
    }

    [Fact]
    public void O_cache_de_traducoes_muda_quando_o_resumo_da_cena_muda_mas_nao_para_um_lote_de_outra_cena()
    {
        var before = Builder(Bible()).Build([Unit("yst1:000001", "a")]);
        var edited = Bible().WithScene(Bible().Scene("yst1")!.EditSummary("Outro resumo."));
        var after = Builder(edited).Build([Unit("yst1:000001", "a")]);
        var other = Builder(edited).Build([Unit("yst2:000001", "a")]);
        var otherBefore = Builder(Bible()).Build([Unit("yst2:000001", "a")]);

        Assert.NotEqual(TranslationCache.ContextFor("m", before), TranslationCache.ContextFor("m", after));
        Assert.Equal(TranslationCache.ContextFor("m", otherBefore), TranslationCache.ContextFor("m", other));
    }

    [Fact]
    public void Sem_cena_a_chave_do_cache_e_a_de_antes()
    {
        var withoutBible = Builder(null).Build([Unit("yst1:000001", "a")]);
        var plain = new TranslationBatch([Unit("yst1:000001", "a")]);

        Assert.Equal(TranslationCache.ContextFor("m", plain), TranslationCache.ContextFor("m", withoutBible));
    }

    [Fact]
    public void A_estimativa_de_custo_conta_o_resumo_que_vai_junto()
    {
        var with = Builder(Bible()).Build([Unit("yst1:000001", "遅いぞ")]);
        var without = Builder(null).Build([Unit("yst1:000001", "遅いぞ")]);

        var costWith = CostEstimator.EstimateBatch(with, ClaudeModels.Opus, ClaudeEffort.Medium);
        var costWithout = CostEstimator.EstimateBatch(without, ClaudeModels.Opus, ClaudeEffort.Medium);

        Assert.True(costWith.Usage.Input > costWithout.Usage.Input);
    }
}
