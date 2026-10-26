using VNForge.Core.Projects;

namespace VNForge.Core.Tests.Projects;

public class ScriptGroupingTests
{
    private static readonly int[] UnorderedNumbers = [100_000, 9, 10];

    private static string Key(int n) => $"yst{n:D5}";

    private static FileStatus File(int n, int japanese = 0, int machine = 0, int approved = 0, int reviewed = 0) =>
        new(Key(n), new StatusCounts(japanese, machine, 0, reviewed, approved));

    private static IReadOnlyList<FileStatus> Files(int count) => [.. Enumerable.Range(1, count).Select(n => File(n, japanese: 1))];

    private static ScriptNames Names(params (int N, string? Name, string? Folder)[] scripts) =>
        ScriptNames.Empty.WithDetected(scripts.ToDictionary(s => Key(s.N), s => new DetectedScript(s.Name, s.Folder), StringComparer.Ordinal));

    [Fact]
    public void Pelas_pastas_do_yst_list_sem_a_pasta_comum_a_todos()
    {
        var names = Names(
            (1, "manha", "userscript/01_cap1"),
            (2, "tarde", "userscript/01_cap1"),
            (3, "noite", "userscript/02_cap2"));

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Capítulo 1", "Capítulo 2"], groups.Select(g => g.Name));
        Assert.Equal([1, 2], groups.Select(g => g.Number));
        Assert.Equal([Key(1), Key(2)], groups[0].ScriptKeys);
        Assert.Equal([Key(3)], groups[1].ScriptKeys);
    }

    [Fact]
    public void O_nome_do_capitulo_vem_do_que_sobra_depois_do_numero()
    {
        var names = Names(
            (1, "a", "userscript/01_prologo"),
            (2, "b", "userscript/02_manha_de_chuva"),
            (3, "c", "userscript/03"));

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Prologo", "Manha de chuva", "Capítulo 3"], groups.Select(g => g.Name));
    }

    [Fact]
    public void Pastas_sem_numero_no_meio_de_pastas_com_numero_e_scripts_sem_pasta_vao_para_menus_e_nomes_no_fim()
    {
        var names = Names(
            (1, "a", "userscript/system"),
            (2, "b", "userscript/01_cap1"),
            (3, "c", "userscript/02_cap2"),
            (4, "d", null));

        var groups = ScriptGrouping.Build(names, Files(4));

        Assert.Equal(["Capítulo 1", "Capítulo 2", ScriptGrouping.ExtraName], groups.Select(g => g.Name));
        Assert.Equal([1, 2, null], groups.Select(g => g.Number));
        Assert.True(groups[2].IsExtra);
        Assert.Equal([Key(1), Key(4)], groups[2].ScriptKeys);
    }

    [Fact]
    public void Sem_nenhuma_pasta_numerada_cada_pasta_e_um_capitulo()
    {
        var names = Names((1, "a", "userscript/prologo"), (2, "b", "userscript/rota_a"), (3, "c", "userscript/rota_a"));

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Prologo", "Rota a"], groups.Select(g => g.Name));
        Assert.Equal([1, 2], groups.Select(g => g.Number));
    }

    [Fact]
    public void Scripts_soltos_na_raiz_agrupam_pelo_prefixo_numerico_do_arquivo()
    {
        var names = Names(
            (1, "00_global", "userscript"),
            (2, "01_prologo", "userscript"),
            (3, "01_prologo_b", "userscript"),
            (4, "02_cap1", "userscript"));

        var groups = ScriptGrouping.Build(names, Files(4));

        Assert.Equal(["Capítulo 0", "Capítulo 1", "Capítulo 2"], groups.Select(g => g.Name));
        Assert.Equal([Key(2), Key(3)], groups[1].ScriptKeys);
    }

    [Fact]
    public void Sem_yst_list_agrupa_em_faixas_na_ordem_numerica_dos_scripts()
    {
        var groups = ScriptGrouping.Build(ScriptNames.Empty, Files(100));

        Assert.Equal(["Parte 1", "Parte 2", "Parte 3"], groups.Select(g => g.Name));
        Assert.Equal([34, 34, 32], groups.Select(g => g.ScriptKeys.Count));
        Assert.Equal(Key(1), groups[0].ScriptKeys[0]);
        Assert.Equal(Key(100), groups[2].ScriptKeys[^1]);
    }

