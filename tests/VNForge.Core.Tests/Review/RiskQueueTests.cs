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
    public void Linha_sem_nenhum_sinal_tem_risco_zero_e_nivel_baixo()
    {
        var risco = LineRisk.Of(Entrada(Linha("a", 1)));

        Assert.Equal(0, risco.Score);
        Assert.Equal(RiskLevel.Low, risco.Level);
        Assert.Empty(risco.Factors);
    }

    [Fact]
    public void Cada_sinal_soma_os_proprios_pontos()
    {
        var duvida = LineRisk.Of(Entrada(ComDuvida(Linha("a", 1))));
        var estranho = LineRisk.Of(Entrada(Linha("a", 2).WithOdd(true)));
        var sugestao = LineRisk.Of(Entrada(Linha("a", 3)), hasEditorSuggestion: true);
        var divergente = LineRisk.Of(Entrada(Linha("a", 4) with { AiTranslation = "outra versão" }));

        Assert.Equal(30, duvida.Score);
        Assert.Equal([RiskFactorKind.AiDoubt], duvida.Factors.Select(f => f.Kind));
        Assert.Equal(20, estranho.Score);
        Assert.Equal([RiskFactorKind.SoundsOdd], estranho.Factors.Select(f => f.Kind));
        Assert.Equal(10, sugestao.Score);
        Assert.Equal([RiskFactorKind.EditorSuggestion], sugestao.Factors.Select(f => f.Kind));
        Assert.Equal(8, divergente.Score);
        Assert.Equal([RiskFactorKind.PassDivergence], divergente.Factors.Select(f => f.Kind));
    }

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
    public void Todos_os_sinais_juntos_somam_92_na_ordem_do_cartao_e_o_texto_do_chip_traz_os_pontos()
    {
        var unit = ComDuvida(Linha("a", 1).WithOdd(true)) with { AiTranslation = "outra versão" };

        var risco = LineRisk.Of(Entrada(unit, Problema(unit, QaSeverity.Warning)), hasEditorSuggestion: true);

        Assert.Equal(92, risco.Score);
        Assert.Equal(RiskLevel.High, risco.Level);
        Assert.Equal(
            [RiskFactorKind.AiDoubt, RiskFactorKind.QaIssue, RiskFactorKind.SoundsOdd, RiskFactorKind.EditorSuggestion, RiskFactorKind.PassDivergence],
            risco.Factors.Select(f => f.Kind));
        Assert.Equal(["Dúvida da IA +30", "Aviso do QA: passa 1 linha +24", "Soa estranho +20", "Sugestão do editor +10", "Passadas divergem +8"], risco.Factors.Select(f => f.Text));
    }

    [Fact]
    public void A_pontuacao_para_em_100()
    {
        var unit = ComDuvida(Linha("a", 1).WithOdd(true)) with { AiTranslation = "outra versão" };

        var risco = LineRisk.Of(Entrada(unit, Problema(unit, QaSeverity.Error)), hasEditorSuggestion: true);

        Assert.Equal(100, risco.Score);
    }

    [Theory]
    [InlineData(0, RiskLevel.Low)]
    [InlineData(29, RiskLevel.Low)]
    [InlineData(30, RiskLevel.Medium)]
    [InlineData(59, RiskLevel.Medium)]
    [InlineData(60, RiskLevel.High)]
    [InlineData(100, RiskLevel.High)]
    public void Os_niveis_mudam_em_30_e_em_60(int pontos, RiskLevel esperado) =>
        Assert.Equal(esperado, RiskWeights.LevelOf(pontos));

    [Fact]
    public void A_fila_so_tem_linhas_esperando_revisao_e_vai_do_maior_risco_ao_menor_com_empate_na_ordem_da_historia()
    {
        var b = Linha("b", 1);
        var a1 = Linha("a", 1);
        var a2 = ComDuvida(Linha("a", 2));
        var a3 = Linha("a", 3).WithOdd(true);
        var traduzida = Linha("a", 4, TranslationStatus.Translated);
        var linhas = ProjectLines.Create(Textos(
            ("b", [b]),
            ("a", [a1, a2, a3, traduzida, Linha("a", 5, TranslationStatus.Approved), Linha("a", 6, TranslationStatus.Reviewed), Linha("a", 7, TranslationStatus.Keep), Linha("a", 8, TranslationStatus.Untranslated)])));

        var fila = RiskQueue.Build(linhas);

        Assert.Equal(["a:000002", "a:000003", "a:000001", "a:000004", "b:000001"], fila.Lines.Select(l => l.Line.Unit.Id));
        Assert.Equal([30, 20, 0, 0, 0], fila.Lines.Select(l => l.Score));
        Assert.Equal([2, 3, 1, 4, 1], fila.Lines.Select(l => l.Line.Number));
    }

    [Fact]
    public void O_aviso_do_QA_da_linha_entra_na_pontuacao_da_fila()
    {
        var unit = Linha("a", 1);
        var report = new QaReport([Problema(unit, QaSeverity.Warning)], [], ["caixa"]);
        var linhas = ProjectLines.Create(Textos(("a", [unit, Linha("a", 2)])), report);

        var fila = RiskQueue.Build(linhas);

        Assert.Equal(["a:000001", "a:000002"], fila.Lines.Select(l => l.Line.Unit.Id));
        Assert.Equal(24, fila.Lines[0].Score);
    }

    [Fact]
    public void Sugestao_do_editor_so_conta_enquanto_vale_para_a_linha()
    {
        var viva = Linha("a", 1);
        var velha = Linha("a", 2);
        var aprovada = Linha("a", 3, TranslationStatus.Approved);
        var linhas = ProjectLines.Create(Textos(("a", [viva, velha, aprovada, Linha("a", 4)])));
        var sugestoes = PendingEditorSuggestions.Of(
            ("a", viva.Id, viva.Translation!),
            ("a", velha.Id, "tradução antiga"),
            ("a", aprovada.Id, aprovada.Translation!));

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
        Assert.Equal("1.254 linhas de risco alto · sem ritmo ainda, aprove algumas linhas para calcular as horas", RiskQueue.DescribeHigh(1254, ReviewPace.None));
        Assert.Equal("Nenhuma linha de risco alto", RiskQueue.DescribeHigh(0, new ReviewPace(0, 84, null)));
    }

    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(223, "3 h 43 min")]
    [InlineData(1500, "25 h")]
    public void O_tempo_sai_em_horas_e_minutos(int minutos, string esperado) =>
        Assert.Equal(esperado, RiskQueue.FormatTime(TimeSpan.FromMinutes(minutos)));
}

public sealed class PendingEditorSuggestionsTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public PendingEditorSuggestionsTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private void Gravar(string json)
    {
        Directory.CreateDirectory(_project.CacheDirectory);
        File.WriteAllText(Path.Combine(_project.CacheDirectory, "editor-suggestions.json"), json);
    }

    [Fact]
    public void Sem_arquivo_nao_ha_sugestoes()
    {
        Assert.Equal(PendingEditorSuggestions.Empty, PendingEditorSuggestions.Load(_project));
    }

    [Fact]
    public void Le_a_linha_e_a_traducao_em_que_a_passada_se_baseou()
    {
        Gravar("""
            {"version":1,"suggestions":[
              {"scriptKey":"yst1","unitId":"yst1:000001","kind":"fluency","base":"Olá.","proposed":"Oi.","reason":"mais natural","at":"2026-11-08T10:00:00+00:00"},
              {"scriptKey":"yst1","unitId":"yst1:000002","kind":"voice","base":"Tchau.","proposed":"Até.","reason":"voz","at":"2026-11-08T10:00:00+00:00"}
            ]}
            """);

        var lidas = PendingEditorSuggestions.Load(_project);
        var viva = new TextUnit("yst1:000001", TextUnitKind.Dialogue, null, "a", "Olá.", TranslationStatus.Machine);

        Assert.True(lidas.Covers("yst1", viva));
        Assert.False(lidas.Covers("yst1", viva with { Translation = "Olá!" }));
        Assert.False(lidas.Covers("yst2", viva));
    }

    [Theory]
    [InlineData("não é json")]
    [InlineData("""{"version":9,"suggestions":[{"scriptKey":"a","unitId":"a:1","base":"x"}]}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"suggestions":[{"scriptKey":"a"},{"unitId":"a:1","base":"x"}]}""")]
    public void Arquivo_ilegivel_ou_de_outra_versao_nao_trava_e_nao_conta(string json)
    {
        Gravar(json);

        var lidas = PendingEditorSuggestions.Load(_project);

        Assert.Equal(PendingEditorSuggestions.Empty, lidas);
    }
}
