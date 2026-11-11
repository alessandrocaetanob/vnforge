using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Qa;

public sealed class ConsistencyGroupTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private static TextUnit Unit(string id, string source, string? translation, params string[] flags) =>
        new(id, TextUnitKind.Dialogue, null, source, translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated) { Flags = flags };

    private static QaContext Context(params TextUnit[] units) =>
        QaContext.ForTexts(units.GroupBy(u => u.Id.Split(':')[0]).ToDictionary(g => g.Key, g => (IReadOnlyList<TextUnit>)g.ToList()));

    private ProjectFolder Project(params TextUnit[] units)
    {
        var project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig("Teste", new GameConfig("jogo", "Jogo.exe"), new EngineConfig("fake"), new LanguagesConfig("ja", "pt-BR"), new EncodingConfig("sjis"), AiConfig.Default));
        foreach (var script in units.GroupBy(u => u.Id.Split(':')[0]))
        {
            project.SaveText(script.Key, [.. script]);
        }

        return project;
    }

    private static IReadOnlyList<ConsistencyGroup> Groups(ProjectFolder project) => ConsistencyGroups.Find(QaContext.ForTexts(project.LoadAllText()));

    [Fact]
    public void Um_original_com_tres_traducoes_vira_um_grupo_com_tres_variantes_e_as_linhas_de_cada_uma()
    {
        var context = Context(
            Unit("a:1", "はい", "Sim."), Unit("a:2", "はい", "Sim."), Unit("b:1", "はい", "Sim."),
            Unit("b:2", "はい", "Sim"), Unit("c:1", "はい", "Isso"));

        var group = Assert.Single(ConsistencyGroups.Find(context));

        Assert.Equal("はい", group.Source);
        Assert.Equal(["Sim.", "Sim", "Isso"], group.Variants.Select(v => v.Translation));
        Assert.Equal([3, 1, 1], group.Variants.Select(v => v.Lines.Count));
        Assert.Equal(5, group.LineCount);
        Assert.Equal([("a:1", "a"), ("a:2", "a"), ("b:1", "b")], group.Reference.Lines.Select(l => (l.UnitId, l.ScriptKey)));
    }

    [Fact]
    public void Manter_diferentes_sobrevive_a_reabrir_o_projeto()
    {
        var project = Project(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"));
        ConsistencyFlags.Keep(project, Assert.Single(Groups(project)));

        var reopened = ProjectFolder.Open(project.Root);

        Assert.Empty(new InconsistentTranslationRule().Check(QaContext.ForTexts(reopened.LoadAllText())));
    }

    [Fact]
    public void Manter_diferentes_recusa_um_grupo_que_mudou_no_disco_depois_de_conferido()
    {
        var project = Project(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"));
        var group = Assert.Single(Groups(project));
        var units = project.LoadText("a").ToList();
        units[1] = units[1] with { Translation = "Aquilo" };
        project.SaveText("a", units);

        var failure = Assert.Throws<VNForgeException>(() => ConsistencyFlags.Keep(project, group));

        Assert.Contains("mudaram", failure.Message);
        Assert.All(project.LoadText("a"), u => Assert.Empty(u.Flags));
    }
}
