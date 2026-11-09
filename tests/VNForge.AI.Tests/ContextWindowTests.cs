using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.AI.Translation;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests;

public sealed partial class ContextWindowTests
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));

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

    // 20 falas já traduzidas, 3 pendentes (20 a 22) e 20 seguintes sem tradução.
    private static TextUnit[] Script() =>
    [
        .. Enumerable.Range(0, 43).Select(i => new TextUnit(
            $"s:{i:D3}", TextUnitKind.Dialogue, "ハル", $"原文{i:D3}", i < 20 ? $"Tradução {i:D3}" : null,
            i < 20 ? TranslationStatus.Translated : TranslationStatus.Untranslated)),
    ];

    private static LineSelection SelectPending(TextUnit[] script) =>
        LineSelection.Build(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["s"] = script },
            null,
            null,
            u => string.CompareOrdinal(u.Id, "s:020") >= 0 && string.CompareOrdinal(u.Id, "s:022") <= 0);

    private static TranslationBatchBuilder Builder(int before, int after) =>
        new([], new CharacterBook([]), maxPreviousLines: before, maxNextLines: after);

    [Fact]
    public void O_padrao_continua_duas_falas_antes_e_duas_depois()
    {
        var selection = SelectPending(Script());

        var batch = selection.BuildBatch(new TranslationBatchBuilder([], new CharacterBook([])), selection.Pending);

        Assert.Equal(["s:018", "s:019"], batch.PreviousLines.Select(u => u.Id));
        Assert.Equal(["s:023", "s:024"], batch.NextLines.Select(u => u.Id));
    }

    [Fact]
    public void A_selecao_entrega_a_janela_configurada_inteira()
    {
        var selection = SelectPending(Script());

        var batch = selection.BuildBatch(Builder(15, 15), selection.Pending);

        Assert.Equal(15, batch.PreviousLines.Count);
        Assert.Equal("s:005", batch.PreviousLines[0].Id);
        Assert.Equal("s:019", batch.PreviousLines[^1].Id);
        Assert.Equal(15, batch.NextLines.Count);
        Assert.Equal("s:023", batch.NextLines[0].Id);
        Assert.Equal("s:037", batch.NextLines[^1].Id);
    }

    [Fact]
    public void A_janela_maxima_de_30_cabe_na_selecao()
    {
        var script = Script();
        var batch = SelectPending(script).BuildBatch(Builder(30, 30), script.Skip(20).Take(3).ToList());

        Assert.Equal(20, batch.PreviousLines.Count);
        Assert.Equal(20, batch.NextLines.Count);
    }

    [Fact]
    public void O_estimador_conta_as_falas_de_contexto_a_mais_no_preco()
    {
        var script = Script();
        var selection = SelectPending(script);

        CostEstimate Estimate(int window) =>
            new RequestEstimator(selection, Builder(window, window), ClaudeModels.Opus, ClaudeEffort.Medium, 30, Rate).Estimate(selection.Pending);

        var small = Estimate(2);
        var large = Estimate(15);

        Assert.True(large.Usage.Input > small.Usage.Input);
        Assert.True(large.Dollars > small.Dollars);
        Assert.Equal(small.Usage.Output, large.Usage.Output);
    }

    [Fact]
    public async Task O_bloco_estavel_do_prompt_e_igual_entre_janelas_e_o_contexto_vai_fora_do_cache()
    {
        var script = Script();
        var selection = SelectPending(script);
        var client = new StubClient();
        var provider = new ClaudeTranslationProvider(client);

        await provider.TranslateAsync(selection.BuildBatch(Builder(2, 2), selection.Pending), CancellationToken.None);
        await provider.TranslateAsync(selection.BuildBatch(Builder(15, 12), selection.Pending), CancellationToken.None);

        var (small, large) = (client.Requests[0], client.Requests[1]);

        // O bloco que recebe cache_control (System) não muda com a janela.
        Assert.Equal(small.System, large.System);
        Assert.Equal(small.BatchContext, large.BatchContext);

        // A janela maior só aparece na mensagem do usuário, que fica fora do cache.
        Assert.DoesNotContain("原文", large.System);
        Assert.Contains("原文019", small.User);
        Assert.DoesNotContain("原文010", small.User);
        Assert.Contains("原文010", large.User);
        Assert.Contains("原文034", large.User);
        Assert.DoesNotContain("原文034", small.User);
    }

    [Fact]
    public void Com_o_padrao_a_chave_do_cache_e_a_de_antes_da_janela_e_com_outra_janela_ela_muda()
    {
        var script = Script();
        var selection = SelectPending(script);
        var pending = selection.Pending;

        string Context(int before, int after) => TranslationCache.ContextFor("claude-opus-5-5", selection.BuildBatch(Builder(before, after), pending));
        string Key(int before, int after) => TranslationCache.KeyFor(script[20], Context(before, after));

        var antes = "claude-opus-5-5" + "\u001e" + string.Empty + "\u001e" + StyleConfig.Default.CacheKey();
        Assert.Equal(antes, Context(2, 2));
        Assert.NotEqual(Key(2, 2), Key(15, 2));
        Assert.NotEqual(Key(2, 2), Key(2, 15));
        Assert.NotEqual(Key(15, 2), Key(2, 15));
        Assert.Equal(Key(15, 2), Key(15, 2));
    }

    [Fact]
    public void Na_borda_do_script_a_janela_grande_traz_so_o_que_existe()
    {
        var script = Script();
        var selection = LineSelection.Build(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["s"] = script }, null, null, _ => true);
        var builder = Builder(30, 30);

        var first = selection.BuildBatch(builder, [script[0]]);
        var last = selection.BuildBatch(builder, [script[^1]]);

        Assert.Empty(first.PreviousLines);
        Assert.Equal(30, first.NextLines.Count);
        Assert.Equal("s:001", first.NextLines[0].Id);
        Assert.Equal(20, last.PreviousLines.Count);
        Assert.Empty(last.NextLines);
        Assert.Equal("s:019", last.PreviousLines[^1].Id);
    }

    [Fact]
    public void Falas_sem_traducao_intercaladas_nao_esvaziam_a_janela_anterior()
    {
        var script = Enumerable.Range(0, 62).Select(i => new TextUnit(
            $"s:{i:D3}", TextUnitKind.Dialogue, "ハル", $"原文{i:D3}", i < 60 && i % 2 == 0 ? $"Tradução {i:D3}" : null,
            i < 60 && i % 2 == 0 ? TranslationStatus.Translated : TranslationStatus.Untranslated)).ToArray();
        var selection = LineSelection.Build(
            new Dictionary<string, IReadOnlyList<TextUnit>> { ["s"] = script }, null, null, u => u.Id is "s:060" or "s:061");

        var batch = selection.BuildBatch(Builder(30, 0), selection.Pending);

        Assert.Equal(30, batch.PreviousLines.Count);
        Assert.Equal("s:000", batch.PreviousLines[0].Id);
        Assert.Equal("s:058", batch.PreviousLines[^1].Id);
    }
}
