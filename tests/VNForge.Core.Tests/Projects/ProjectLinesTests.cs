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
    public void Scripts_saem_em_ordem_ordinal_da_chave_com_a_contagem_de_cada_um()
    {
        var linhas = ProjectLines.Create(Textos(
            ("yst00010", [Linha("yst00010", 1, null, TranslationStatus.Approved)]),
            ("Zeta", []),
            ("yst00002", [Linha("yst00002", 1, null, TranslationStatus.Untranslated), Linha("yst00002", 2, null, TranslationStatus.Machine)])));

        Assert.Equal(["Zeta", "yst00002", "yst00010"], linhas.Scripts.Select(s => s.Key));
        Assert.Equal(new StatusCounts(1, 1, 0, 0, 0), linhas.Scripts[1].Counts);
        Assert.Equal(new StatusCounts(0, 0, 0, 0, 1), linhas.Scripts[2].Counts);
    }

    [Fact]
    public void Linhas_ganham_numero_a_partir_de_1_na_ordem_do_arquivo()
    {
        var linhas = ProjectLines.Create(Textos(("a", [Linha("a", 9, null, TranslationStatus.Untranslated), Linha("a", 3, null, TranslationStatus.Untranslated)])));

        var script = linhas.LinesOf("a");

        Assert.Equal([1, 2], script.Select(l => l.Number));
        Assert.Equal(["a:000009", "a:000003"], script.Select(l => l.Unit.Id));
        Assert.All(script, l => Assert.Equal("a", l.ScriptKey));
    }

    [Fact]
    public void Chave_desconhecida_devolve_lista_vazia()
    {
        var linhas = ProjectLines.Create(Textos(("a", [Linha("a", 1, null, TranslationStatus.Untranslated)])));

        Assert.Empty(linhas.LinesOf("outro"));
    }

    [Fact]
    public void Percentuais_truncam_e_contam_revisadas_e_aprovadas_como_concluidas()
    {
        var progresso = new ScriptProgress("a", new StatusCounts(1, 1, 1, 1, 1));
        var quase = new ScriptProgress("b", new StatusCounts(0, 0, 0, 2, 1));
        var tudo = new ScriptProgress("c", new StatusCounts(0, 0, 0, 0, 3));

        Assert.Equal(40, progresso.CompletedPercent);
        Assert.Equal(20, progresso.MachinePercent);
        Assert.Equal(100, quase.CompletedPercent);
        Assert.Equal(100, tudo.CompletedPercent);
        Assert.Equal(0, tudo.MachinePercent);
        Assert.Equal(66, new ScriptProgress("d", new StatusCounts(1, 0, 0, 0, 2)).CompletedPercent);
        Assert.Equal(99, new ScriptProgress("e", new StatusCounts(1, 0, 0, 0, 199)).CompletedPercent);
    }

    [Fact]
    public void Script_sem_linhas_tem_percentuais_zero()
    {
        var linhas = ProjectLines.Create(Textos(("vazio", [])));

        var script = Assert.Single(linhas.Scripts);
        Assert.Equal(0, script.CompletedPercent);
        Assert.Equal(0, script.MachinePercent);
        Assert.Empty(linhas.LinesOf("vazio"));
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
    public void Id_igual_em_scripts_diferentes_nao_mistura_problemas()
    {
        var texts = Textos(
            ("a", [new TextUnit("1", TextUnitKind.Dialogue, null, "x", null, TranslationStatus.Untranslated)]),
            ("b", [new TextUnit("1", TextUnitKind.Dialogue, null, "x", null, TranslationStatus.Untranslated)]));
        var relatorio = new QaReport([new QaIssue("r", QaSeverity.Warning, "1", "b", "m")], [], ["r"]);

        var linhas = ProjectLines.Create(texts, relatorio);

        Assert.False(linhas.LinesOf("a")[0].HasIssues);
        Assert.True(linhas.LinesOf("b")[0].HasIssues);
    }

    [Fact]
    public void Sem_relatorio_nenhuma_linha_tem_problema()
    {
        var linhas = ProjectLines.Create(Textos(("a", [Linha("a", 1, null, TranslationStatus.Untranslated)])));

        Assert.All(linhas.LinesOf("a"), l => Assert.False(l.HasIssues));
    }

    [Fact]
    public void Read_le_o_projeto_e_roda_o_QA_padrao()
    {
        using var temp = new TempFolder();
        var projeto = ProjectFolder.Create(temp.Path, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("yst00002", [Linha("yst00002", 1, "健", TranslationStatus.Approved)]);
        projeto.SaveText("yst00001",
        [
            Linha("yst00001", 1, "美咲", TranslationStatus.Approved),
            Linha("yst00001", 2, null, TranslationStatus.Untranslated),
            new("yst00001:000003", TextUnitKind.Dialogue, null, "原文3", null, TranslationStatus.Translated),
        ]);

        var linhas = ProjectLines.Read(projeto);

        Assert.Equal(["yst00001", "yst00002"], linhas.Scripts.Select(s => s.Key));
        var primeiro = linhas.LinesOf("yst00001");
        Assert.Equal(3, primeiro.Count);
        Assert.False(primeiro[0].HasIssues);
        Assert.Equal(QaSeverity.Warning, primeiro[1].Severity);
        Assert.Equal(QaSeverity.Error, primeiro[2].Severity);
        Assert.False(linhas.LinesOf("yst00002")[0].HasIssues);
    }

    [Fact]
    public void Read_com_um_motor_de_QA_sem_regras_nao_marca_nada()
    {
        using var temp = new TempFolder();
        var projeto = ProjectFolder.Create(temp.Path, new ProjectConfig(
            "Minha VN",
            new GameConfig("D:/Jogos/MinhaVN", "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        projeto.SaveText("a", [Linha("a", 1, null, TranslationStatus.Untranslated)]);

        var linhas = ProjectLines.Read(projeto, new QaEngine([]));

        Assert.False(linhas.LinesOf("a")[0].HasIssues);
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

    [Theory]
    [InlineData(LineStatusFilter.All, TranslationStatus.Untranslated, false, true)]
    [InlineData(LineStatusFilter.All, TranslationStatus.Approved, true, true)]
    [InlineData(LineStatusFilter.Pending, TranslationStatus.Untranslated, false, true)]
    [InlineData(LineStatusFilter.Pending, TranslationStatus.Machine, false, false)]
    [InlineData(LineStatusFilter.Pending, TranslationStatus.Approved, true, false)]
    [InlineData(LineStatusFilter.Machine, TranslationStatus.Machine, false, true)]
    [InlineData(LineStatusFilter.Machine, TranslationStatus.Untranslated, false, false)]
    [InlineData(LineStatusFilter.Machine, TranslationStatus.Translated, true, false)]
    [InlineData(LineStatusFilter.Warnings, TranslationStatus.Approved, true, true)]
    [InlineData(LineStatusFilter.Warnings, TranslationStatus.Untranslated, false, false)]
    public void Filtro_de_status_aceita_so_o_que_pede(LineStatusFilter filtro, TranslationStatus status, bool comProblema, bool esperado)
    {
        var problemas = comProblema ? new[] { Problema("s", 1, QaSeverity.Warning) } : [];
        var entrada = Entrada(Linha("s", 1, null, status), problemas);

        Assert.Equal(esperado, new LineFilter(filtro).Matches(entrada));
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
    public void Status_e_personagem_valem_juntos()
    {
        var pendenteAna = Entrada(Linha("s", 1, "Ana", TranslationStatus.Untranslated));
        var pendenteKen = Entrada(Linha("s", 2, "Ken", TranslationStatus.Untranslated));
        var iaAna = Entrada(Linha("s", 3, "Ana", TranslationStatus.Machine));
        var todas = new[] { pendenteAna, pendenteKen, iaAna };
        var filtro = new LineFilter(LineStatusFilter.Pending, "Ana");

        Assert.Equal([pendenteAna], filtro.Apply(todas));
        Assert.Equal(1, filtro.Count(todas));
        Assert.Equal(2, new LineFilter(LineStatusFilter.Pending).Count(todas));
        Assert.Equal(2, new LineFilter(Speaker: "Ana").Count(todas));
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
}
