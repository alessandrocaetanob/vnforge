using VNForge.AI.Translation;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.AI.Tests.Translation;

public sealed class LineSelectionTests : IDisposable
{
    private const string Key = "yst00001";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "vnforge-ai-tests", Guid.NewGuid().ToString("N"));
    private readonly ProjectFolder _project;

    public LineSelectionTests()
    {
        Directory.CreateDirectory(_root);
        _project = ProjectFolder.Create(_root, new ProjectConfig(
            "Teste",
            new GameConfig("D:/Jogos/Qualquer", "Qualquer.exe"),
            new EngineConfig("yuris", 500, "0xD36FAC96"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        _project.SaveText(Key, [.. Enumerable.Range(0, 5).Select(i => new TextUnit($"{Key}:{i:D6}", TextUnitKind.Dialogue, null, $"原文{i}", null, TranslationStatus.Untranslated))]);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static string Id(int i) => $"{Key}:{i:D6}";

    private LineSelection Select(Func<TextUnit, bool>? wanted = null) =>
        LineSelection.Build(_project.LoadAllText(), null, null, wanted ?? (u => u.Status == TranslationStatus.Untranslated));

    private static TextUnit? Machine(TranslationResult result, TextUnit unit) =>
        unit with { Translation = result.Translation, Status = TranslationStatus.Machine };

    [Fact]
    public void Grava_o_resultado_nas_linhas_que_continuam_como_na_selecao()
    {
        var selection = Select();

        var saved = selection.ApplyAndSave(_project, [new TranslationResult(Id(0), "zero"), new TranslationResult(Id(1), "um")], Machine);

        Assert.Equal(0, saved.Skipped);
        Assert.Equal(Key, saved.LastKey);
        Assert.Equal(["zero", "um", null, null, null], _project.LoadText(Key).Select(u => u.Translation));
    }

    [Fact]
    public void Linha_traduzida_a_mao_depois_da_selecao_nao_e_sobrescrita_e_conta_como_pulada()
    {
        var selection = Select();
        new LineEditor(_project, TimeProvider.System).SetTranslation(Key, Id(1), "à mão");

        var saved = selection.ApplyAndSave(_project, [new TranslationResult(Id(0), "zero"), new TranslationResult(Id(1), "um")], Machine);

        var units = _project.LoadText(Key);
        Assert.Equal(1, saved.Skipped);
        Assert.Equal("zero", units[0].Translation);
        Assert.Equal("à mão", units[1].Translation);
        Assert.Equal(TranslationStatus.Translated, units[1].Status);
    }

    [Fact]
    public void Edicao_de_outra_linha_e_as_notas_da_linha_gravada_sobrevivem()
    {
        var selection = Select();
        var editor = new LineEditor(_project, TimeProvider.System);
        editor.SetTranslation(Key, Id(3), "outra");
        editor.SetNotes(Key, Id(0), "cuidado");

        selection.ApplyAndSave(_project, [new TranslationResult(Id(0), "zero")], Machine);

        var units = _project.LoadText(Key);
        Assert.Equal("outra", units[3].Translation);
        Assert.Equal("cuidado", units[0].Notes);
        Assert.Equal("zero", units[0].Translation);
    }

    [Fact]
    public void Original_trocado_depois_da_selecao_pula_a_linha()
    {
        var selection = Select();
        var units = _project.LoadText(Key).ToList();
        units[2] = units[2] with { Source = "novo original" };
        _project.SaveText(Key, units);

        var saved = selection.ApplyAndSave(_project, [new TranslationResult(Id(2), "dois")], Machine);

        Assert.Equal(1, saved.Skipped);
        Assert.Null(_project.LoadText(Key)[2].Translation);
    }

    [Fact]
    public void Retraducao_de_linha_de_maquina_so_vale_se_ela_nao_mudou()
    {
        var units = _project.LoadText(Key).ToList();
        units[0] = units[0] with { Translation = "antiga", Status = TranslationStatus.Machine };
        units[1] = units[1] with { Translation = "antiga", Status = TranslationStatus.Machine };
        _project.SaveText(Key, units);
        var selection = Select(u => u.Status == TranslationStatus.Machine);
        new LineEditor(_project, TimeProvider.System).Approve(Key, Id(1));

        var saved = selection.ApplyAndSave(_project, [new TranslationResult(Id(0), "nova"), new TranslationResult(Id(1), "nova")], Machine);

        var after = _project.LoadText(Key);
        Assert.Equal(1, saved.Skipped);
        Assert.Equal("nova", after[0].Translation);
        Assert.Equal(TranslationStatus.Approved, after[1].Status);
        Assert.Equal("antiga", after[1].Translation);
    }

    [Fact]
    public void Resultado_que_nao_muda_a_linha_nao_regrava_o_arquivo()
    {
        var selection = Select();
        var before = File.GetLastWriteTimeUtc(_project.TextPath(Key));
        File.SetLastWriteTimeUtc(_project.TextPath(Key), before.AddHours(-1));
        var marker = File.GetLastWriteTimeUtc(_project.TextPath(Key));

        var saved = selection.ApplyAndSave(_project, [new TranslationResult(Id(0), "x")], (_, _) => null);

        Assert.Equal(0, saved.Skipped);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(_project.TextPath(Key)));
    }

    [Fact]
    public void O_limite_e_o_filtro_de_script_valem_na_selecao()
    {
        var limited = LineSelection.Build(_project.LoadAllText(), null, 2, u => true);
        var filtered = LineSelection.Build(_project.LoadAllText(), key => key == "outro", null, u => true);

        Assert.Equal(2, limited.Pending.Count);
        Assert.Empty(filtered.Pending);
        Assert.Equal(0, filtered.FileCount);
    }
}
