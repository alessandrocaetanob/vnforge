using VNForge.AI.Bible;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using static VNForge.AI.Tests.Bible.BibleFixtures;

namespace VNForge.AI.Tests.Bible;

public sealed class BibleChapterPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));

    public BibleChapterPlannerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ProjectFolder ProjectWithTwoChapters()
    {
        var project = NewProject(_root);
        project.SaveText("yst1", Script("yst1", "おはよう", "遅いぞ"));
        project.SaveText("yst2", Script("yst2", "ただいま"));
        project.SaveText("yst3", Script("yst3", "さようなら"));
        project.SaveText("yst9", Script("yst9", "メニュー"));
        project.SaveScriptNames(ScriptNames.Empty
            .WithDetected(new Dictionary<string, DetectedScript>
            {
                ["yst1"] = new("manha", "userscript/01_cap1"),
                ["yst2"] = new("tarde", "userscript/01_cap1"),
                ["yst3"] = new("noite", "userscript/02_cap2"),
                ["yst9"] = new("menu", "userscript/sistema"),
            })
            .Rename("yst2", "Volta para casa"));
        project.SaveCharacters([new CharacterEntry("美咲", "Misaki")]);
        return project;
    }

    [Fact]
    public void Cena_sem_texto_nao_entra_e_os_personagens_vao_junto()
    {
        var project = ProjectWithTwoChapters();
        project.SaveText("yst2", [Line("yst2", 0, "  ", "美咲")]);

        var chapters = BibleChapterPlanner.Plan(project);

        Assert.Equal(["yst1"], chapters[0].Scenes.Select(s => s.Key));
        Assert.Equal(new BibleCharacterName("美咲", "Misaki"), Assert.Single(chapters[0].Characters));
    }

    [Fact]
    public void Capitulo_maior_que_o_contexto_se_divide_em_partes_na_fronteira_das_cenas()
    {
        var long1 = Scene("yst1", "a", [.. Enumerable.Repeat(new string('あ', 400), 10)]);
        var long2 = Scene("yst2", "b", [.. Enumerable.Repeat(new string('あ', 400), 10)]);
        var long3 = Scene("yst3", "c", [.. Enumerable.Repeat(new string('あ', 400), 10)]);
        var chapter = Chapter("Manhã", 1, long1, long2, long3);

        var parts = BibleChapterPlanner.Split(chapter, maxTokens: 9000);

        Assert.Equal(["Manhã (parte 1)", "Manhã (parte 2)"], parts.Select(p => p.Name));
        Assert.Equal([["yst1", "yst2"], ["yst3"]], parts.Select(p => p.Scenes.Select(s => s.Key).ToArray()));
        Assert.All(parts, p => Assert.Equal(1, p.Number));
    }
}
