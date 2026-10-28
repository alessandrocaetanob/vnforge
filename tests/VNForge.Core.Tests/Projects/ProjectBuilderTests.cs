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
    private const string PreviousFile = "anterior.txt";
    private const string PackageFake = "pacote.fake";

    private const string Hello = "Hello";

    private const string FirstUnit = "a001:000000";

    private const string ThreeWords = "aaa bbb ccc";
    private const string OverflowProfile = """{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 2, "charWidth": "uniform" } }""";

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
    public void Relata_cada_script_do_jogo_em_ordem_e_um_relato_final_com_todos()
    {
        Translate("a001", (0, Hello));
        var reports = new List<ScriptWorkProgress>();

        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), progress: new SyncProgress<ScriptWorkProgress>(reports.Add));

        Assert.Equal(["a001.fake", "a002.fake", string.Empty], reports.Select(r => r.Name));
        Assert.Equal([0, 1, 2], reports.Select(r => r.Done));
        Assert.All(reports, r => Assert.Equal(2, r.Total));
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
    public void Linha_nao_traduzir_gera_o_mesmo_pacote_que_a_linha_sem_traducao()
    {
        var semMarca = Build();
        var esperado = File.ReadAllBytes(Path.Combine(_project.BuildDirectory, PackageFake));
        var units = _engine.Scripts[0].Extract(new ScriptFile("a001.fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, PackageFake, "a001.fake"))), new FakeContext()).Units;
        _project.SaveText("a001", [.. units.Select((u, i) => i == 0 ? u with { Status = TranslationStatus.Keep } : u)]);

        var result = Build();

        Assert.Empty(result.RebuiltScripts);
        Assert.Equal(0, _engine.RebuildCalls);
        Assert.Equal(esperado, File.ReadAllBytes(Path.Combine(_project.BuildDirectory, PackageFake)));
        Assert.Empty(semMarca.RebuiltScripts);
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
    public void Painel_da_Build_ignora_data_de_gravacao_e_mudanca_so_de_status()
    {
        Translate("a001", (0, Hello));
        Build();

        var units = _project.LoadText("a001").Select(u => u with { Status = TranslationStatus.Approved }).ToList();
        _project.SaveText("a001", units);
        File.SetLastWriteTimeUtc(_project.TextPath("a001"), DateTime.UtcNow.AddMinutes(5));
        Assert.True(PainelConcluiu(PipelineStageKey.Build));

        Build();
        Assert.True(PainelConcluiu(PipelineStageKey.Build));
    }

    [Fact]
    public void Painel_da_Build_volta_a_pendente_quando_a_traducao_muda_e_conclui_no_build_seguinte()
    {
        Translate("a001", (0, Hello));
        Build();

        Translate("a001", (0, "Oi"));
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

    [Fact]
    public void Entrega_ao_repacker_so_o_script_com_traducao_e_o_conteudo_original()
    {
        Translate("a001", (0, Hello));

        var result = Build();

        var built = Assert.Single(result.RebuiltScripts);
        Assert.Equal(new BuiltScript("a001", A001Fake, 1), built);
        Assert.Equal(1, result.TotalLines);
        var script = Assert.Single(_engine.LastRebuilt);
        Assert.Equal(Path.Combine(_project.GameCacheDirectory, PackageFake, A001Fake), script.Path);
        Assert.Equal("dialogue|Olá\nname|Ana", Encoding.UTF8.GetString(script.Original));
        Assert.Equal("dialogue|Hello\nname|Ana", Encoding.UTF8.GetString(script.Content));
        Assert.Equal(1, Assert.Single(result.Archives).ReplacedFiles);
    }

    private BuildResult BuildIncremental(string? encoding = null) =>
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: new BuildOptions(OnlyChanged: true, EncodingStrategy: encoding));

    [Fact]
    public void Incremental_sem_mudanca_desde_o_build_anterior_nao_reconstroi_nenhum_script()
    {
        Translate("a001", (0, Hello));
        BuildIncremental();
        var rebuildCalls = _engine.RebuildCalls;
        var repackCalls = _engine.RepackCalls;
        var content = BuiltContent();

        var result = BuildIncremental();

        Assert.Equal(rebuildCalls, _engine.RebuildCalls);
        Assert.Equal(repackCalls, _engine.RepackCalls);
        Assert.Empty(result.RebuiltScripts);
        Assert.Equal(1, result.ReusedScripts);
        Assert.Equal(content, BuiltContent());
        Assert.Equal(1, Assert.Single(result.Archives).ReplacedFiles);
    }

    [Fact]
    public void Incremental_reconstroi_so_o_script_cuja_traducao_mudou_e_mantem_o_outro()
    {
        Translate("a001", (0, Hello));
        Translate("a002", (0, "It rained."));
        BuildIncremental();
        var calls = _engine.RebuildCalls;
        Translate("a002", (0, "It was raining."));

        var result = BuildIncremental();

        Assert.Equal(calls + 1, _engine.RebuildCalls);
        Assert.Equal("a002", Assert.Single(result.RebuiltScripts).Key);
        Assert.Equal(1, result.ReusedScripts);
        Assert.Equal(2, Assert.Single(result.Archives).ReplacedFiles);
        Assert.Equal("dialogue|Hello\nname|Ana" + "\u0001" + "narration|It was raining.", BuiltContent());
    }

    [Fact]
    public void Incremental_com_outra_estrategia_de_codificacao_reconstroi()
    {
        Translate("a001", (0, Hello));
        BuildIncremental();
        var calls = _engine.RebuildCalls;

        var result = BuildIncremental("fold");

        Assert.Equal(calls + 1, _engine.RebuildCalls);
        Assert.Equal("fold", result.Encoding.Strategy);
        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
    }

    [Fact]
    public void Incremental_sem_build_anterior_reconstroi_tudo()
    {
        Translate("a001", (0, Hello));

        var result = BuildIncremental();

        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
        Assert.Equal(0, result.ReusedScripts);
    }

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

    [Fact]
    public void Build_completo_ignora_o_estado_anterior()
    {
        Translate("a001", (0, Hello));
        BuildIncremental();
        var calls = _engine.RebuildCalls;

        var result = Build();

        Assert.Equal(calls + 1, _engine.RebuildCalls);
        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
    }

    [Fact]
    public void Estrategia_pedida_vale_so_para_este_build_e_nao_muda_o_vnforge_json()
    {
        Translate("a001", (0, Hello));

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: new BuildOptions(EncodingStrategy: "fold"));

        Assert.Equal("fold", result.Encoding.Strategy);
        Assert.Equal("sjis", _project.LoadConfig().Encoding.Strategy);
    }

    [Fact]
    public void Estrategia_desconhecida_no_build_e_recusada_em_portugues()
    {
        var ex = Assert.Throws<VNForgeException>(() =>
            ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: new BuildOptions(EncodingStrategy: "latin9")));

        Assert.Contains("latin9", ex.Message, StringComparison.Ordinal);
    }

    private void SaveProfile(string json) => File.WriteAllText(_project.FormattingPath, json);

    private string BuiltContent() => File.ReadAllText(Path.Combine(_project.BuildDirectory, PackageFake));

    [Fact]
    public void Sem_formatting_json_o_texto_vai_como_esta()
    {
        Translate("a001", (0, "uma frase bem longa para uma caixa curta"));

        Build();

        Assert.StartsWith("dialogue|uma frase bem longa para uma caixa curta\nname|Ana", BuiltContent());
    }

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
    public void Quebra_so_vale_para_os_papeis_do_perfil()
    {
        SaveProfile("""{ "kinds": ["narration"], "wrap": { "enabled": true, "maxWidth": 5, "charWidth": "uniform" } }""");
        Translate("a001", (0, ThreeWords), (1, "Ana Maria Souza"));
        Translate("a002", (0, ThreeWords));

        Build();

        var content = BuiltContent();
        Assert.Contains("dialogue|aaa bbb ccc\nname|Ana Maria Souza", content);
        Assert.Contains(@"narration|aaa\nbbb\nccc", content);
    }

    [Fact]
    public void Formatacao_nao_altera_o_text_json_em_disco()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "charWidth": "uniform" } }""");
        Translate("a001", (0, ThreeWords));
        var before = File.ReadAllText(_project.TextPath("a001"));

        Build();

        Assert.Equal(before, File.ReadAllText(_project.TextPath("a001")));
    }

    [Fact]
    public void Caixa_acima_do_limite_de_linhas_vira_aviso_com_o_id_da_linha()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 2, "charWidth": "uniform" } }""");
        Translate("a001", (0, ThreeWords));

        var result = Build();

        Assert.Contains(result.Warnings, w => w.StartsWith("a001:000000: caixa 1 com 3 linhas (máximo 2)", StringComparison.Ordinal));
        Assert.StartsWith(@"dialogue|aaa\nbbb\nccc", BuiltContent());
    }

    [Fact]
    public void Caixa_acima_do_limite_que_o_qa_ja_aponta_nao_repete_nos_avisos_do_build()
    {
        SaveProfile(OverflowProfile);
        Translate("a001", (0, ThreeWords));

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, allowWarnings: true);

        var issue = Assert.Single(result.Qa.OtherIssues, i => i.RuleId == BoxLineLimitRule.RuleId);
        Assert.Equal("a001:000000", issue.UnitId);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("caixa 1 com 3 linhas", StringComparison.Ordinal));
    }

    [Fact]
    public void Caixa_acima_do_limite_marcada_como_ok_nao_volta_nos_avisos_do_build()
    {
        SaveProfile(OverflowProfile);
        Translate("a001", (0, ThreeWords));
        _project.SaveText("a001", [.. _project.LoadText("a001").Select(u => u.Id == "a001:000000" ? u with { Flags = [BoxLineLimitRule.RuleId] } : u)]);

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, allowWarnings: true);

        Assert.Single(result.Qa.Silenced, i => i.RuleId == BoxLineLimitRule.RuleId);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("caixa 1 com 3 linhas", StringComparison.Ordinal));
    }

    [Fact]
    public void A_codificacao_confere_o_texto_ja_formatado()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "charWidth": "uniform" } }""");
        Translate("a001", (0, "aaa bbb cãc"));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Equal(11,Assert.Single(ex.Report.Blocking).Position);
    }

    [Fact]
    public void ReportEncoding_tambem_usa_o_texto_formatado()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "charWidth": "uniform" } }""");
        Translate("a001", (0, "aaa bbb cãc"));

        var report = ProjectBuilder.ReportEncoding(_project);

        Assert.Equal(11, Assert.Single(report.Blocking).Position);
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
    public void Traducao_igual_ao_original_ou_sem_status_nao_reconstroi()
    {
        var units = _engine.Scripts[0].Extract(new ScriptFile(A001Fake, File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, PackageFake, A001Fake))), new FakeContext()).Units;
        _project.SaveText("a001", [units[0] with { Translation = "Olá", Status = TranslationStatus.Translated }, units[1] with { Translation = "Anna" }]);

        var result = Build();

        Assert.Empty(result.RebuiltScripts);
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
    public void Cp1252_entrega_ao_script_o_nome_cadastrado_no_lugar_do_japones()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        _project.SaveCharacters([new CharacterEntry("犬井", "Inui")]);
        TranslateAs("犬井", "Bom dia!");

        Build();

        Assert.Equal("Inui", Assert.Single(_engine.LastRebuiltUnits, u => u.NeedsRewrite).Speaker);
        Assert.Equal("犬井", _project.LoadAllText()["a001"][0].Speaker);
    }

    [Fact]
    public void Cp1252_entrega_ao_script_o_falante_de_sinais_largos_normalizado()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        TranslateAs("？？？", "Quem?");

        Build();

        Assert.Equal("???", Assert.Single(_engine.LastRebuiltUnits, u => u.NeedsRewrite).Speaker);
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

    [Fact]
    public void Falante_sem_traducao_nao_e_reconstruido_mesmo_em_cp1252()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        var units = _engine.Scripts[0].Extract(new ScriptFile(A001Fake, File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, PackageFake, A001Fake))), new FakeContext()).Units;
        _project.SaveText("a001", [.. units.Select((u, i) => i == 0 ? u with { Speaker = "犬井" } : u)]);

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, allowWarnings: true);

        Assert.Empty(result.RebuiltScripts);
        Assert.Equal(0, _engine.RebuildCalls);
    }

    [Fact]
    public void Mudar_o_nome_do_personagem_refaz_o_script_no_build_incremental()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        _project.SaveCharacters([new CharacterEntry("犬井", "Inui")]);
        TranslateAs("犬井", "Bom dia!");
        var options = new BuildOptions(OnlyChanged: true);
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: options);
        var calls = _engine.RebuildCalls;

        _project.SaveCharacters([new CharacterEntry("犬井", "Inui-kun")]);
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([]), options: options);

        Assert.Equal(calls + 1, _engine.RebuildCalls);
        Assert.Equal("Inui-kun", Assert.Single(_engine.LastRebuiltUnits, u => u.NeedsRewrite).Speaker);
    }

    [Fact]
    public void Lista_de_uma_vez_os_casos_de_varias_linhas_e_scripts()
    {
        Translate("a001", (0, "«Olá»"), (1, "Aña"));
        Translate("a002", (0, "Chovia ª vez"));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Equal([FirstUnit, FirstUnit, FirstUnit, "a001:000001", "a002:000000"], ex.Report.Blocking.Select(i => i.UnitId));
        Assert.Contains("a002:000000 posição 7: 'ª' (U+00AA)", ex.Message);
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
    public void Cp1252_do_vnforge_json_builda_com_os_tres_caracteres()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("cp1252") });
        Translate("a001", (0, "«Olá» ª ã"));

        var result = Build();

        Assert.Equal("cp1252", result.Encoding.Strategy);
        Assert.Empty(result.Encoding.Issues);
    }

    [Fact]
    public void Fold_builda_e_o_resultado_traz_os_caracteres_trocados()
    {
        _project.SaveConfig(_project.LoadConfig() with { Encoding = new EncodingConfig("fold") });
        Translate("a001", (0, "«ªã»"));

        var result = Build();

        Assert.Equal(["\"", "a", "a", "\""], result.Encoding.Replaced.Select(i => i.Replacement));
        Assert.Equal("fold", result.Encoding.Strategy);
    }

    [Theory]
    [InlineData("remap", "ainda não está disponível")]
    public void Estrategia_sem_encoder_aborta_sem_cair_em_shift_jis(string strategy, string expected)
    {
        File.WriteAllText(_project.ConfigPath, File.ReadAllText(_project.ConfigPath).Replace("\"sjis\"", $"\"{strategy}\"", StringComparison.Ordinal));

        var ex = Assert.Throws<VNForgeException>(() => Build());

        Assert.Contains($"'{strategy}'", ex.Message);
        Assert.Contains(expected, ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
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
    public void Build_anterior_e_trocado_so_no_fim()
    {
        Directory.CreateDirectory(_project.BuildDirectory);
        File.WriteAllText(Path.Combine(_project.BuildDirectory, PreviousFile), "x");

        Build();

        Assert.False(File.Exists(Path.Combine(_project.BuildDirectory, PreviousFile)));
        Assert.True(File.Exists(Path.Combine(_project.BuildDirectory, PackageFake)));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".tmp"));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".old"));
    }

    [Fact]
    public void Cancelamento_mantem_o_build_anterior_e_nao_deixa_temporario()
    {
        Directory.CreateDirectory(_project.BuildDirectory);
        var previous = Path.Combine(_project.BuildDirectory, PreviousFile);
        File.WriteAllText(previous, "x");
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ProjectBuilder.Build(_project, [_engine], cancellationToken: source.Token));

        Assert.True(File.Exists(previous));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".tmp"));
    }

    [Fact]
    public void Nao_altera_a_pasta_do_jogo_nem_o_cache()
    {
        Translate("a001", (0, Hello));
        var gameBefore = File.ReadAllText(Path.Combine(_game, A001Fake));
        var cacheBefore = File.ReadAllText(Path.Combine(_project.GameCacheDirectory, PackageFake, A001Fake));

        Build();

        Assert.Equal(gameBefore, File.ReadAllText(Path.Combine(_game, A001Fake)));
        Assert.Equal(cacheBefore, File.ReadAllText(Path.Combine(_project.GameCacheDirectory, PackageFake, A001Fake)));
    }

    [Fact]
    public void Texto_sem_script_com_caractere_nao_codificavel_nao_bloqueia_o_build()
    {
        _project.SaveText("zzz", [new TextUnit("zzz:000000", TextUnitKind.Dialogue, null, "x", "ã", TranslationStatus.Translated)]);

        var result = Build();

        Assert.Contains(result.Warnings, w => w.Contains("text/zzz.json"));
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

    private BuildResult BuildWithQa(QaSeverity severity, bool allowWarnings = false) =>
        ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([new QaEngineTests.FlagEmpty("traducao-vazia", severity)]), allowWarnings: allowWarnings);

    private void MarkOk(string unitId)
    {
        var key = unitId.Split(':')[0];
        _project.SaveText(key, [.. _project.LoadText(key).Select(u => u.Id == unitId ? u with { Flags = ["traducao-vazia"] } : u)]);
    }

    [Fact]
    public void Erro_de_qa_para_o_build_sem_gravar_nada()
    {
        Translate("a001", (0, ""));

        var ex = Assert.Throws<QaBlockedException>(() => BuildWithQa(QaSeverity.Error));

        Assert.Contains("traducao-vazia", ex.Message);
        Assert.Single(ex.Report.Errors);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
        Assert.Equal(0, _engine.RebuildCalls);
    }

    [Fact]
    public void Erro_de_qa_para_o_build_mesmo_com_allow_warnings()
    {
        Translate("a001", (0, ""));

        Assert.Throws<QaBlockedException>(() => BuildWithQa(QaSeverity.Error, allowWarnings: true));
    }

    [Fact]
    public void Aviso_de_qa_para_o_build_por_padrao()
    {
        Translate("a001", (0, ""));

        var ex = Assert.Throws<QaBlockedException>(() => BuildWithQa(QaSeverity.Warning));

        Assert.Single(ex.Report.Warnings);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
    }

    [Fact]
    public void Com_allow_warnings_so_os_avisos_passam_e_vao_no_resultado()
    {
        Translate("a001", (0, ""));

        var result = BuildWithQa(QaSeverity.Warning, allowWarnings: true);

        Assert.Single(result.Qa.Warnings);
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
    }

    [Fact]
    public void Linha_nao_traduzida_nao_bloqueia_o_build_e_vai_contada_no_resultado()
    {
        Translate("a001", (0, "Olá"));

        var result = ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: new QaEngine([new UntranslatedQaRule()]));

        Assert.True(result.Qa.UntranslatedCount > 0);
        Assert.Empty(result.Qa.OtherIssues);
        Assert.NotEmpty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
    }

    [Fact]
    public void Outro_aviso_junto_da_nao_traduzida_continua_bloqueando_sem_allow_warnings()
    {
        Translate("a001", (0, ""));
        var qa = new QaEngine([new UntranslatedQaRule(), new QaEngineTests.FlagEmpty("traducao-vazia", QaSeverity.Warning)]);

        var ex = Assert.Throws<QaBlockedException>(() => ProjectBuilder.Build(_project, [_engine], cancellationToken: TestContext.Current.CancellationToken, qa: qa));

        Assert.DoesNotContain("nao-traduzida", ex.Message);
        Assert.Contains("traducao-vazia", ex.Message);
    }

    [Fact]
    public void Aviso_marcado_como_ok_nao_para_o_build_e_conta_como_silenciado()
    {
        Translate("a001", (0, ""));
        MarkOk(FirstUnit);

        var result = BuildWithQa(QaSeverity.Warning);

        Assert.Empty(result.Qa.Issues);
        Assert.Single(result.Qa.Silenced);
    }

    [Fact]
    public void Erro_marcado_como_ok_continua_parando_o_build()
    {
        Translate("a001", (0, ""));
        MarkOk(FirstUnit);

        Assert.Throws<QaBlockedException>(() => BuildWithQa(QaSeverity.Error));
    }
}
