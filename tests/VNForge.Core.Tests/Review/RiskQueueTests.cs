using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Review;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Review;

public sealed class RiskQueueTests
{
    private static TextUnit Linha(string key, int n, TranslationStatus status = TranslationStatus.Machine) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, "美咲", $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    private static QaIssue Problema(TextUnit unit, QaSeverity gravidade, string regra = "caixa", string mensagem = "passa 1 linha") =>
        new(regra, gravidade, unit.Id, unit.Id.Split(':')[0], mensagem);

    private static LineEntry Entrada(TextUnit unit, params QaIssue[] problemas) => new(unit.Id.Split(':')[0], 1, unit, problemas);

    private static TextUnit ComDuvida(TextUnit unit) => unit with { Doubt = new TranslationDoubt(DoubtKind.Tone, "Ela fala sério?") };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos(params (string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);

    [Fact]
    public void Aviso_do_QA_vale_24_erro_40_e_sugestao_de_estilo_10_e_so_o_pior_conta()
    {
        var unit = Linha("a", 1);

        var aviso = LineRisk.Of(Entrada(unit, Problema(unit, QaSeverity.Warning)));
        var erro = LineRisk.Of(Entrada(unit, Problema(unit, QaSeverity.Warning), Problema(unit, QaSeverity.Error, "marcador", "faltam marcadores")));
        var estilo = LineRisk.Of(Entrada(unit, Problema(unit, QaSeverity.Warning, HonorificsRule.RuleId, "honorífico")));

        Assert.Equal(24, aviso.Score);
        Assert.Equal("Aviso do QA: passa 1 linha", Assert.Single(aviso.Factors).Label);
        Assert.Equal(40, erro.Score);
        Assert.Equal("Erro do QA: faltam marcadores", Assert.Single(erro.Factors).Label);
        Assert.Equal(10, estilo.Score);
        Assert.Equal("Sugestão de estilo do QA: honorífico", Assert.Single(estilo.Factors).Label);
    }

    [Fact]
    public void Todos_os_sinais_juntos_somam_84_na_ordem_do_cartao_e_o_texto_do_chip_traz_os_pontos()
    {
        var unit = ComDuvida(Linha("a", 1).WithOdd(true));

        var risco = LineRisk.Of(Entrada(unit, Problema(unit, QaSeverity.Warning)), hasEditorSuggestion: true);

        Assert.Equal(84, risco.Score);
        Assert.Equal(RiskLevel.High, risco.Level);
        Assert.Equal(
            [RiskFactorKind.AiDoubt, RiskFactorKind.QaIssue, RiskFactorKind.SoundsOdd, RiskFactorKind.EditorSuggestion],
            risco.Factors.Select(f => f.Kind));
        Assert.Equal(["Dúvida da IA +30", "Aviso do QA: passa 1 linha +24", "Soa estranho +20", "Sugestão do editor +10"], risco.Factors.Select(f => f.Text));
    }

    private static EditorSuggestion Sugestao(string unitId, string baseText) =>
        new("a", unitId, EditorSuggestionKind.Voice, baseText, "proposta", "voz", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Sugestao_do_editor_so_conta_enquanto_vale_para_a_linha()
    {
        var viva = Linha("a", 1);
        var velha = Linha("a", 2);
        var aprovada = Linha("a", 3, TranslationStatus.Approved);
        var linhas = ProjectLines.Create(Textos(("a", [viva, velha, aprovada, Linha("a", 4)])));
        var sugestoes = new EditorSuggestionsFile(
            EditorSuggestionsFile.CurrentVersion,
            [
                Sugestao(viva.Id, viva.Translation!),
                Sugestao(velha.Id, "tradução antiga"),
                Sugestao(aprovada.Id, aprovada.Translation!),
            ]);

        var fila = RiskQueue.Build(linhas, sugestoes);

        Assert.Equal(["a:000001", "a:000002", "a:000004"], fila.Lines.Select(l => l.Line.Unit.Id));
        Assert.Equal([10, 0, 0], fila.Lines.Select(l => l.Score));
        Assert.Equal(RiskFactorKind.EditorSuggestion, Assert.Single(fila.Lines[0].Factors).Kind);
    }

    [Fact]
    public void A_contagem_e_a_distribuicao_somam_100_por_nivel()
    {
        var altas = Enumerable.Range(1, 18).Select(i => ComDuvida(Linha("a", i))).ToList();
        var unidades = new List<TextUnit>(altas);
        unidades.AddRange(Enumerable.Range(19, 31).Select(i => ComDuvida(Linha("a", i))));
        unidades.AddRange(Enumerable.Range(50, 51).Select(i => Linha("a", i)));
        var erros = new QaReport([.. altas.Select(u => Problema(u, QaSeverity.Error))], [], ["caixa"]);

        var fila = RiskQueue.Build(ProjectLines.Create(Textos(("a", [.. unidades])), erros));

        Assert.Equal(18, fila.Count(RiskLevel.High));
        Assert.Equal(31, fila.Count(RiskLevel.Medium));
        Assert.Equal(51, fila.Count(RiskLevel.Low));
        Assert.Equal(new RiskDistribution(18, 31, 51), fila.Distribution);
    }

    [Fact]
    public void A_distribuicao_arredonda_pelo_maior_resto_e_uma_fila_vazia_e_toda_baixa()
    {
        var alta = ComDuvida(Linha("a", 1));
        var erro = new QaReport([Problema(alta, QaSeverity.Error)], [], ["caixa"]);
        var tres = RiskQueue.Build(ProjectLines.Create(Textos(("a", [alta, Linha("a", 2), Linha("a", 3)])), erro));

        Assert.Equal(new RiskDistribution(33, 0, 67), tres.Distribution);
        Assert.Equal(100, tres.Distribution.High + tres.Distribution.Medium + tres.Distribution.Low);
        Assert.Equal(new RiskDistribution(0, 0, 100), RiskQueue.Build(ProjectLines.Create(Textos())).Distribution);
    }

    [Fact]
    public void As_horas_ate_zerar_o_risco_alto_saem_do_ritmo_e_arredondam_o_minuto_para_cima()
    {
        var ritmo = new ReviewPace(0, 84, null);

        Assert.Equal(TimeSpan.FromMinutes(223), RiskQueue.TimeToReview(312, ritmo));
        Assert.Equal(TimeSpan.Zero, RiskQueue.TimeToReview(0, ritmo));
        Assert.Null(RiskQueue.TimeToReview(312, ReviewPace.None));
    }

    [Fact]
    public void A_frase_das_horas_conta_as_linhas_de_risco_alto_e_diz_o_ritmo_ou_que_ainda_nao_ha()
    {
        Assert.Equal("312 linhas de risco alto · faltam cerca de 3 h 43 min no seu ritmo de 84 linhas por hora", RiskQueue.DescribeHigh(312, new ReviewPace(0, 84, null)));
        Assert.Equal("1 linha de risco alto · faltam cerca de 1 h no seu ritmo de 1 linha por hora", RiskQueue.DescribeHigh(1, new ReviewPace(0, 1, null)));
        Assert.Equal("1.254 linhas de risco alto", RiskQueue.DescribeHigh(1254, ReviewPace.None));
        Assert.Equal("Nenhuma linha de risco alto", RiskQueue.DescribeHigh(0, new ReviewPace(0, 84, null)));
    }

    [Theory]
    [InlineData(0, "0 min")]
    public void O_tempo_sai_em_horas_e_minutos(int minutos, string esperado) =>
        Assert.Equal(esperado, RiskQueue.FormatTime(TimeSpan.FromMinutes(minutos)));
}
