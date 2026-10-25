using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

public class TranslationQuoterTests
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));

    private static TextUnit Line(string id, string source = "こんにちは、世界のみなさん", TranslationStatus status = TranslationStatus.Untranslated) =>
        new(id, TextUnitKind.Dialogue, null, source, status == TranslationStatus.Untranslated ? null : "ok", status);

    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts(int scripts, int linesEach)
    {
        var result = new Dictionary<string, IReadOnlyList<TextUnit>>(StringComparer.Ordinal);
        for (var s = 1; s <= scripts; s++)
        {
            var key = $"yst{s:D5}";
            result[key] = [.. Enumerable.Range(0, linesEach).Select(i => Line($"{key}:{i:D6}"))];
        }

        return result;
    }

    [Fact]
    public void Esta_cena_so_conta_o_script_aberto()
    {
        var keys = Texts(5, 3).Keys.Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["yst00003"], TranslationQuoter.KeysFor(TranslationScope.Scene, keys, "yst00003"));
    }

    [Fact]
    public void O_jogo_todo_conta_todos_os_scripts_em_ordem()
    {
        var keys = new[] { "b", "a", "c" };

        Assert.Equal(["a", "b", "c"], TranslationQuoter.KeysFor(TranslationScope.Game, keys, "a"));
    }

    [Fact]
    public void O_capitulo_e_a_faixa_de_scripts_da_cena_aberta()
    {
        var keys = Enumerable.Range(1, (TranslationQuoter.ChapterSize * 2) + 5).Select(i => $"yst{i:D5}").ToList();

        var chapter = TranslationQuoter.KeysFor(TranslationScope.Chapter, keys, "yst00023");

        Assert.Equal(TranslationQuoter.ChapterSize, chapter.Count);
        Assert.Equal("yst00021", chapter[0]);
        Assert.Contains("yst00023", chapter);

        var last = TranslationQuoter.KeysFor(TranslationScope.Chapter, keys, "yst00045");
        Assert.Equal(5, last.Count);
    }

    [Fact]
    public void Sem_cena_aberta_o_capitulo_e_a_cena_ficam_vazios()
    {
        var keys = new[] { "a", "b" };

        Assert.Empty(TranslationQuoter.KeysFor(TranslationScope.Scene, keys, null));
        Assert.Empty(TranslationQuoter.KeysFor(TranslationScope.Chapter, keys, "outra"));
    }

    [Fact]
    public void So_as_linhas_sem_traducao_e_com_original_entram()
    {
        var texts = new Dictionary<string, IReadOnlyList<TextUnit>>
        {
            ["a"] = [Line("a:1"), Line("a:2", status: TranslationStatus.Machine), Line("a:3", source: "  "), Line("a:4", status: TranslationStatus.Approved)],
            ["b"] = [Line("b:1")],
        };

        var pending = TranslationQuoter.PendingLines(texts, ["a"]);

        Assert.Equal(["a:1"], pending.Select(l => l.Id));
    }

    [Fact]
    public void O_custo_sobe_da_rapida_para_a_caprichada_e_o_tempo_tambem()
    {
        var lines = TranslationQuoter.PendingLines(Texts(1, 400), ["yst00001"]);

        var quotes = TranslationQuoter.Quote(lines, Rate);

        Assert.Equal(400, quotes.Lines);
        Assert.Equal([TranslationQuality.Fast, TranslationQuality.Balanced, TranslationQuality.Careful], quotes.Qualities.Select(q => q.Quality));
        Assert.True(quotes.For(TranslationQuality.Fast).Brl < quotes.For(TranslationQuality.Balanced).Brl);
        Assert.True(quotes.For(TranslationQuality.Balanced).Brl < quotes.For(TranslationQuality.Careful).Brl);
        Assert.True(quotes.For(TranslationQuality.Fast).Time < quotes.For(TranslationQuality.Careful).Time);
        Assert.Equal(QualityProfiles.For(TranslationQuality.Careful).TimeFor(400), quotes.For(TranslationQuality.Careful).Time);
    }

    [Fact]
    public void O_valor_em_reais_segue_a_cotacao_informada()
    {
        var lines = TranslationQuoter.PendingLines(Texts(1, 100), ["yst00001"]);

        var at5 = TranslationQuoter.Quote(lines, Rate).For(TranslationQuality.Balanced);
        var at10 = TranslationQuoter.Quote(lines, new ExchangeRate(10m, new DateOnly(2026, 10, 25))).For(TranslationQuality.Balanced);

        Assert.Equal(at5.Quote.Dollars, at10.Quote.Dollars);
        Assert.Equal(at5.Brl * 2m, at10.Brl);
    }

    [Fact]
    public void Escopo_sem_linhas_pendentes_custa_zero()
    {
        var quotes = TranslationQuoter.Quote([], Rate);

        Assert.Equal(0, quotes.Lines);
        Assert.All(quotes.Qualities, q => Assert.Equal(0m, q.Brl));
        Assert.All(quotes.Qualities, q => Assert.Equal(TimeSpan.Zero, q.Time));
    }

    [Fact]
    public void A_qualidade_que_cabe_e_a_mais_cara_dentro_do_que_sobra_do_limite()
    {
        var lines = TranslationQuoter.PendingLines(Texts(1, 400), ["yst00001"]);
        var quotes = TranslationQuoter.Quote(lines, Rate);
        var fast = quotes.For(TranslationQuality.Fast).Brl;
        var balanced = quotes.For(TranslationQuality.Balanced).Brl;
        var careful = quotes.For(TranslationQuality.Careful).Brl;

        Assert.Equal(TranslationQuality.Careful, quotes.MostCarefulThatFits(spent: 10m, limit: 10m + careful)?.Quality);
        Assert.Equal(TranslationQuality.Balanced, quotes.MostCarefulThatFits(spent: 10m, limit: 10m + careful - 0.01m)?.Quality);
        Assert.Equal(TranslationQuality.Fast, quotes.MostCarefulThatFits(spent: 10m, limit: 10m + balanced - 0.01m)?.Quality);
        Assert.Null(quotes.MostCarefulThatFits(spent: 10m, limit: 10m + fast - 0.01m));
    }

    [Fact]
    public void Sem_limite_a_mais_cuidadosa_sempre_cabe()
    {
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(Texts(1, 50), ["yst00001"]), Rate);

        Assert.Equal(TranslationQuality.Careful, quotes.MostCarefulThatFits(spent: 99999m, limit: null)?.Quality);
        Assert.True(TranslationQuotes.Fits(0m, 10m, 10m));
        Assert.False(TranslationQuotes.Fits(0.01m, 10m, 10m));
    }

    [Fact]
    public void Cada_qualidade_usa_o_modelo_do_perfil()
    {
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(Texts(1, 10), ["yst00001"]), Rate);

        Assert.Equal([ClaudeModels.Haiku, ClaudeModels.Sonnet, ClaudeModels.Opus], quotes.Qualities.Select(q => q.Profile.Model));
    }
}
