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
    public void Cena_sem_conteudo_ou_fora_da_biblia_nao_entra()
    {
        var batch = Builder(Bible()).Build([Unit("yst3:000001", "a"), Unit("yst9:000001", "b")]);

        Assert.Empty(batch.Scenes);
    }

    [Fact]
    public async Task O_bloco_estavel_nao_muda_de_uma_cena_para_outra()
    {
        var first = await Translate(Builder(Bible()).Build([Unit("yst1:000001", "a")]));
        var second = await Translate(Builder(Bible()).Build([Unit("yst2:000001", "b")]));

        Assert.Equal(first.System, second.System);
        Assert.NotEqual(first.BatchContext, second.BatchContext);
    }

    private static TranslationBatch BatchWithWindow(GameBible? bible, int before, int after) =>
        new TranslationBatchBuilder([], new CharacterBook([]), maxPreviousLines: before, maxNextLines: after) { Bible = bible! }.Build([Unit("yst1:000001", "a")]);

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
