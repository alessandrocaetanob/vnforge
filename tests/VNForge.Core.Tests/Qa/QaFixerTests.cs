using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class QaFixerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public QaFixerTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("jogo", "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Unit(string id, string? translation, TranslationStatus status = TranslationStatus.Translated, params string[] flags) =>
        new(id, TextUnitKind.Dialogue, null, "原文", translation, status) { Flags = flags };

    private static Dictionary<string, IReadOnlyList<TextUnit>> Texts(params TextUnit[] units) =>
        new Dictionary<string, IReadOnlyList<TextUnit>> { ["a"] = units };

    [Fact]
    public void Ignora_linha_sem_traducao_e_nao_mexe_no_status()
    {
        var result = QaFixer.Plan(
            Texts(Unit("a:1", "a  b", TranslationStatus.Untranslated), Unit("a:2", null), Unit("a:3", "c  d", TranslationStatus.Reviewed)),
            QaEngine.Default);

        var fix = Assert.Single(result.Fixes);
        Assert.Equal("a:3", fix.UnitId);
        Assert.Equal(TranslationStatus.Reviewed, result.Texts["a"][2].Status);
        Assert.Equal("a  b", result.Texts["a"][0].Translation);
    }

    [Fact]
    public void Filtro_com_regra_desconhecida_falha()
    {
        Assert.Throws<VNForgeException>(() => QaFixer.Plan(Texts(Unit("a:1", "x")), QaEngine.Default, ["nao-existe"]));
    }

    [Fact]
    public void Filtro_com_regra_sem_correcao_falha()
    {
        Assert.Throws<VNForgeException>(() => QaFixer.Plan(Texts(Unit("a:1", "x")), QaEngine.Default, ["marcador-diferente"]));
    }
    private static QaReport Run(IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> texts) =>
        QaEngine.Default.Run(QaContext.ForTexts(texts));

    [Fact]
    public void Propoe_a_correcao_de_cada_regra_sobre_a_traducao_original_sem_encadear()
    {
        var texts = Texts(Unit("a:1", "Vamos,  anda !"));

        var proposals = QaFixer.Propose(texts, QaEngine.Default, Run(texts).Issues);

        // Cada regra propõe a sua correção sobre o texto gravado: a de espaço duplo não vê a de pontuação.
        Assert.Equal(
            [("espaco-duplo", "Vamos, anda !"), ("espaco-antes-pontuacao", "Vamos,  anda!")],
            proposals.Select(p => (p.RuleId, p.After)));
        Assert.All(proposals, p => Assert.Equal(("a:1", "a", "Vamos,  anda !"), (p.UnitId, p.ScriptKey, p.Before)));
    }

    [Fact]
    public void ApplyOne_devolve_nulo_e_nao_grava_quando_nada_muda()
    {
        _project.SaveText("a", [Unit("a:1", "limpo")]);
        var path = _project.TextPath("a");
        var bytes = File.ReadAllBytes(path);

        Assert.Null(QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "espaco-duplo"));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void ApplyOne_com_regra_sem_corretor_ou_desconhecida_falha()
    {
        _project.SaveText("a", [Unit("a:1", "x")]);

        Assert.Throws<VNForgeException>(() => QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "traducao-vazia"));
        Assert.Throws<VNForgeException>(() => QaFixer.ApplyOne(_project, QaEngine.Default, "a", "a:1", "nada"));
    }
}