    [Fact]
    public void Um_jogo_pequeno_sem_pastas_e_uma_parte_so_e_a_ordem_e_numerica_nao_alfabetica()
    {
        var files = UnorderedNumbers.Select(n => new FileStatus($"yst{n}", new StatusCounts(1, 0, 0, 0, 0))).ToList();

        var group = Assert.Single(ScriptGrouping.Build(ScriptNames.Empty, files));

        Assert.Equal(["yst9", "yst10", "yst100000"], group.ScriptKeys);
    }

    [Fact]
    public void Um_jogo_enorme_sem_pastas_nao_passa_do_maximo_de_faixas()
    {
        var groups = ScriptGrouping.Build(ScriptNames.Empty, Files(2_000));

        Assert.Equal(ScriptGrouping.MaxRanges, groups.Count);
        Assert.Equal(2_000, groups.Sum(g => g.ScriptKeys.Count));
    }

    [Fact]
    public void O_grupo_escolhido_pela_pessoa_vale_mais_que_a_pasta()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/01_cap1"), (3, "c", "userscript/02_cap2"))
            .WithGroup(Key(2), "Manhã de chuva");

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Capítulo 1", "Manhã de chuva", "Capítulo 2"], groups.Select(g => g.Name));
        Assert.Equal([Key(2)], groups[1].ScriptKeys);
    }

    [Fact]
    public void Pastas_numeradas_ja_agrupadas_pela_pessoa_ainda_deixam_as_pastas_sem_numero_em_menus_e_nomes()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/system"), (3, "c", "userscript/02_cap2"))
            .WithGroup(Key(1), "Manhã de chuva")
            .WithGroup(Key(3), "O clube de leitura");

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Manhã de chuva", "O clube de leitura", ScriptGrouping.ExtraName], groups.Select(g => g.Name));
        Assert.Equal([1, 2, null], groups.Select(g => g.Number));
    }

    [Fact]
    public void Renomear_uma_faixa_nao_renumera_nem_redistribui_as_outras()
    {
        var files = Files(120);
        var before = ScriptGrouping.Build(ScriptNames.Empty, files);

        var renamed = ScriptGrouping.RenameGroup(ScriptNames.Empty, files, "Parte 1", "Prólogo");
        var after = ScriptGrouping.Build(renamed, files);

        Assert.Equal(["Parte 1", "Parte 2", "Parte 3"], before.Select(g => g.Name));
        Assert.Equal(["Prólogo", "Parte 2", "Parte 3"], after.Select(g => g.Name));
        Assert.Equal(before.Select(g => g.ScriptKeys), after.Select(g => g.ScriptKeys));
    }

    [Fact]
    public void Reagrupar_um_script_de_uma_faixa_nao_muda_o_tamanho_das_outras()
    {
        var files = Files(100);
        var before = ScriptGrouping.Build(ScriptNames.Empty, files);

        var moved = ScriptGrouping.Regroup(ScriptNames.Empty, [Key(68)], "Parte 1");
        var after = ScriptGrouping.Build(moved, files);

        Assert.Equal(before[0].ScriptKeys.Count + 1, after[0].ScriptKeys.Count);
        Assert.Contains(Key(68), after[0].ScriptKeys);
        Assert.Equal(before[2].ScriptKeys, after[2].ScriptKeys);
        Assert.Equal(before[1].ScriptKeys.Where(k => k != Key(68)), after[1].ScriptKeys);
    }

    [Fact]
    public void Uma_pasta_fora_da_comum_nao_vira_capitulo_e_os_capitulos_numerados_continuam()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/02_cap2"), (3, "c", "system"));

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Capítulo 1", "Capítulo 2", ScriptGrouping.ExtraName], groups.Select(g => g.Name));
        Assert.Equal([Key(3)], groups[2].ScriptKeys);
    }

    [Fact]
    public void Com_pasta_preenchida_sem_numero_o_prefixo_numerico_do_nome_decide()
    {
        var names = Names((1, "01_prologo", "userscript/ys"), (2, "02_cap1", "userscript/ys"), (3, "menu", "userscript/ys"));

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal(["Capítulo 1", "Capítulo 2", ScriptGrouping.ExtraName], groups.Select(g => g.Name));
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
    public void Dois_scripts_com_o_mesmo_grupo_escolhido_ficam_juntos_sem_diferenciar_caixa()
    {
        var names = ScriptNames.Empty.WithGroup(Key(1), "Festa").WithGroup(Key(3), "festa");

        var groups = ScriptGrouping.Build(names, Files(3));

        Assert.Equal("Festa", groups[0].Name);
        Assert.Equal([Key(1), Key(3)], groups[0].ScriptKeys);
        Assert.Equal("Parte 1", groups[1].Name);
        Assert.Equal([Key(2)], groups[1].ScriptKeys);
    }

    [Fact]
    public void Um_grupo_chamado_menus_e_nomes_e_o_ultimo_e_nao_tem_numero()
    {
        var names = ScriptNames.Empty.WithGroup(Key(1), "menus e nomes");

        var groups = ScriptGrouping.Build(names, Files(2));

        Assert.Equal(["Parte 1", ScriptGrouping.ExtraName], groups.Select(g => g.Name));
        Assert.Null(groups[1].Number);
    }

    [Fact]
    public void Os_totais_somam_pronto_para_revisar_e_japones()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/01_cap1"));
        var files = new[]
        {
            new FileStatus(Key(1), new StatusCounts(Untranslated: 5, Machine: 3, Translated: 2, Reviewed: 7, Approved: 11)),
            new FileStatus(Key(2), new StatusCounts(Untranslated: 1, Machine: 1, Translated: 0, Reviewed: 0, Approved: 4)),
        };

        var group = Assert.Single(ScriptGrouping.Build(names, files));

        Assert.Equal(22, group.Ready);
        Assert.Equal(6, group.ToReview);
        Assert.Equal(6, group.Japanese);
        Assert.Equal(34, group.Total);
    }

    [Fact]
    public void Script_que_nao_esta_nos_textos_nao_entra_na_conta()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (9, "fantasma", "userscript/09_cap9"));

        var group = Assert.Single(ScriptGrouping.Build(names, [File(1, japanese: 2)]));

        Assert.Equal("Capítulo 1", group.Name);
        Assert.Equal(2, group.Total);
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

    [Fact]
    public void Reagrupar_move_os_scripts_e_um_nome_vazio_os_devolve_a_pasta()
    {
        var names = Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/02_cap2"));
        var files = Files(2);

        var moved = ScriptGrouping.Regroup(names, [Key(2)], "Capítulo 1");
        var back = ScriptGrouping.Regroup(moved, [Key(2)], null);

        var group = Assert.Single(ScriptGrouping.Build(moved, files));
        Assert.Equal([Key(1), Key(2)], group.ScriptKeys);
        Assert.Equal(["Capítulo 1", "Capítulo 2"], ScriptGrouping.Build(back, files).Select(g => g.Name));
    }

    [Fact]
    public void A_renomeacao_fica_guardada_no_scripts_json_do_projeto()
    {
        using var temp = new TempFolder();
        var project = ProjectFolder.Create(temp.Path, new ProjectConfig(
            "Minha VN",
            new GameConfig(Path.Combine(Path.GetTempPath(), "Jogos", "MinhaVN"), "MinhaVN.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        var files = Files(2);
        project.SaveScriptNames(Names((1, "a", "userscript/01_cap1"), (2, "b", "userscript/02_cap2")));

        project.SaveScriptNames(ScriptGrouping.RenameGroup(project.LoadScriptNames(), files, "Capítulo 2", "O clube de leitura"));

        var reloaded = project.LoadScriptNames();
        Assert.Equal(["Capítulo 1", "O clube de leitura"], ScriptGrouping.Build(reloaded, files).Select(g => g.Name));
        Assert.Contains("\"group\": \"O clube de leitura\"", System.IO.File.ReadAllText(project.ScriptNamesPath), StringComparison.Ordinal);
    }
}
