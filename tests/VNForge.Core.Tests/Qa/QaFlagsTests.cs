using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class QaFlagsTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;
    private readonly QaEngine _engine = new([new QaEngineTests.FlagEmpty("aviso-x", QaSeverity.Warning), new QaEngineTests.FlagEmpty("outro-aviso", QaSeverity.Warning)]);

    public QaFlagsTests()
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
        _project.SaveText("a", [Unit("a:1"), Unit("a:2")]);
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Unit(string id) =>
        new(id, TextUnitKind.Dialogue, null, "原文", "", TranslationStatus.Translated);

    private IReadOnlyList<string> FlagsOf(string id) => _project.LoadText("a").Single(u => u.Id == id).Flags;

    [Fact]
    public void Desmarcar_tira_so_aquela_regra()
    {
        QaFlags.MarkOk(_project, _engine, "a:1", "aviso-x");
        QaFlags.MarkOk(_project, _engine, "a:1", "outro-aviso");

        Assert.True(QaFlags.Unmark(_project, _engine, "a:1", "aviso-x"));
        Assert.False(QaFlags.Unmark(_project, _engine, "a:1", "aviso-x"));

        Assert.Equal(["outro-aviso"], FlagsOf("a:1"));
    }

    [Fact]
    public void A_marca_silencia_o_aviso_depois_de_ler_o_arquivo()
    {
        QaFlags.MarkOk(_project, _engine, "a:1", "aviso-x");

        var report = _engine.Run(QaContext.FromProject(_project), new QaOptions(Rules: ["aviso-x"]));

        Assert.Equal(["a:2"], report.Issues.Select(i => i.UnitId));
        Assert.Equal(["a:1"], report.Silenced.Select(i => i.UnitId));
    }

    [Fact]
    public void Regra_desconhecida_e_recusada_sem_gravar()
    {
        var ex = Assert.Throws<VNForgeException>(() => QaFlags.MarkOk(_project, _engine, "a:1", "nao-existe"));

        Assert.Contains("nao-existe", ex.Message);
        Assert.Empty(FlagsOf("a:1"));
    }

    [Theory]
    [InlineData("a:9")]
    [InlineData("zz:1")]
    [InlineData("../x:1")]
    public void Linha_inexistente_e_erro_em_portugues(string unitId)
    {
        var ex = Assert.Throws<VNForgeException>(() => QaFlags.MarkOk(_project, _engine, unitId, "aviso-x"));

        Assert.Contains(unitId.StartsWith("..", StringComparison.Ordinal) ? "inválido" : "não encontrada", ex.Message);
    }
}
