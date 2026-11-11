using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class QaEngineTests
{
    internal sealed class FlagEmpty(string id = "traducao-vazia", QaSeverity severity = QaSeverity.Error) : UnitQaRule
    {
        public override string Id => id;

        public override string Description => "Tradução vazia.";

        public override QaSeverity Severity => severity;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == "")
            {
                yield return "tradução vazia";
            }
        }
    }

    private static TextUnit Unit(string id, string? translation, params string[] flags) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, TranslationStatus.Translated) { Flags = flags };

    private static QaContext Context(params TextUnit[] units) =>
        QaContext.ForTexts(units.GroupBy(u => u.Id.Split(':')[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)g.ToList()));

    [Fact]
    public void Filtro_por_regra_desconhecida_e_erro_em_portugues()
    {
        var engine = new QaEngine([new FlagEmpty()]);

        var ex = Assert.Throws<VNForgeException>(() => engine.Run(Context(), new QaOptions(Rules: ["nao-existe"])));

        Assert.Contains("nao-existe", ex.Message);
        Assert.Contains("traducao-vazia", ex.Message);
    }

    [Fact]
    public void Erro_marcado_como_ok_continua_bloqueando()
    {
        var engine = new QaEngine([new FlagEmpty("erro-x")]);

        var report = engine.Run(Context(Unit("a:1", "", "erro-x")));

        Assert.True(report.HasErrors);
        Assert.Empty(report.Silenced);
        Assert.True(report.Blocks(allowWarnings: true));
    }

    [Fact]
    public void DescribeUntranslated_usa_singular_plural_e_fica_vazio_sem_linhas()
    {
        static QaReport Report(int n) => new QaEngine([new UntranslatedQaRule()]).Run(Context(
            [.. Enumerable.Range(1, n).Select(i => new TextUnit($"a:{i}", TextUnitKind.Dialogue, null, "こんにちは", null, TranslationStatus.Untranslated))]));

        Assert.Equal(string.Empty, QaBlockedException.DescribeUntranslated(Report(0)));
        Assert.Equal("QA: 1 linha sem tradução; o build copiou o original nela.", QaBlockedException.DescribeUntranslated(Report(1)));
        Assert.Equal("QA: 1.254 linhas sem tradução; o build copiou o original nelas.", QaBlockedException.DescribeUntranslated(Report(1254)));
    }

    [Theory]
    [InlineData("com espaco")]
    public void Id_de_regra_invalido_e_recusado(string id) =>
        Assert.Throws<ArgumentException>(() => new QaEngine([new FlagEmpty(id)]));

    [Fact]
    public void Id_de_regra_repetido_e_recusado() =>
        Assert.Throws<ArgumentException>(() => new QaEngine([new FlagEmpty("igual"), new FlagEmpty("igual")]));
}
