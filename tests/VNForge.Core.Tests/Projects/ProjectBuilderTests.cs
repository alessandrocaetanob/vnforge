using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Qa;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

[Trait("Risco", "R3")]
[Trait("Risco", "R4")]
public sealed class ProjectBuilderTests : IDisposable
{
    private const string A001Fake = "a001.fake";
    private const string PreviousFile = "anterior.txt";
    private const string PackageFake = "pacote.fake";

    private const string Hello = "Hello";

    private const string FirstUnit = "a001:000000";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();

    public ProjectBuilderTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, A001Fake), "dialogue|Olá\nname|Ana");
        File.WriteAllText(Path.Combine(_game, "a002.fake"), "narration|Chovia.");
        _project = ProjectFolder.Create(
            Path.Combine(_temp.Path, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
        _engine.Unpack(_game, _project.GameCacheDirectory, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _temp.Dispose();

    private BuildResult Build() => ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]));

    private void Translate(string key, params (int Index, string Text)[] changes)
    {
        var units = _project.LoadAllText().TryGetValue(key, out var saved)
            ? saved
            : _engine.Scripts[0].Extract(new ScriptFile(key + ".fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, PackageFake, key + ".fake"))), new FakeContext()).Units;
        var edited = units.Select((u, i) => changes.FirstOrDefault(c => c.Index == i) is { Text: not null } c
            ? u with { Translation = c.Text, Status = TranslationStatus.Translated }
            : u).ToList();
        _project.SaveText(key, edited);
    }

    [Fact]
    public void Sem_traducao_nenhum_script_e_reconstruido_e_o_pacote_sai_identico()
    {
        var result = Build();

        Assert.Empty(result.RebuiltScripts);
        Assert.Equal(2, result.ScriptCount);
        Assert.Equal(0, _engine.RebuildCalls);
        Assert.Empty(_engine.LastRebuilt);
        var archive = Assert.Single(result.Archives);
        Assert.Equal(0, archive.ReplacedFiles);
        Assert.Equal(
            "dialogue|Olá\nname|Ana" + "\u0001" + "narration|Chovia.",
            File.ReadAllText(Path.Combine(_project.BuildDirectory, PackageFake)));
    }

    private bool PainelConcluiu(PipelineStageKey etapa) => ProjectDashboard.Read(_project).Stages.Single(s => s.Key == etapa).IsComplete;

    private BuildResult BuildIncremental(string? encoding = null) =>
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: new BuildOptions(OnlyChanged: true, EncodingStrategy: encoding));

    private void SaveProfile(string json) => File.WriteAllText(_project.FormattingPath, json);

    private string BuiltContent() => File.ReadAllText(Path.Combine(_project.BuildDirectory, PackageFake));

    [Fact]
    public void Texto_que_o_shift_jis_nao_codifica_aborta_listando_o_id_e_nao_grava_nada()
    {
        Translate("a001", (1, "Aña 😀"));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Contains("a001:000001", ex.Message);
        Assert.Contains("'sjis'", ex.Message);
        Assert.Equal(["ñ", "😀"], ex.Report.Blocking.Select(i => i.Character));
        Assert.Equal(0, _engine.RebuildCalls);
        Assert.False(Directory.Exists(_project.BuildDirectory + ".tmp"));
    }

    [Fact]
    public void Cp1252_recusa_o_i_trema_listando_a_linha()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        Translate("a001", (1, "maïs"));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Contains("a001:000001 posição 2: 'ï' (U+00EF)", ex.Message);
    }

    private void TranslateAs(string speaker, string text)
    {
        Translate("a001", (0, text));
        var units = _project.LoadAllText()["a001"];
        _project.SaveText("a001", [.. units.Select((u, i) => i == 0 ? u with { Speaker = speaker } : u)]);
    }

    [Fact]
    public void Cp1252_com_nome_japones_sem_cadastro_e_recusado_pela_conferencia_antes_do_script()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        TranslateAs("犬井", "Bom dia!");

        var ex = Assert.Throws<QaBlockedException>(() =>
            ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, allowWarnings: true));

        var issue = Assert.Single(ex.Report.BlockingIssues, i => i.RuleId == SpeakerEncodingRule.RuleId);
        Assert.Equal(FirstUnit, issue.UnitId);
        Assert.Contains("O nome do personagem 犬井 não existe na codificação do jogo (cp1252)", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _engine.RebuildCalls);
    }

    [Fact]
    public void Build_sem_a_regra_de_conferencia_ainda_recusa_o_nome_japones_citando_a_linha()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        TranslateAs("犬井", "Bom dia!");

        var ex = Assert.Throws<VNForgeException>(Build);

        Assert.Contains(FirstUnit, ex.Message, StringComparison.Ordinal);
        Assert.Contains("Glossário > Personagens", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _engine.RebuildCalls);
    }

    private void ChooseSpeaker(string text, string? speaker, string? extracted = null)
    {
        TranslateAs(extracted ?? "犬井", text);
        var units = _project.LoadAllText()["a001"];
        _project.SaveText("a001", [.. units.Select((u, i) => i == 0 ? u with { ChosenSpeaker = speaker } : u)]);
    }

    [Fact]
    public void Falante_escolhido_japones_sem_cadastro_e_recusado_pela_conferencia()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        ChooseSpeaker("Bom dia!", "犬井", extracted: "Inui");

        var ex = Assert.Throws<QaBlockedException>(() =>
            ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, allowWarnings: true));

        Assert.Equal(FirstUnit, Assert.Single(ex.Report.BlockingIssues, i => i.RuleId == SpeakerEncodingRule.RuleId).UnitId);
        Assert.Equal(0, _engine.RebuildCalls);
    }

    [Fact]
    public void Build_sem_a_regra_de_conferencia_recusa_o_falante_escolhido_que_nao_grava()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        ChooseSpeaker("Bom dia!", "犬井", extracted: "Inui");

        var ex = Assert.Throws<VNForgeException>(Build);

        Assert.Contains(FirstUnit, ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, _engine.RebuildCalls);
    }

    [Fact]
    public void Cp1252_sem_sinal_recusa_o_espaco_nao_separavel_na_traducao_e_com_guillemets_aceita()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        Translate("a001", (0, "um\u00A0dois"));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Contains("U+00A0", ex.Message);
        _project.SaveConfig(_project.LoadConfig() with { Style = StyleConfig.Default with { SpeakerDelimiter = SpeakerDelimiterStyle.Guillemets } });
        Assert.Empty(Build().Encoding.Issues);
    }

    [Fact]
    public void Falha_do_repacker_preserva_o_build_anterior_e_remove_o_temporario()
    {
        Directory.CreateDirectory(_project.BuildDirectory);
        var previous = Path.Combine(_project.BuildDirectory, PreviousFile);
        File.WriteAllText(previous, "x");
        _engine.RepackFailure = new VNForgeException("pacote corrompido");

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Equal("pacote corrompido", ex.Message);
        Assert.True(File.Exists(previous));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".tmp"));
    }

    [Fact]
    public void Projeto_dentro_da_pasta_do_jogo_e_recusado_sem_gravar_nada()
    {
        var inside = ProjectFolder.Create(
            Path.Combine(_game, "projeto"),
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), "Jogo.exe"),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("sjis"),
                AiConfig.Default));
        _engine.Unpack(_game, inside.GameCacheDirectory, TestContext.Current.CancellationToken);

        var ex = Assert.Throws<VNForgeException>(() => ProjectBuilder.Build(inside, [_engine], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("fica dentro da pasta do jogo", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(inside.BuildDirectory, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(inside.BuildDirectory + ".tmp"));
    }

    private void MarkOk(string unitId)
    {
        var key = unitId.Split(':')[0];
        _project.SaveText(key, [.. _project.LoadText(key).Select(u => u.Id == unitId ? u with { Flags = ["traducao-vazia"] } : u)]);
    }
}
