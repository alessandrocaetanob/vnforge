using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

/// <summary>Scripts sem nenhuma linha (#381): contam como prontos e nunca são o "Você está aqui" nem o próximo passo.</summary>
public class EmptyScenesTests
{
    private static string Key(int n) => $"yst{n:D5}";

    private static FileStatus File(int n, int japanese = 0, int machine = 0, int approved = 0) =>
        new(Key(n), new StatusCounts(japanese, machine, 0, 0, approved));

    private static ScriptNames Names(params (int N, string Folder)[] scripts) =>
        ScriptNames.Empty.WithDetected(scripts.ToDictionary(s => Key(s.N), s => new DetectedScript(null, s.Folder), StringComparer.Ordinal));

    [Fact]
    public void Script_sem_linhas_tem_progresso_completo_e_nao_conta_como_trabalho_da_IA()
    {
        var progress = new ScriptProgress(Key(1), new StatusCounts(0, 0, 0, 0, 0));

        Assert.True(progress.IsEmpty);
        Assert.Equal(100, progress.CompletedPercent);
        Assert.Equal(0, progress.MachinePercent);
    }

    [Fact]
    public void Script_com_linhas_continua_com_o_percentual_de_sempre()
    {
        var progress = new ScriptProgress(Key(1), new StatusCounts(1, 0, 0, 0, 1));

        Assert.False(progress.IsEmpty);
        Assert.Equal(50, progress.CompletedPercent);
    }

    [Fact]
    public void Capitulo_com_todas_as_cenas_vazias_tem_progresso_completo()
    {
        var names = Names((1, "userscript/01_vazios"), (2, "userscript/01_vazios"), (3, "userscript/02_cap2"));

        var chapters = ScriptGrouping.Build(names, [File(1), File(2), File(3, japanese: 4)]);

        var empty = chapters.Single(c => c.Name == "Vazios");
        Assert.True(empty.IsEmpty);
        Assert.True(empty.IsComplete);
        Assert.Equal(0, empty.Total);
        var other = chapters.Single(c => c.Name == "Capítulo 2");
        Assert.False(other.IsEmpty);
        Assert.False(other.IsComplete);
    }

    [Fact]
    public void Capitulo_com_uma_cena_vazia_e_outra_com_linhas_pendentes_nao_e_vazio_nem_completo()
    {
        var names = Names((1, "userscript/01_cap1"), (2, "userscript/01_cap1"));

        var chapter = ScriptGrouping.Build(names, [File(1), File(2, machine: 3)]).Single();

        Assert.False(chapter.IsEmpty);
        Assert.False(chapter.IsComplete);
    }

    [Fact]
    public void Capitulo_com_todas_as_linhas_prontas_esta_completo()
    {
        var names = Names((1, "userscript/01_cap1"));

        var chapter = ScriptGrouping.Build(names, [File(1, approved: 5)]).Single();

        Assert.True(chapter.IsComplete);
        Assert.False(chapter.IsEmpty);
    }

    [Fact]
    public void O_proximo_capitulo_pula_os_vazios_mesmo_que_o_criterio_os_aceite()
    {
        var names = Names((1, "userscript/01_vazios"), (2, "userscript/02_cap2"), (3, "userscript/03_cap3"));
        var chapters = ScriptGrouping.Build(names, [File(1), File(2, japanese: 4), File(3, japanese: 2)]);

        var next = ScriptGrouping.ChapterToWorkOn(chapters, here: null, _ => true);

        Assert.Equal("Capítulo 2", next?.Name);
    }

    [Fact]
    public void Capitulo_onde_a_pessoa_esta_tem_prioridade_se_ainda_tem_o_que_fazer()
    {
        var names = Names((1, "userscript/01_cap1"), (2, "userscript/02_cap2"));
        var chapters = ScriptGrouping.Build(names, [File(1, machine: 1), File(2, machine: 1)]);

        Assert.Equal("Capítulo 2", ScriptGrouping.ChapterToWorkOn(chapters, "Capítulo 2", c => c.ToReview > 0)?.Name);
        Assert.Equal("Capítulo 1", ScriptGrouping.ChapterToWorkOn(chapters, "Capítulo 2", c => c.Name == "Capítulo 1")?.Name);
    }

    [Fact]
    public void Capitulo_vazio_onde_a_pessoa_esta_nao_e_escolhido()
    {
        var names = Names((1, "userscript/01_vazios"), (2, "userscript/02_cap2"));
        var chapters = ScriptGrouping.Build(names, [File(1), File(2, japanese: 4)]);

        Assert.Equal("Capítulo 2", ScriptGrouping.ChapterToWorkOn(chapters, "Vazios", _ => true)?.Name);
    }

    [Fact]
    public void Se_todos_os_capitulos_sao_vazios_nenhum_e_escolhido()
    {
        var names = Names((1, "userscript/01_vazios"), (2, "userscript/02_vazios2"));
        var chapters = ScriptGrouping.Build(names, [File(1), File(2)]);

        Assert.All(chapters, c => Assert.True(c.IsComplete));
        Assert.Null(ScriptGrouping.ChapterToWorkOn(chapters, here: null, _ => true));
    }
}
