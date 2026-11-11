using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Formatting;
using VNForge.Core.Glossary;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Qa;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class ProjectBuilderTests : IDisposable
{
    private const string A001Fake = "a001.fake";
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
    public void Script_que_a_engine_pede_para_reconstruir_passa_pelo_Rebuild_mesmo_sem_traducao()
    {
        _engine.ScriptsRequiringRebuild.Add("a002.fake");

        var result = Build();

        Assert.Equal(1, _engine.RebuildCalls);
        Assert.Empty(_engine.LastRebuiltUnits);
        Assert.Equal(2, result.ScriptCount);
    }

    [Fact]
    public void Resumo_do_projeto_informa_o_ultimo_build_com_os_scripts_trocados()
    {
        Assert.Null(ProjectSummaryReader.Read(_project.Root).LastBuild);
        Translate("a001", (0, Hello));

        var result = Build();

        var build = ProjectSummaryReader.Read(_project.Root).LastBuild;
        Assert.NotNull(build);
        Assert.Equal(Assert.Single(result.Archives).ReplacedFiles, build.Scripts);
        Assert.Equal(1, build.Scripts);
        Assert.True(DateTimeOffset.UtcNow - build.BuiltAt < TimeSpan.FromMinutes(5));
    }

    private bool PainelConcluiu(PipelineStageKey etapa) => ProjectDashboard.Read(_project).Stages.Single(s => s.Key == etapa).IsComplete;

    [Fact]
    public void Painel_da_Build_como_pendente_ate_o_primeiro_build_e_concluido_depois()
    {
        Translate("a001", (0, Hello));
        Assert.False(PainelConcluiu(PipelineStageKey.Build));

        Build();

        Assert.True(PainelConcluiu(PipelineStageKey.Build));
    }

    [Fact]
    public void Painel_da_Build_volta_a_pendente_quando_a_formatacao_muda()
    {
        Translate("a001", (0, Hello));
        Build();

        File.WriteAllText(_project.FormattingPath, "{ \"mudou\": true }");

        Assert.False(PainelConcluiu(PipelineStageKey.Build));
    }

    [Fact]
    public void Painel_da_Injetar_so_conclui_enquanto_o_jogo_tem_os_arquivos_do_build()
    {
        Translate("a001", (0, Hello));
        Build();
        Assert.False(PainelConcluiu(PipelineStageKey.Inject));

        var backup = Directory.CreateDirectory(Path.Combine(_project.BackupDirectory, "2026-10-24_1200")).FullName;
        var gameDirectory = _game.Replace("\\", "/");
        File.WriteAllText(
            Path.Combine(backup, "vnforge-backup.json"),
            $$"""{"version":1,"createdAt":"2026-10-24T12:00:00+00:00","gameDirectory":"{{gameDirectory}}","files":[{"path":"{{PackageFake}}","existed":true,"originalSha256":"aa","injectedSha256":"bb"}]}""");
        File.Copy(Path.Combine(_project.BuildDirectory, PackageFake), Path.Combine(_game, PackageFake), overwrite: true);
        Assert.True(PainelConcluiu(PipelineStageKey.Inject));

        File.WriteAllText(Path.Combine(_game, PackageFake), "original");

        Assert.False(PainelConcluiu(PipelineStageKey.Inject));
    }

    private BuildResult BuildIncremental(string? encoding = null) =>
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: new BuildOptions(OnlyChanged: true, EncodingStrategy: encoding));

    [Fact]
    public void Incremental_com_build_apagado_refaz_os_pacotes_sem_reconstruir_o_script()
    {
        Translate("a001", (0, Hello));
        BuildIncremental();
        var calls = _engine.RebuildCalls;
        Directory.Delete(_project.BuildDirectory, recursive: true);

        var result = BuildIncremental();

        Assert.Equal(calls, _engine.RebuildCalls);
        Assert.Equal(1, result.ReusedScripts);
        Assert.Equal("dialogue|Hello\nname|Ana" + "\u0001" + "narration|Chovia.", BuiltContent());
    }

    [Fact]
    public void Incremental_com_estado_corrompido_reconstroi()
    {
        Translate("a001", (0, Hello));
        BuildIncremental();
        File.WriteAllText(Path.Combine(_project.CacheDirectory, "build-state.json"), "{ não é json");

        var result = BuildIncremental();

        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
    }

    private void SaveProfile(string json) => File.WriteAllText(_project.FormattingPath, json);

    private string BuiltContent() => File.ReadAllText(Path.Combine(_project.BuildDirectory, PackageFake));

    [Fact]
    public void Perfil_com_quebra_ligada_quebra_a_traducao_antes_de_gravar()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 12, "maxLines": null, "charWidth": "uniform" } }""");
        Translate("a001", (0, @"uma frase\c bem longa"));

        var result = Build();

        Assert.StartsWith(@"dialogue|uma frase\c\nbem longa" + "\nname|Ana", BuiltContent());
        Assert.Equal(1, result.TotalLines);
    }

    [Fact]
    public void Qa_do_build_mede_o_texto_com_o_pos_dicionario_condicionado_ao_falante()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 2, "charWidth": "uniform" }, "dictionary": { "post": [ { "pattern": "x", "with": "aaa bbb ccc", "when": [ { "kind": "speaker", "value": "犬井" } ] } ] } }""");
        Translate("a001", (0, "x"));
        var saved = _project.LoadAllText()["a001"];
        _project.SaveText("a001", [.. saved.Select((u, i) => i == 0 ? u with { Speaker = "犬井" } : u)]);

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, allowWarnings: true);

        var issue = Assert.Single(result.Qa.OtherIssues, i => i.RuleId == BoxLineLimitRule.RuleId);
        Assert.Equal("a001:000000", issue.UnitId);
        Assert.StartsWith(@"dialogue|aaa\nbbb\nccc", BuiltContent());
    }

    [Fact]
    public void Marcador_invalido_aborta_citando_a_linha_e_o_perfil()
    {
        SaveProfile("""{ "wrap": { "enabled": true } }""");
        Translate("a001", (0, @"oi \x"));

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Contains(FirstUnit, ex.Message);
        Assert.Contains("formatting.json", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
    }

    [Fact]
    public void Formatting_json_invalido_aborta_apontando_o_campo()
    {
        SaveProfile("""{ "wrap": { "maxWidth": 0 } }""");
        Translate("a001", (0, Hello));

        var ex = Assert.Throws<FormattingProfileException>(() => Build());

        Assert.Equal("$.wrap.maxWidth", ex.Field);
        Assert.Equal(0, _engine.RebuildCalls);
    }

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

    private void TranslateAs(string speaker, string text)
    {
        Translate("a001", (0, text));
        var units = _project.LoadAllText()["a001"];
        _project.SaveText("a001", [.. units.Select((u, i) => i == 0 ? u with { Speaker = speaker } : u)]);
    }

    private void ChooseSpeaker(string text, string? speaker, string? extracted = null)
    {
        TranslateAs(extracted ?? "犬井", text);
        var units = _project.LoadAllText()["a001"];
        _project.SaveText("a001", [.. units.Select((u, i) => i == 0 ? u with { ChosenSpeaker = speaker } : u)]);
    }

    [Theory]
    [InlineData("sjis", "美咲", null, "美咲", "Mísaki", "美咲")]
    [InlineData("cp1252", "？？？", null, null, null, "???")]
    public void Placa_da_caixa_do_jogo_mostra_o_nome_que_o_build_entrega_a_engine(string strategy, string extracted, string? chosen, string? characterSource, string? characterTarget, string? plate)
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig(strategy) });
        if (characterSource is not null)
        {
            _project.SaveCharacters([new CharacterEntry(characterSource, characterTarget!)]);
        }

        ChooseSpeaker("Bom dia!", chosen, extracted);
        Build();

        var written = Assert.Single(_engine.LastRebuiltUnits, u => u.NeedsRewrite).EffectiveSpeaker;
        var encoder = TextEncoderRegistry.Default.Create(strategy, _project.LoadConfig().Style.SpeakerDelimiter);
        var unit = _project.LoadAllText()["a001"][0];
        var shown = SpeakerText.WrittenName(unit, new CharacterBook(_project.LoadCharacters()), encoder);
        Assert.Equal(string.IsNullOrEmpty(written) ? null : written, shown);
        Assert.Equal(plate, shown);
        Assert.Equal(plate, LinePreviewer.Create(_project, FirstUnit).Speaker);
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
    public void Trocar_o_falante_escolhido_refaz_o_script_no_build_incremental()
    {
        ChooseSpeaker("Bom dia!", "Misaki");
        var options = new BuildOptions(OnlyChanged: true);
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: options);
        var calls = _engine.RebuildCalls;

        ChooseSpeaker("Bom dia!", string.Empty);
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: options);

        Assert.Equal(calls + 1, _engine.RebuildCalls);
        Assert.Equal(string.Empty, Assert.Single(_engine.LastRebuiltUnits, u => u.NeedsRewrite).ChosenSpeaker);
    }

    [Fact]
    public void Mensagem_limita_a_lista_e_aponta_o_report()
    {
        Translate("a001", (0, new string('ã', 30)));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Equal(30, ex.Report.Blocking.Count);
        Assert.Contains("e mais 10", ex.Message);
        Assert.Contains("vnf build --report", ex.Message);
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
    public void ReportEncoding_confere_sem_tocar_em_build_nem_no_cache()
    {
        Translate("a001", (0, "ã"));
        Directory.Delete(_project.GameCacheDirectory, recursive: true);

        var report = ProjectBuilder.ReportEncoding(_project);

        Assert.Equal("ã", Assert.Single(report.Blocking).Character);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
    }

    [Fact]
    public void Texto_sem_script_vira_aviso()
    {
        _project.SaveText("zzz", [new TextUnit("zzz:000000", TextUnitKind.Dialogue, null, "x", "y", TranslationStatus.Translated)]);

        var result = Build();

        Assert.Contains(result.Warnings, w => w.Contains("text/zzz.json"));
    }

    [Fact]
    public void Sem_cache_pede_para_rodar_o_extract()
    {
        Directory.Delete(_project.GameCacheDirectory, recursive: true);

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Contains("Rode vnf extract", ex.Message);
    }

    [Fact]
    public void Engine_desconhecida_ou_sem_suporte_ao_build_e_erro_em_portugues()
    {
        var unknown = Assert.Throws<VNForgeException>(() => ProjectBuilder.Build(_project, [new NoRepackEngine { EngineId = "outra" }], cancellationToken: TestContext.Current.CancellationToken));
        var unsupported = Assert.Throws<VNForgeException>(() => ProjectBuilder.Build(_project, [new NoRepackEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("engine 'fake'", unknown.Message);
        Assert.Contains("ainda não suporta o build", unsupported.Message);
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

    [Fact]
    public void Pasta_do_jogo_inexistente_e_erro_em_portugues()
    {
        Directory.Delete(_game, recursive: true);

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Contains("A pasta do jogo não existe", ex.Message);
    }

    private sealed class NoRepackEngine : IEngine, IGameUnpacker
    {
        public string EngineId { get; init; } = "fake";

        public string Id => EngineId;

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default) => new([]);

        public UnpackedGame Open(string unpackedDirectory) => throw new NotSupportedException();
    }

    private void MarkOk(string unitId)
    {
        var key = unitId.Split(':')[0];
        _project.SaveText(key, [.. _project.LoadText(key).Select(u => u.Id == unitId ? u with { Flags = ["traducao-vazia"] } : u)]);
    }
}
