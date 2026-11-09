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
    public void Um_capitulo_por_grupo_da_historia_e_o_extra_fica_de_fora()
    {
        var chapters = BibleChapterPlanner.Plan(ProjectWithTwoChapters());

        Assert.Equal([1, 2], chapters.Select(c => c.Number));
        Assert.Equal([["yst1", "yst2"], ["yst3"]], chapters.Select(c => c.Scenes.Select(s => s.Key).ToArray()));
        Assert.DoesNotContain(chapters.SelectMany(c => c.Scenes), s => s.Key == "yst9");
    }

    [Fact]
    public void A_cena_leva_o_nome_do_usuario_ou_o_da_engine_e_nunca_o_da_biblia_anterior()
    {
        var project = ProjectWithTwoChapters();
        project.SaveBible(VNForge.Core.Bible.GameBible.From([new VNForge.Core.Bible.BibleScene("yst1") { Name = "Nome antigo da IA" }]));

        var scenes = BibleChapterPlanner.Plan(project).SelectMany(c => c.Scenes).ToDictionary(s => s.Key, s => s.Label);

        Assert.Equal("manha", scenes["yst1"]);
        Assert.Equal("Volta para casa", scenes["yst2"]);
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

    [Fact]
    public void Capitulo_que_cabe_nao_se_divide()
    {
        var chapter = Chapter("Manhã", 1, Scene("yst1", "a", "おはよう"));

        var parts = BibleChapterPlanner.Split(chapter, maxTokens: 9000);

        Assert.Same(chapter, Assert.Single(parts));
    }

    [Fact]
    public void O_texto_do_capitulo_e_estavel_e_traz_falante_e_fala_de_cada_cena()
    {
        var chapter = Chapter("Manhã", 1, Scene("yst1", "manha", "おはよう", "遅いぞ"));

        var text = chapter.Text();

        Assert.Equal(text, chapter.Text());
        Assert.Contains("Cena yst1 (manha)", text, StringComparison.Ordinal);
        Assert.Contains("美咲: おはよう", text, StringComparison.Ordinal);
        Assert.Contains("健太: 遅いぞ", text, StringComparison.Ordinal);
    }
}
