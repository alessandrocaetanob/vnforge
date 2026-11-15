using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Dictionaries;

[Trait("Risco", "R4")]
public sealed class DictionaryTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly ProjectFolder _project;

    public DictionaryTests()
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

    private static TextUnit Unit(string id, string? speaker, string source, string? translation = null) =>
        new(id, TextUnitKind.Dialogue, speaker, source, translation, translation is null ? TranslationStatus.Untranslated : TranslationStatus.Translated);

    private static DictionaryContext Context(string? sceneName = null, string? group = null, string? folder = null, string? speakerSource = null, string? speakerTarget = null)
    {
        ScriptNames scripts = sceneName is null && group is null && folder is null
            ? ScriptNames.Empty
            : ScriptNames.From([new ScriptNameEntry("cap1", null, sceneName) { Group = group, Folder = folder }]);
        var characters = speakerSource is null ? [] : new[] { new CharacterEntry(speakerSource, speakerTarget ?? speakerSource, CharacterGender.Unknown) };
        return new DictionaryContext(scripts, new CharacterBook(characters));
    }

    private static DictionaryCondition Spk(string value) => new(DictionaryConditionKind.Speaker, value);

    private static DictionaryCondition Scn(string value) => new(DictionaryConditionKind.Scene, value);

    private static DictionaryCondition Src(string value) => new(DictionaryConditionKind.SourceContains, value);

    private static DictionaryRule Rule(string pattern, string with, params DictionaryCondition[] when) =>
        new(pattern, with) { Conditions = when };

    private static string Post(IReadOnlyList<DictionaryRule> rules, string translation, DictionaryLine? line) =>
        FormattingPipeline.Create(FormattingProfile.Default with { Dictionary = new DictionaryRules([], rules) }).Format(translation, line).Text;

    [Theory]
    [InlineData("cap1")]
    public void Condicao_de_cena_bate_pela_chave_pelo_nome_pelo_grupo_ou_pela_pasta(string scene)
    {
        var rules = new[] { Rule("você", "tu", Scn(scene)) };
        var context = Context(sceneName: "Capítulo 1", group: "Manhã de chuva", folder: "userscript/01_cap1");

        Assert.Equal("tu", Post(rules, "você", context.Line(Unit("cap1:000001", "蓮", "原文"))));
        Assert.Equal("você", Post(rules, "você", context.Line(Unit("cap2:000001", "蓮", "原文"))));
    }

    [Fact]
    public void Condicao_de_trecho_do_original_olha_o_original_e_nao_a_traducao()
    {
        var rules = new[] { Rule("senhora Misaki", "professora Misaki", Src("先生")) };

        Assert.Equal("professora Misaki", Post(rules, "senhora Misaki", DictionaryLine.Bare("先生、おはよう")));
        Assert.Equal("senhora Misaki", Post(rules, "senhora Misaki", DictionaryLine.Bare("おはよう")));
    }

    [Fact]
    public void Pre_recusa_a_regra_que_escreve_um_marcador()
    {
        var pre = new PreDictionary([Rule("a", "\\n")]);

        var ex = Assert.Throws<FormattingException>(() => pre.Apply(Unit("cap1:000001", null, "a")));

        Assert.Contains("cap1:000001", ex.Message);
    }

    private static ProjectConfig Config() => new(
        "Outro",
        new GameConfig("D:/Jogos/Outro", "Jogo.exe"),
        new EngineConfig("yuris", 500, "0xD36FAC96"),
        new LanguagesConfig("ja", "pt-BR"),
        new EncodingConfig("fold"),
        AiConfig.Default);

    // ---- formatting.json ----

    [Fact]
    public void Formatting_json_ida_e_volta_guarda_regras_condicoes_e_liga_desliga()
    {
        var profile = FormattingProfile.Default with
        {
            Dictionary = new DictionaryRules(
                [new DictionaryRule("ぼ、ぼく", "ぼく"), new DictionaryRule("ありがとー", "ありがとう") { Enabled = false, Conditions = [Src("ー")] }],
                [new DictionaryRule(@"(\w+)-san", "$1", Regex: true) { Conditions = [Spk("Ren"), Scn("Capítulo 1")] }]),
        };

        var json = FormattingProfileSerializer.Serialize(profile);
        var read = FormattingProfileSerializer.Parse(json);

        Assert.Equal(profile.Dictionary.Pre.Select(Describe), read.Dictionary.Pre.Select(Describe));
        Assert.Equal(profile.Dictionary.Post.Select(Describe), read.Dictionary.Post.Select(Describe));
        Assert.Contains("\"sourceContains\"", json);
        Assert.Equal(json, FormattingProfileSerializer.Serialize(read));
    }

    private static string Describe(DictionaryRule r) =>
        $"{r.Pattern}>{r.With}|{r.Regex}|{r.Enabled}|{string.Join(',', r.Conditions.Select(c => $"{c.Kind}={c.Value}"))}";

    [Theory]
    [InlineData("""{ "dictionary": { "post": [ { "pattern": "", "with": "x" } ] } }""", "$.dictionary.post[0].pattern")]
    public void Formatting_json_com_dicionario_invalido_cita_o_campo(string json, string field)
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal(field, ex.Field);
    }

    // ---- build, previa e QA ----

    // ---- alcance da regra ----
}
