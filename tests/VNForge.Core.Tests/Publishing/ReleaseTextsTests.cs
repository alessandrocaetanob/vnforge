using VNForge.Core.Projects;
using VNForge.Core.Publishing;
using VNForge.Core.Text;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Publishing;

public sealed class ReleaseTextsTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public ReleaseTextsTests()
    {
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(Path.Combine(_temp.Path, "jogo"), "jogo.exe"),
                new EngineConfig("yuris"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
    }

    public void Dispose() => _temp.Dispose();

    private static TextUnit Unit(string id, string? translation, string? speaker = null) =>
        new(id, speaker is null ? TextUnitKind.Narration : TextUnitKind.Dialogue, speaker, "原文" + id, translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static IReadOnlyDictionary<string, IReadOnlyList<TextUnit>> Texts(params (string Key, TextUnit[] Units)[] scenes) =>
        scenes.ToDictionary(s => s.Key, s => (IReadOnlyList<TextUnit>)s.Units, StringComparer.Ordinal);

    private static ChangesSinceTest Changed(params ChangedScene[] scenes) => new(ChangesSinceTestState.Changed, null, false, scenes);

    [Fact]
    public void Guarda_so_as_falas_que_o_build_grava_com_o_falante_na_frente_e_le_de_volta()
    {
        var texts = Texts(("yst00001", [Unit("a", "Olá.", "Aoi"), Unit("b", null), Unit("c", "Chovia.")]));

        ReleaseTexts.Save(_project, "1.1.0", texts);
        var loaded = ReleaseTexts.Load(_project, "1.1.0");

        Assert.NotNull(loaded);
        Assert.Equal(["a", "c"], loaded["yst00001"].Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Aoi: Olá.", loaded["yst00001"]["a"]);
        Assert.Equal("Chovia.", loaded["yst00001"]["c"]);
        Assert.True(File.Exists(Path.Combine(_project.Root, "releases", "texts", "1.1.0.json")));
    }

    [Fact]
    public void O_texto_das_versoes_fica_fora_do_historico_que_o_lancamento_le()
    {
        ReleaseTexts.Save(_project, "1.1.0", Texts(("yst00001", [Unit("a", "Olá.")])));

        Assert.Empty(ReleaseService.LoadRecords(_project));
    }

    [Fact]
    public void Versao_sem_texto_guardado_nao_tem_o_que_carregar()
    {
        Assert.Null(ReleaseTexts.Load(_project, "1.0.0"));
    }

    [Fact]
    public void Arquivo_ilegivel_vale_como_texto_nao_guardado()
    {
        var path = Path.Combine(_project.Root, "releases", "texts", "1.0.0.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ isso não é json");

        Assert.Null(ReleaseTexts.Load(_project, "1.0.0"));
    }

    [Fact]
    public void Exemplos_pegam_uma_fala_por_cena_com_o_antes_o_depois_e_a_posicao_na_cena()
    {
        var before = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["yst00001"] = new Dictionary<string, string> { ["a"] = "Antes A.", ["b"] = "Antes B." },
            ["yst00002"] = new Dictionary<string, string> { ["x"] = "Aoi: Vamos logo." },
        };
        var texts = Texts(
            ("yst00001", [Unit("a", "Antes A."), Unit("b", "Depois B."), Unit("c", "Nova C.")]),
            ("yst00002", [Unit("w", "Intocada."), Unit("x", "Vamos, antes que o sinal toque.", "Aoi")]));
        var changes = Changed(new ChangedScene("yst00001", 1, 2, ["b", "c"]), new ChangedScene("yst00002", 2, 1, ["x"]));

        var examples = ReleaseTexts.Examples(before, texts, changes, max: 3);

        Assert.Equal(
            [new ChangeExample(1, 2, "Antes B.", "Depois B."), new ChangeExample(2, 2, "Aoi: Vamos logo.", "Aoi: Vamos, antes que o sinal toque.")],
            examples);
    }

    [Fact]
    public void Exemplos_levam_o_nome_da_cena_quando_o_projeto_tem_e_ficam_sem_quando_nao()
    {
        var before = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["yst00001"] = new Dictionary<string, string> { ["a"] = "Antes." },
            ["yst00002"] = new Dictionary<string, string> { ["a"] = "Antes." },
        };
        var texts = Texts(("yst00001", [Unit("a", "Depois.")]), ("yst00002", [Unit("a", "Depois.")]));
        var changes = Changed(new ChangedScene("yst00001", 1, 1, ["a"]), new ChangedScene("yst00002", 2, 1, ["a"]));
        var names = ScriptNames.From([new ScriptNameEntry("yst00002", "manha", "Manhã de chuva")]);

        var examples = ReleaseTexts.Examples(before, texts, changes, max: 3, names);

        Assert.Equal([null, "Manhã de chuva"], examples.Select(e => e.SceneName));
        Assert.All(ReleaseTexts.Examples(before, texts, changes, max: 3), e => Assert.Null(e.SceneName));
    }

    [Fact]
    public void Fala_nova_vem_sem_o_antes()
    {
        var before = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        var texts = Texts(("yst00001", [Unit("a", "Nova.")]));

        var example = Assert.Single(ReleaseTexts.Examples(before, texts, Changed(new ChangedScene("yst00001", 1, 1, ["a"])), max: 3));

        Assert.Null(example.Before);
        Assert.Equal("Nova.", example.After);
    }

    [Fact]
    public void Exemplos_respeitam_o_limite_de_cenas_e_pulam_a_fala_que_perdeu_a_traducao()
    {
        var before = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["yst00001"] = new Dictionary<string, string> { ["a"] = "Tinha." } };
        var texts = Texts(
            ("yst00001", [Unit("a", null)]),
            ("yst00002", [Unit("a", "Dois.")]),
            ("yst00003", [Unit("a", "Três.")]));
        var changes = Changed(new ChangedScene("yst00001", 1, 1, ["a"]), new ChangedScene("yst00002", 2, 1, ["a"]), new ChangedScene("yst00003", 3, 1, ["a"]));

        var examples = ReleaseTexts.Examples(before, texts, changes, max: 1);

        Assert.Equal([2], examples.Select(e => e.SceneNumber));
    }

    [Fact]
    public void Sem_o_texto_da_versao_anterior_nao_ha_exemplos()
    {
        var texts = Texts(("yst00001", [Unit("a", "Nova.")]));

        Assert.Empty(ReleaseTexts.Examples(null, texts, Changed(new ChangedScene("yst00001", 1, 1, ["a"])), max: 3));
    }
}
