using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class LinePreviewerTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public LinePreviewerTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig("D:/Jogos/Teste", "Jogo.exe"),
                new EngineConfig("yuris", 500, "0xD36FAC96"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("fold"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private void Save(params TextUnit[] units) => _project.SaveText("a001", units);

    private void SaveProfile(string json) => File.WriteAllText(_project.FormattingPath, json);

    private static TextUnit Unit(string id, string? translation, TextUnitKind kind = TextUnitKind.Dialogue) =>
        new(id, kind, null, "原文", translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    [Fact]
    public void Quebra_ligada_mostra_o_texto_quebrado_e_as_colunas_de_cada_linha()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 3, "charWidth": "uniform" } }""");
        Save(Unit("a001:000001", "um dois tres quatro"));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.Equal("um dois\\ntres\\nquatro", preview.Text);
        Assert.Equal("um dois tres quatro", preview.Translation);
        Assert.True(preview.Formatted);
        Assert.Equal([[7, 4, 6]], preview.Columns.Select(b => b.ToArray()));
        Assert.Equal(3, preview.LineCount);
        Assert.Equal(10, preview.MaxWidth);
        Assert.Empty(preview.Warnings);
    }

    [Fact]
    public void Colunas_seguem_a_medida_do_charWidth_do_perfil()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 40, "charWidth": "halfWidthLatin" } }""");
        Save(Unit("a001:000001", "abc\\c日本"));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.Equal([[7]], preview.Columns.Select(b => b.ToArray()));
    }

    [Fact]
    public void Caixa_acima_do_limite_gera_aviso_e_pagina_abre_outra_caixa()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 1, "charWidth": "uniform" } }""");
        Save(Unit("a001:000001", "aaa bbb\\pcc"));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.Equal([[3, 3], [2]], preview.Columns.Select(b => b.ToArray()));
        var warning = Assert.Single(preview.Warnings);
        Assert.Contains("caixa 1", warning);
    }

    [Fact]
    public void Sem_perfil_o_texto_fica_como_esta_e_so_mede()
    {
        Save(Unit("a001:000001", "oi\\nmundo"));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.Equal("oi\\nmundo", preview.Text);
        Assert.False(preview.WrapEnabled);
        Assert.Equal([[2, 5]], preview.Columns.Select(b => b.ToArray()));
    }

    [Fact]
    public void Papel_fora_dos_kinds_do_perfil_nao_e_formatado()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 3, "charWidth": "uniform" } }""");
        Save(Unit("a001:000001", "aaa bbb", TextUnitKind.Choice));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.False(preview.Formatted);
        Assert.Equal("aaa bbb", preview.Text);
    }

    [Fact]
    public void Traducao_igual_ao_original_nao_e_formatada_como_no_build()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 3, "charWidth": "uniform" } }""");
        Save(new TextUnit("a001:000001", TextUnitKind.Dialogue, null, "aaa bbb", "aaa bbb", TranslationStatus.Translated));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.False(preview.Formatted);
        Assert.Equal("aaa bbb", preview.Text);
    }

    [Fact]
    public void Boxes_trazem_o_texto_e_as_colunas_de_cada_linha_sem_os_marcadores_de_quebra()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 10, "maxLines": 3, "charWidth": "uniform" } }""");
        Save(Unit("a001:000001", "um dois tres quatro\\pfim\\c!"));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.Equal([["um dois", "tres", "quatro"], ["fim\\c!"]], preview.Boxes.Select(b => b.Select(l => l.Text).ToArray()));
        Assert.Equal([[7, 4, 6], [4]], preview.Boxes.Select(b => b.Select(l => l.Columns).ToArray()));
    }

    [Fact]
    public void Texto_em_edicao_quebra_nos_mesmos_pontos_que_a_linha_gravada()
    {
        const string Profile = """{ "wrap": { "enabled": true, "maxWidth": 12, "maxLines": 2, "charWidth": "uniform" }, "typography": { "ellipsis": "dots" } }""";
        const string Edited = "Isto… é um texto bem longo para a caixa\\pOutra caixa curta";
        SaveProfile(Profile);
        Save(Unit("a001:000001", Edited));
        var saved = LinePreviewer.Create(_project, "a001:000001");

        var unsaved = LinePreviewer.Create(_project.LoadFormatting(), Unit("a001:000001", "antigo"), Edited);

        Assert.Equal(saved.Text, unsaved.Text);
        Assert.Equal(saved.Columns, unsaved.Columns);
        Assert.Equal(saved.Warnings, unsaved.Warnings);
        Assert.Equal(saved.MaxLines, unsaved.MaxLines);
    }

    [Fact]
    public void Texto_em_edicao_de_linha_ainda_sem_traducao_ja_e_formatado_como_o_build_fara_ao_gravar()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "charWidth": "uniform" } }""");

        var preview = LinePreviewer.Create(_project.LoadFormatting(), Unit("a001:000001", null), "aaa bbb");

        Assert.True(preview.Formatted);
        Assert.Equal("aaa\\nbbb", preview.Text);
    }

    [Fact]
    public void Id_inexistente_e_linha_sem_traducao_lancam_erro_em_portugues()
    {
        Save(Unit("a001:000001", null));

        var missing = Assert.Throws<VNForgeException>(() => LinePreviewer.Create(_project, "a001:999999"));
        Assert.Contains("a001:999999", missing.Message);
        var untranslated = Assert.Throws<VNForgeException>(() => LinePreviewer.Create(_project, "a001:000001"));
        Assert.Contains("não tem tradução", untranslated.Message);
    }
}
