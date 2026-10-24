using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests.Demo;

/// <summary>O projeto de demonstração tem que bater com os números dos artboards, porque as telas U1 a U9 são conferidas contra eles.</summary>
public class DemoProjectTests
{
    [Fact]
    public void Tem_312_scripts_e_48211_linhas_com_a_distribuicao_do_painel()
    {
        var texts = DemoProject.Texts;
        var counts = ProjectStatusReporter.Create(texts).Total;

        Assert.Equal(DemoProject.ScriptCount, texts.Count);
        Assert.Equal(DemoProject.LineCount, counts.Total);
        Assert.Equal(DemoProject.Counts, counts);
        Assert.Equal(18_320, counts.Untranslated);
        Assert.Equal(new StatusDistribution(21, 18, 6, 17, 38), StatusDistribution.Of(counts));
    }

    [Fact]
    public void Os_oito_scripts_da_tabela_tem_as_linhas_do_artboard()
    {
        Assert.Equal(["yst00012", "yst00013", "yst00014", "yst00015", "yst00016", "yst00017", "yst00090", "yst00091"], DemoProject.Named.Select(s => s.Key));
        foreach (var script in DemoProject.Named)
        {
            Assert.Equal(script.Lines, DemoProject.Texts[script.Key].Count);
        }

        var prologue = StatusCounts.Of(DemoProject.Texts["yst00012"]);
        Assert.Equal(330, prologue.Approved);
        Assert.Equal(82, prologue.Reviewed);
        Assert.Equal(0, prologue.Untranslated);
        Assert.Equal(121, DemoProject.Texts["yst00091"].Count(u => u.Status == TranslationStatus.Approved));
    }

    [Fact]
    public void Os_personagens_tem_as_linhas_da_tabela_e_somam_o_projeto()
    {
        var all = DemoProject.Texts.Values.SelectMany(u => u).ToList();

        Assert.Equal(23, DemoProject.Characters.Count);
        Assert.Equal(DemoProject.LineCount, DemoProject.Characters.Sum(c => c.Lines));
        Assert.Equal(14_884, all.Count(u => u.Speaker is null));
        Assert.Equal(8_120, all.Count(u => u.Speaker == "美咲"));
        Assert.Equal(6_431, all.Count(u => u.Speaker == "健太"));
        Assert.Equal(640, all.Count(u => u.Speaker == "健太の母"));
    }

    [Fact]
    public void O_qa_do_artboard_tem_2_erros_e_37_avisos_em_linhas_que_existem()
    {
        var report = DemoProject.Report;
        var ids = DemoProject.Texts.Values.SelectMany(u => u).ToDictionary(u => u.Id);

        Assert.Equal(2, report.Errors.Count);
        Assert.Equal(37, report.Warnings.Count);
        Assert.All(report.Issues, i => Assert.True(ids.ContainsKey(i.UnitId)));
        Assert.All(report.Errors, e => Assert.DoesNotContain(@"\p", ids[e.UnitId].Translation));
        Assert.All(report.Warnings, w => Assert.True(
            ids[w.UnitId].Translation!.Contains("  ", StringComparison.Ordinal) || ids[w.UnitId].Translation!.Contains(" !", StringComparison.Ordinal)));
    }

    [Fact]
    public void As_linhas_do_editor_trazem_os_problemas_do_qa()
    {
        var lines = DemoProject.Lines();

        Assert.Equal(DemoProject.ScriptCount, lines.Scripts.Count);
        Assert.Equal(39, DemoProject.Texts.Keys.SelectMany(k => lines.LinesOf(k)).Count(l => l.HasIssues));
    }

    [Fact]
    public void E_deterministico_e_nao_tem_texto_de_jogo()
    {
        Assert.Same(DemoProject.Texts, DemoProject.Texts);
        Assert.All(DemoProject.Texts.Values.SelectMany(u => u).Take(2_000), u => Assert.StartsWith("yst", u.Id));
    }
}
