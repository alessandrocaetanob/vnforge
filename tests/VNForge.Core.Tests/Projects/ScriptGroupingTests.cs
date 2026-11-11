using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ScriptGroupingTests
{

    private static string Key(int n) => $"yst{n:D5}";

    private static FileStatus File(int n, int japanese = 0, int machine = 0, int approved = 0, int reviewed = 0) =>
        new(Key(n), new StatusCounts(japanese, machine, 0, reviewed, approved));

    private static IReadOnlyList<FileStatus> Files(int count) => [.. Enumerable.Range(1, count).Select(n => File(n, japanese: 1))];

    private static ScriptNames Names(params (int N, string? Name, string? Folder)[] scripts) =>
        ScriptNames.Empty.WithDetected(scripts.ToDictionary(s => Key(s.N), s => new DetectedScript(s.Name, s.Folder), StringComparer.Ordinal));

    [Fact]
    public void Sem_nenhuma_pasta_numerada_cada_pasta_e_um_capitulo()
    {
        var names = Names((1, "a", "userscript/prologo"), (2, "b", "userscript/rota_a"), (3, "c", "userscript/rota_a"));

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Prologo", "Rota a"], groups.Select(g => g.Name));
        Assert.Equal([1, 2], groups.Select(g => g.Number));
    }

    [Fact]
    public void Centenas_de_prefixos_na_raiz_caem_nas_faixas_em_vez_de_virar_centenas_de_capitulos()
    {
        var names = Names([.. Enumerable.Range(1, 300).Select(n => (n, (string?)$"{n:D3}_cena", (string?)"userscript"))]);

        var groups = ScriptGrouping.Build(names, Files(300));

        Assert.Equal(8, groups.Count);
        Assert.All(groups, g => Assert.StartsWith("Parte ", g.Name, StringComparison.Ordinal));
        Assert.Equal(300, groups.Sum(g => g.ScriptKeys.Count));
    }

    [Fact]
    public void Um_unico_script_com_prefixo_numerico_nao_tira_a_historia_das_faixas()
    {
        var names = Names((1, "00_global", null));

        var groups = ScriptGrouping.Build(names, Files(131));

        AssertOnlyRanges(groups, 131);
        Assert.Equal(4, groups.Count);
    }

    private static void AssertOnlyRanges(IReadOnlyList<ScriptGroupTotals> groups, int scripts)
    {
        Assert.All(groups, g => Assert.StartsWith("Parte ", g.Name, StringComparison.Ordinal));
        Assert.DoesNotContain(groups, g => g.IsExtra);
        Assert.Equal(scripts, groups.Sum(g => g.ScriptKeys.Count));
    }

    [Fact]
    public void Sem_scripts_nao_ha_grupos()
    {
        Assert.Empty(ScriptGrouping.Build(ScriptNames.Empty, []));
    }

    [Fact]
    public void Renomear_um_grupo_guarda_o_novo_nome_em_cada_script_dele()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/01_cap1"), (3, "c", "userscript/02_cap2"));
        var files = Files(3);

        var renamed = ScriptGrouping.RenameGroup(names, files, "Capítulo 1", "Manhã de chuva");

        Assert.Equal("Manhã de chuva", renamed.Group(Key(1)));
        Assert.Equal("Manhã de chuva", renamed.Group(Key(2)));
        Assert.Null(renamed.Group(Key(3)));
        Assert.Equal(["Manhã de chuva", "Capítulo 2"], ScriptGrouping.Build(renamed, files).Select(g => g.Name));
        Assert.Same(names, ScriptGrouping.RenameGroup(names, files, "Capítulo 1", " "));
    }
}
