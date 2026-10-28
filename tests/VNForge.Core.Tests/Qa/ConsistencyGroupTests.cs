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
    public void A_mais_usada_e_a_referencia_e_no_empate_vale_a_que_aparece_primeiro()
    {
        var empate = Assert.Single(ConsistencyGroups.Find(Context(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"))));
        var maioria = Assert.Single(ConsistencyGroups.Find(Context(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"), Unit("a:3", "はい", "Isso"))));

        Assert.Equal("Sim", empate.Reference.Translation);
        Assert.Equal("Isso", maioria.Reference.Translation);
        Assert.True(maioria.Reference.IsReference);
        Assert.All(maioria.Variants.Skip(1), v => Assert.False(v.IsReference));
    }

    [Fact]
    public void A_diferenca_de_cada_variante_e_contra_a_referencia_e_a_referencia_nao_tem_diferenca()
    {
        var group = Assert.Single(ConsistencyGroups.Find(Context(
            Unit("a:1", "はい", "Oi mundo."), Unit("a:2", "はい", "Oi mundo."), Unit("a:3", "はい", "Oi  mundo.")))).Variants;

        Assert.Equal([new TextDiffSegment("Oi mundo.", TextDiffKind.Same)], group[0].Diff);
        Assert.Contains(group[1].Diff, s => s is { Kind: TextDiffKind.Added, Text: " " });
    }

    [Fact]
    public void Originais_com_uma_traducao_so_linhas_sem_traducao_e_originais_vazios_nao_formam_grupo()
    {
        var context = Context(
            Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", " Sim "), Unit("a:3", "はい", null),
            Unit("a:4", "いいえ", "Não"), Unit("a:5", "  ", "x"), Unit("a:6", "  ", "y"));

        Assert.Empty(ConsistencyGroups.Find(context));
    }

    [Fact]
    public void Os_grupos_saem_na_ordem_em_que_o_original_aparece_primeiro()
    {
        var context = Context(
            Unit("a:1", "二", "Dois"), Unit("a:2", "一", "Um"), Unit("a:3", "二", "Duas"), Unit("a:4", "一", "Uma"));

        Assert.Equal(["二", "一"], ConsistencyGroups.Find(context).Select(g => g.Source));
    }

    [Fact]
    public void A_regra_aponta_so_as_linhas_das_variantes_que_fogem_da_referencia()
    {
        var context = Context(
            Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Sim"), Unit("a:3", "はい", "Isso"), Unit("b:1", "はい", "Aham"));

        var issues = new InconsistentTranslationRule().Check(context).ToList();

        Assert.Equal(["a:3", "b:1"], issues.Select(i => i.UnitId));
        Assert.All(issues, i => Assert.Contains("Sim", i.Message));
    }

    [Fact]
    public void A_impressao_digital_muda_com_o_original_com_uma_traducao_e_com_a_linha_de_uma_variante()
    {
        var base_ = Assert.Single(ConsistencyGroups.Find(Context(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso")))).Fingerprint;

        string Fingerprint(params TextUnit[] units) => Assert.Single(ConsistencyGroups.Find(Context(units))).Fingerprint;

        Assert.Equal(base_, Fingerprint(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso")));
        Assert.NotEqual(base_, Fingerprint(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso.")));
        Assert.NotEqual(base_, Fingerprint(Unit("a:1", "はい。", "Sim"), Unit("a:2", "はい。", "Isso")));
        Assert.NotEqual(base_, Fingerprint(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"), Unit("a:3", "はい", "Isso")));
    }

    [Fact]
    public void Manter_diferentes_grava_a_marca_em_todas_as_linhas_do_grupo_e_a_regra_para_de_apontar()
    {
        var project = Project(Unit("a:1", "はい", "Sim"), Unit("b:1", "はい", "Isso"), Unit("b:2", "はい", "Isso"), Unit("b:3", "いいえ", "Não"));
        var group = Assert.Single(Groups(project));

        var written = ConsistencyFlags.Keep(project, group);

        Assert.Equal(3, written);
        Assert.All(new[] { ("a", "a:1"), ("b", "b:1"), ("b", "b:2") }, x => Assert.Contains(ConsistencyGroups.FlagFor(group), project.LoadText(x.Item1).Single(u => u.Id == x.Item2).Flags));
        Assert.Empty(project.LoadText("b").Single(u => u.Id == "b:3").Flags);
        Assert.True(Assert.Single(Groups(project)).IsKept);
        Assert.Empty(new InconsistentTranslationRule().Check(QaContext.ForTexts(project.LoadAllText())));
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
    public void O_grupo_volta_quando_uma_das_traducoes_muda()
    {
        var project = Project(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"));
        ConsistencyFlags.Keep(project, Assert.Single(Groups(project)));
        var units = project.LoadText("a").ToList();
        units[1] = units[1] with { Translation = "Aquilo" };
        project.SaveText("a", units);

        var issue = Assert.Single(new InconsistentTranslationRule().Check(QaContext.ForTexts(project.LoadAllText())));

        Assert.Equal("a:2", issue.UnitId);
        Assert.False(Assert.Single(Groups(project)).IsKept);
    }

    [Fact]
    public void O_grupo_volta_quando_o_original_de_uma_linha_muda()
    {
        var project = Project(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"), Unit("a:3", "はい", "Aquilo"));
        ConsistencyFlags.Keep(project, Assert.Single(Groups(project)));
        var units = project.LoadText("a").ToList();
        units[2] = units[2] with { Source = "はい。" };
        project.SaveText("a", units);

        Assert.NotEmpty(new InconsistentTranslationRule().Check(QaContext.ForTexts(project.LoadAllText())));
    }

    [Fact]
    public void Manter_diferentes_de_novo_troca_a_marca_velha_sem_acumular()
    {
        var project = Project(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso"));
        ConsistencyFlags.Keep(project, Assert.Single(Groups(project)));
        var units = project.LoadText("a").ToList();
        units[1] = units[1] with { Translation = "Aquilo" };
        project.SaveText("a", units);

        ConsistencyFlags.Keep(project, Assert.Single(Groups(project)));

        Assert.All(project.LoadText("a"), u => Assert.Single(u.Flags, f => f.StartsWith(ConsistencyGroups.FlagPrefix, StringComparison.Ordinal)));
        Assert.True(Assert.Single(Groups(project)).IsKept);
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

    [Fact]
    public void Marcar_ok_numa_linha_continua_silenciando_so_o_aviso_dela()
    {
        var context = Context(Unit("a:1", "はい", "Sim"), Unit("a:2", "はい", "Isso", "traducao-inconsistente"), Unit("a:3", "はい", "Aquilo"));

        var report = QaEngine.Default.Run(context, new QaOptions(["traducao-inconsistente"], QaSeverity.Warning));

        Assert.Equal(["a:3"], report.Issues.Select(i => i.UnitId));
        Assert.Equal(["a:2"], report.Silenced.Select(i => i.UnitId));
    }
}
