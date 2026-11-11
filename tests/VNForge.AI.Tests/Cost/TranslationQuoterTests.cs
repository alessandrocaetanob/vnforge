using VNForge.AI.Claude;
using VNForge.AI.Cost;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Cost;

public class TranslationQuoterTests
{
    private static readonly ExchangeRate Rate = new(5m, new DateOnly(2026, 10, 25));

    private static TextUnit Line(string id, string source = "こんにちは、世界のみなさん", TranslationStatus status = TranslationStatus.Untranslated) =>
        new(id, TextUnitKind.Dialogue, null, source, status == TranslationStatus.Untranslated ? null : "ok", status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts(int scripts, int linesEach)
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
    public void Sem_cenas_escolhidas_a_selecao_fica_vazia()
    {
        var keys = new[] { "a", "b" };

        Assert.Empty(TranslationQuoter.KeysFor(TranslationScope.Selection, keys, "a"));
        Assert.Empty(TranslationQuoter.KeysFor(TranslationScope.Selection, keys, "a", []));
    }

    [Fact]
    public void Sem_cena_aberta_o_capitulo_e_a_cena_ficam_vazios()
    {
        var keys = new[] { "a", "b" };

        Assert.Empty(TranslationQuoter.KeysFor(TranslationScope.Scene, keys, null));
        Assert.Empty(TranslationQuoter.KeysFor(TranslationScope.Chapter, keys, "outra"));
    }

    [Fact]
    public void Sem_limite_a_mais_cuidadosa_sempre_cabe()
    {
        var quotes = TranslationQuoter.Quote(TranslationQuoter.PendingLines(Texts(1, 50), ["yst00001"]), Rate);

        Assert.Equal(TranslationQuality.Careful, quotes.MostCarefulThatFits(spent: 99999m, limit: null)?.Quality);
        Assert.True(TranslationQuotes.Fits(0m, 10m, 10m));
        Assert.False(TranslationQuotes.Fits(0.01m, 10m, 10m));
    }
}
