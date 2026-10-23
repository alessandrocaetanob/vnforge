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
    public void Sem_regras_o_relatorio_e_vazio()
    {
        var report = new QaEngine([]).Run(Context(Unit("a:1", "")));

        Assert.Empty(report.Issues);
        Assert.False(report.Blocks(allowWarnings: false));
    }

    [Fact]
    public void Regra_por_linha_aponta_a_linha_com_id_gravidade_e_script()
    {
        var engine = new QaEngine([new FlagEmpty()]);

        var report = engine.Run(Context(Unit("a:1", "Oi"), Unit("a:2", "")));

        var issue = Assert.Single(report.Issues);
        Assert.Equal(new QaIssue("traducao-vazia", QaSeverity.Error, "a:2", "a", "tradução vazia"), issue);
        Assert.Equal(["traducao-vazia"], report.RulesRun);
    }

    [Fact]
    public void Filtro_por_regra_roda_so_as_escolhidas()
    {
        var engine = new QaEngine([new FlagEmpty("regra-a"), new FlagEmpty("regra-b", QaSeverity.Warning)]);

        var report = engine.Run(Context(Unit("a:1", "")), new QaOptions(Rules: ["regra-b"]));

        Assert.Equal("regra-b", Assert.Single(report.Issues).RuleId);
        Assert.Equal(["regra-b"], report.RulesRun);
    }

    [Fact]
    public void Filtro_por_regra_desconhecida_e_erro_em_portugues()
    {
        var engine = new QaEngine([new FlagEmpty()]);

        var ex = Assert.Throws<VNForgeException>(() => engine.Run(Context(), new QaOptions(Rules: ["nao-existe"])));

        Assert.Contains("nao-existe", ex.Message);
        Assert.Contains("traducao-vazia", ex.Message);
    }

    [Fact]
    public void Filtro_por_gravidade_e_um_minimo()
    {
        var engine = new QaEngine([new FlagEmpty("erro-x"), new FlagEmpty("aviso-x", QaSeverity.Warning)]);
        var context = Context(Unit("a:1", ""));

        Assert.Equal(["erro-x", "aviso-x"], engine.Run(context, new QaOptions(MinimumSeverity: QaSeverity.Warning)).Issues.Select(i => i.RuleId));
        Assert.Equal(["erro-x"], engine.Run(context, new QaOptions(MinimumSeverity: QaSeverity.Error)).Issues.Select(i => i.RuleId));
    }

    [Fact]
    public void Marcar_como_ok_silencia_o_aviso_so_naquela_linha()
    {
        var engine = new QaEngine([new FlagEmpty("aviso-x", QaSeverity.Warning)]);

        var report = engine.Run(Context(Unit("a:1", "", "aviso-x"), Unit("a:2", "")));

        Assert.Equal("a:2", Assert.Single(report.Issues).UnitId);
        Assert.Equal("a:1", Assert.Single(report.Silenced).UnitId);
        Assert.True(report.Blocks(allowWarnings: false));
    }

    [Fact]
    public void Marca_de_outra_regra_nao_silencia()
    {
        var engine = new QaEngine([new FlagEmpty("aviso-x", QaSeverity.Warning)]);

        var report = engine.Run(Context(Unit("a:1", "", "outra-regra")));

        Assert.Single(report.Issues);
        Assert.Empty(report.Silenced);
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
    public void Allow_warnings_libera_so_avisos()
    {
        var engine = new QaEngine([new FlagEmpty("aviso-x", QaSeverity.Warning)]);
        var report = engine.Run(Context(Unit("a:1", "")));

        Assert.True(report.Blocks(allowWarnings: false));
        Assert.False(report.Blocks(allowWarnings: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Maiuscula")]
    [InlineData("com espaco")]
    [InlineData("-comeca-com-hifen")]
    [InlineData("termina-com-hifen-")]
    public void Id_de_regra_invalido_e_recusado(string id) =>
        Assert.Throws<ArgumentException>(() => new QaEngine([new FlagEmpty(id)]));

    [Fact]
    public void Id_de_regra_repetido_e_recusado() =>
        Assert.Throws<ArgumentException>(() => new QaEngine([new FlagEmpty("igual"), new FlagEmpty("igual")]));

    [Fact]
    public void Regra_entre_linhas_ve_o_projeto_inteiro()
    {
        var engine = new QaEngine([new SameSourceRule()]);

        var report = engine.Run(Context(Unit("a:1", "Oi"), Unit("b:1", "Olá")));

        Assert.Equal(["a:1", "b:1"], report.Issues.Select(i => i.UnitId));
    }

    private sealed class SameSourceRule : IQaRule
    {
        public string Id => "inconsistencia";

        public string Description => "Mesmo original traduzido de jeitos diferentes.";

        public IEnumerable<QaIssue> Check(QaContext context)
        {
            var all = context.Texts.SelectMany(t => t.Value.Select(u => (Key: t.Key, Unit: u))).ToList();
            foreach (var (key, unit) in all.Where(x => all.Any(y => y.Unit.Source == x.Unit.Source && y.Unit.Translation != x.Unit.Translation)))
            {
                yield return new QaIssue(Id, QaSeverity.Warning, unit.Id, key, "traduzido de outro jeito em outra linha");
            }
        }
    }
}
