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
    public void Falante_da_linha_vai_para_a_previa_e_linha_sem_falante_fica_nula()
    {
        Save(
            new TextUnit("a001:000001", TextUnitKind.Dialogue, "美咲", "原文", "Olá", TranslationStatus.Translated),
            new TextUnit("a001:000002", TextUnitKind.Dialogue, "", "原文", "Oi", TranslationStatus.Translated));

        Assert.Equal("美咲", LinePreviewer.Create(_project, "a001:000001").Speaker);
        Assert.Null(LinePreviewer.Create(_project, "a001:000002").Speaker);
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
    public void Linha_fora_dos_kinds_traz_o_aviso_que_o_vnf_preview_mostra()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 3, "charWidth": "uniform" } }""");
        Save(Unit("a001:000001", "aaa bbb", TextUnitKind.Choice));

        var preview = LinePreviewer.Create(_project, "a001:000001");

        Assert.Equal(LinePreview.NotFormattedNotice, preview.Notice);
        Assert.Null(LinePreviewer.Create(_project.LoadFormatting(), Unit("a001:000001", null), "aaa bbb").Notice);
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
