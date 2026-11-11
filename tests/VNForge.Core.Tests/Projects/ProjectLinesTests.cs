using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public class ProjectLinesTests
{
    private static TextUnit Linha(string key, int n, string? falante, TranslationStatus status) =>
        new($"{key}:{n:D6}", TextUnitKind.Dialogue, falante, $"原文{n}", status == TranslationStatus.Untranslated ? null : $"tradução {n}", status);

    private static Dictionary<string, IReadOnlyList<TextUnit>> Textos(params (string Key, TextUnit[] Units)[] scripts) =>
        scripts.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);

    private static QaIssue Problema(string key, int n, QaSeverity gravidade, string regra = "regra") =>
        new(regra, gravidade, $"{key}:{n:D6}", key, "problema");

    private static LineEntry Entrada(TextUnit unit, params QaIssue[] problemas) => new("s", 1, unit, problemas);

    [Fact]
    public void Chave_desconhecida_devolve_lista_vazia()
    {
        var linhas = ProjectLines.Create(Textos(("a", [Linha("a", 1, null, TranslationStatus.Untranslated)])));

        Assert.Empty(linhas.LinesOf("outro"));
    }

    [Fact]
    public void Problemas_casam_com_a_linha_pela_chave_do_script_e_pelo_id()
    {
        var texts = Textos(
            ("a", [Linha("a", 1, null, TranslationStatus.Approved), Linha("a", 2, null, TranslationStatus.Approved)]),
            ("b", [Linha("b", 1, null, TranslationStatus.Approved)]));
        var relatorio = new QaReport(
            [
                Problema("a", 2, QaSeverity.Warning, "x"),
                Problema("a", 2, QaSeverity.Error, "y"),
                Problema("b", 1, QaSeverity.Warning),
                Problema("c", 1, QaSeverity.Error),
            ],
            [],
            ["x", "y", "regra"]);

        var linhas = ProjectLines.Create(texts, relatorio);

        Assert.False(linhas.LinesOf("a")[0].HasIssues);
        Assert.Null(linhas.LinesOf("a")[0].Severity);
        Assert.Equal(2, linhas.LinesOf("a")[1].Issues.Count);
        Assert.Equal(QaSeverity.Error, linhas.LinesOf("a")[1].Severity);
        Assert.Equal(QaSeverity.Warning, linhas.LinesOf("b")[0].Severity);
    }

    [Fact]
    public void Falantes_saem_do_que_mais_fala_para_o_que_menos_fala_com_empate_ordinal()
    {
        var entradas = new[]
        {
            Entrada(Linha("a", 1, "健", TranslationStatus.Approved)),
            Entrada(Linha("a", 2, "美咲", TranslationStatus.Approved)),
            Entrada(Linha("a", 3, "美咲", TranslationStatus.Approved)),
            Entrada(Linha("a", 4, null, TranslationStatus.Approved)),
            Entrada(Linha("a", 5, "", TranslationStatus.Approved)),
            Entrada(Linha("a", 6, "Ana", TranslationStatus.Approved)),
            Entrada(Linha("a", 7, "ana", TranslationStatus.Approved)),
        };

        Assert.Equal(["美咲", "Ana", "ana", "健"], ProjectLines.SpeakersOf(entradas));
        Assert.Empty(ProjectLines.SpeakersOf([]));
    }

    [Fact]
    public void Filtro_de_duvida_aceita_so_as_linhas_com_duvida()
    {
        var comDuvida = Entrada(Linha("s", 1, null, TranslationStatus.Machine) with { Doubt = new TranslationDoubt(DoubtKind.Tone, "Ironia?") });
        var semDuvida = Entrada(Linha("s", 2, null, TranslationStatus.Machine));
        var filtro = new LineFilter(LineStatusFilter.Doubt);

        Assert.True(filtro.Matches(comDuvida));
        Assert.False(filtro.Matches(semDuvida));
        Assert.Equal(1, filtro.Count([comDuvida, semDuvida]));
    }

    [Fact]
    public void Filtro_de_personagem_compara_em_ordem_ordinal_e_narracao_aceita_so_sem_falante()
    {
        var misaki = Entrada(Linha("s", 1, "美咲", TranslationStatus.Approved));
        var ana = Entrada(Linha("s", 2, "Ana", TranslationStatus.Approved));
        var nula = Entrada(Linha("s", 3, null, TranslationStatus.Approved));
        var vazia = Entrada(Linha("s", 4, "", TranslationStatus.Approved));

        Assert.All(new[] { misaki, ana, nula, vazia }, l => Assert.True(new LineFilter(Speaker: null).Matches(l)));
        Assert.True(new LineFilter(Speaker: "Ana").Matches(ana));
        Assert.False(new LineFilter(Speaker: "ana").Matches(ana));
        Assert.False(new LineFilter(Speaker: "Ana").Matches(misaki));
        Assert.False(new LineFilter(Speaker: "Ana").Matches(nula));
        Assert.True(new LineFilter(Speaker: LineFilter.Narration).Matches(nula));
        Assert.True(new LineFilter(Speaker: LineFilter.Narration).Matches(vazia));
        Assert.False(new LineFilter(Speaker: LineFilter.Narration).Matches(ana));
    }

    [Fact]
    public void Apply_devolve_a_mesma_lista_quando_o_filtro_aceita_tudo_e_preserva_a_ordem_quando_filtra()
    {
        var a = Entrada(Linha("s", 1, null, TranslationStatus.Machine));
        var b = Entrada(Linha("s", 2, null, TranslationStatus.Untranslated));
        var c = Entrada(Linha("s", 3, null, TranslationStatus.Machine));
        IReadOnlyList<LineEntry> todas = [a, b, c];

        Assert.Same(todas, LineFilter.None.Apply(todas));
        Assert.Same(todas, new LineFilter().Apply(todas));
        Assert.Equal(3, LineFilter.None.Count(todas));
        Assert.Equal([a, c], new LineFilter(LineStatusFilter.Machine).Apply(todas));
    }

    [Fact]
    public void Filtro_de_avisos_conta_qualquer_gravidade()
    {
        var aviso = Entrada(Linha("s", 1, null, TranslationStatus.Approved), Problema("s", 1, QaSeverity.Warning));
        var erro = Entrada(Linha("s", 2, null, TranslationStatus.Approved), Problema("s", 2, QaSeverity.Error));
        var limpa = Entrada(Linha("s", 3, null, TranslationStatus.Approved));

        Assert.Equal([aviso, erro], new LineFilter(LineStatusFilter.Warnings).Apply([aviso, erro, limpa]));
    }

    private static ProjectFolder ProjetoCp1252(string raiz, params TextUnit[] linhas)
    {
        var projeto = ProjectFolder.Create(raiz, new ProjectConfig(
            "VN",
            new GameConfig("D:/Jogos/VN", "VN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("cp1252"),
            AiConfig.Default));
        projeto.SaveText("story04", linhas);
        return projeto;
    }

    private static TextUnit RascunhoComTil() =>
        new("story04:000064", TextUnitKind.Dialogue, null, "～朝～", "～Manhã～", TranslationStatus.Machine);

    [Fact]
    public void Gravar_a_linha_sem_o_caractere_tira_o_erro_de_codificacao_sem_reler_o_projeto()
    {
        using var pasta = new TempFolder();
        var linhas = ProjectLines.Read(ProjetoCp1252(pasta.Path, RascunhoComTil()));
        var entrada = linhas.LinesOf("story04").Single();
        Assert.Contains(entrada.Issues, i => i.RuleId == UnencodableCharacterRule.RuleId);

        var gravada = linhas.WithUnit(entrada, entrada.Unit with { Translation = "Manhã", Status = TranslationStatus.Translated });

        Assert.DoesNotContain(gravada.Issues, i => i.RuleId == UnencodableCharacterRule.RuleId);
    }
}
