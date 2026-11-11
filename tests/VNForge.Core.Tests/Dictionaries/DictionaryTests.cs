using VNForge.Core.Dictionaries;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Projects;
using VNForge.Core.Text;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Dictionaries;

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

    // ---- condições (pós-dicionário) ----

    [Fact]
    public void Regra_sem_condicao_troca_em_toda_linha_mesmo_sem_linha_conhecida()
    {
        var rules = new[] { Rule("Sakura-san", "Sakura") };

        Assert.Equal("Oi, Sakura!", Post(rules, "Oi, Sakura-san!", null));
        Assert.Equal("Oi, Sakura!", Post(rules, "Oi, Sakura-san!", DictionaryLine.Bare("原文")));
    }

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
    public void Pre_com_condicao_de_cena_e_de_trecho_do_original()
    {
        var pre = new PreDictionary(
            [
                Rule("ありがとー", "ありがとう", Scn("cap1")),
                Rule("やばい", "大変だ", Src("!")),
            ]);

        Assert.Equal("ありがとう", pre.Apply(Unit("cap1:000001", null, "ありがとー")).Source);
        Assert.Equal("ありがとー", pre.Apply(Unit("cap2:000001", null, "ありがとー")).Source);
        Assert.Equal("大変だ!", pre.Apply(Unit("cap1:000002", null, "やばい!")).Source);
        Assert.Equal("やばい", pre.Apply(Unit("cap1:000003", null, "やばい")).Source);
    }

    [Fact]
    public void Pre_recusa_a_regra_que_escreve_um_marcador()
    {
        var pre = new PreDictionary([Rule("a", "\\n")]);

        var ex = Assert.Throws<FormattingException>(() => pre.Apply(Unit("cap1:000001", null, "a")));

        Assert.Contains("cap1:000001", ex.Message);
    }

    [Fact]
    public void Pre_ignora_regra_desligada_e_nao_cria_etapa_sem_regra_ligada()
    {
        var pre = new PreDictionary([new DictionaryRule("a", "b") { Enabled = false }]);

        Assert.True(pre.IsEmpty);
        var unit = Unit("cap1:000001", null, "a");
        Assert.Same(unit, pre.Apply(unit));
    }

    [Fact]
    public void Pre_com_regex_invalida_e_erro_citando_a_regra()
    {
        var ex = Assert.Throws<FormattingException>(() => new PreDictionary([new DictionaryRule("(", "x", Regex: true)]));

        Assert.Contains("$.dictionary.pre[0].pattern", ex.Message);
    }

    [Fact]
    public void Pre_do_projeto_le_as_regras_do_formatting_json_com_os_nomes_dos_scripts()
    {
        _project.SaveScriptNames(ScriptNames.From([new ScriptNameEntry("cap1", null, "Capítulo 1")]));
        _project.SaveFormatting(FormattingProfile.Default with
        {
            Dictionary = new DictionaryRules([Rule("せやな", "そうだな", Scn("Capítulo 1"))], []),
        });

        var pre = PreDictionary.For(_project);

        Assert.Equal("そうだな", pre.Apply(Unit("cap1:000001", null, "せやな")).Source);
        Assert.Equal("せやな", pre.Apply(Unit("cap2:000001", null, "せやな")).Source);
        Assert.True(PreDictionary.For(ProjectFolder.Create(Path.Combine(_temp.Path, "outro"), Config())).IsEmpty);
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
    [InlineData("""{ "dictionary": { "pre": [ { "pattern": "(", "with": "x", "regex": true } ] } }""", "$.dictionary.pre[0].pattern")]
    [InlineData("""{ "dictionary": { "pre": [ { "pattern": "a", "with": "x", "when": [ { "kind": "mood", "value": "x" } ] } ] } }""", "$.dictionary.pre[0].when[0].kind")]
    [InlineData("""{ "dictionary": { "pre": [ { "pattern": "a", "with": "x", "when": [ { "kind": "speaker", "value": " " } ] } ] } }""", "$.dictionary.pre[0].when")]
    [InlineData("""{ "dictionary": { "antes": [] } }""", "$.dictionary.antes")]
    public void Formatting_json_com_dicionario_invalido_cita_o_campo(string json, string field)
    {
        var ex = Assert.Throws<FormattingProfileException>(() => FormattingProfileSerializer.Parse(json));

        Assert.Equal(field, ex.Field);
    }

    // ---- alcance da regra ----

    [Fact]
    public void Alcance_conta_as_linhas_que_trocam_e_as_que_a_condicao_impede()
    {
        var context = Context(speakerSource: "蓮", speakerTarget: "Ren");
        var rule = Rule("você", "tu", Spk("Ren"));
        TextUnit[] units =
        [
            Unit("cap1:000001", "蓮", "原", "você vem?"),
            Unit("cap1:000002", "蓮", "原", "você sabe."),
            Unit("cap1:000003", "葵", "原", "você chegou cedo."),
            Unit("cap1:000004", "蓮", "原", "ele vem."),
            Unit("cap1:000005", "蓮", "原"),
        ];

        var impact = DictionaryImpact.Of(rule, DictionarySide.Post, units, context);

        Assert.Equal(2, impact.Applies);
        Assert.Equal(1, impact.Blocked);
        Assert.Equal("cap1:000001", impact.FirstApplied!.Unit.Id);
        Assert.Equal("tu vem?", impact.FirstApplied.After);
        Assert.Equal("cap1:000003", impact.FirstBlocked!.Unit.Id);
    }

    [Fact]
    public void Alcance_do_pre_olha_o_original_e_conta_a_regra_desligada()
    {
        var rule = new DictionaryRule("ぼ、ぼく", "ぼく") { Enabled = false };

        var impact = DictionaryImpact.Of(rule, DictionarySide.Pre, [Unit("a:1", null, "ぼ、ぼく、行く"), Unit("a:2", null, "ぼく、行く")]);

        Assert.Equal(1, impact.Applies);
        Assert.Equal(0, impact.Blocked);
        Assert.Null(impact.FirstBlocked);
    }

    [Fact]
    public void Alcance_do_pos_conta_so_as_linhas_que_o_build_grava_dos_papeis_formatados()
    {
        var rule = Rule("você", "tu");
        TextUnit[] units =
        [
            Unit("a:1", null, "原", "você vem?"),
            new TextUnit("a:2", TextUnitKind.Choice, null, "原", "você?", TranslationStatus.Translated),
            Unit("a:3", null, "você vem?", "você vem?"),
        ];

        var impact = DictionaryImpact.Of(rule, DictionarySide.Post, units, kinds: [TextUnitKind.Dialogue, TextUnitKind.Narration]);

        Assert.Equal(1, impact.Applies);
        Assert.Equal("a:1", impact.FirstApplied!.Unit.Id);
    }
}
