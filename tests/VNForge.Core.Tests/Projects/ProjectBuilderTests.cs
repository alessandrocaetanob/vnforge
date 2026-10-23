using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Formatting;
using VNForge.Core.Qa;
using VNForge.Core.Tests.Qa;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class ProjectBuilderTests : IDisposable
{
    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly ProjectFolder _project;
    private readonly FakeEngine _engine = new();

    public ProjectBuilderTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, "a001.fake"), "dialogue|Olá\nname|Ana");
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

    private BuildResult Build() => ProjectBuilder.Build(_project, [_engine], TestContext.Current.CancellationToken, qa: new QaEngine([]));

    private void Translate(string key, params (int Index, string Text)[] changes)
    {
        var units = _project.LoadAllText().TryGetValue(key, out var saved)
            ? saved
            : _engine.Scripts[0].Extract(new ScriptFile(key + ".fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, "pacote.fake", key + ".fake"))), new FakeContext()).Units;
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
            File.ReadAllText(Path.Combine(_project.BuildDirectory, "pacote.fake")));
    }

    [Fact]
    public void Entrega_ao_repacker_so_o_script_com_traducao_e_o_conteudo_original()
    {
        Translate("a001", (0, "Hello"));

        var result = Build();

        var built = Assert.Single(result.RebuiltScripts);
        Assert.Equal(new BuiltScript("a001", "a001.fake", 1), built);
        Assert.Equal(1, result.TotalLines);
        var script = Assert.Single(_engine.LastRebuilt);
        Assert.Equal(Path.Combine(_project.GameCacheDirectory, "pacote.fake", "a001.fake"), script.Path);
        Assert.Equal("dialogue|Olá\nname|Ana", Encoding.UTF8.GetString(script.Original));
        Assert.Equal("dialogue|Hello\nname|Ana", Encoding.UTF8.GetString(script.Content));
        Assert.Equal(1, Assert.Single(result.Archives).ReplacedFiles);
    }

    private BuildResult BuildIncremental(string? encoding = null) =>
        ProjectBuilder.Build(_project, [_engine], TestContext.Current.CancellationToken, qa: new QaEngine([]), onlyChanged: true, encodingStrategy: encoding);

    [Fact]
    public void Incremental_sem_mudanca_desde_o_build_anterior_nao_reconstroi_nenhum_script()
    {
        Translate("a001", (0, "Hello"));
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
        Translate("a001", (0, "Hello"));
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
        Translate("a001", (0, "Hello"));
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
        Translate("a001", (0, "Hello"));

        var result = BuildIncremental();

        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
        Assert.Equal(0, result.ReusedScripts);
    }

    [Fact]
    public void Incremental_com_build_apagado_refaz_os_pacotes_sem_reconstruir_o_script()
    {
        Translate("a001", (0, "Hello"));
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
        Translate("a001", (0, "Hello"));
        BuildIncremental();
        File.WriteAllText(Path.Combine(_project.CacheDirectory, "build-state.json"), "{ não é json");

        var result = BuildIncremental();

        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
    }

    [Fact]
    public void Build_completo_ignora_o_estado_anterior()
    {
        Translate("a001", (0, "Hello"));
        BuildIncremental();
        var calls = _engine.RebuildCalls;

        var result = Build();

        Assert.Equal(calls + 1, _engine.RebuildCalls);
        Assert.Equal("a001", Assert.Single(result.RebuiltScripts).Key);
    }

    [Fact]
    public void Estrategia_pedida_vale_so_para_este_build_e_nao_muda_o_vnforge_json()
    {
        Translate("a001", (0, "Hello"));

        var result = ProjectBuilder.Build(_project, [_engine], TestContext.Current.CancellationToken, qa: new QaEngine([]), encodingStrategy: "fold");

        Assert.Equal("fold", result.Encoding.Strategy);
        Assert.Equal("sjis", _project.LoadConfig().Encoding.Strategy);
    }

    [Fact]
    public void Estrategia_desconhecida_no_build_e_recusada_em_portugues()
    {
        var ex = Assert.Throws<VNForgeException>(() =>
            ProjectBuilder.Build(_project, [_engine], TestContext.Current.CancellationToken, qa: new QaEngine([]), encodingStrategy: "latin9"));

        Assert.Contains("latin9", ex.Message, StringComparison.Ordinal);
    }

    private void SaveProfile(string json) => File.WriteAllText(_project.FormattingPath, json);

    private string BuiltContent() => File.ReadAllText(Path.Combine(_project.BuildDirectory, "pacote.fake"));

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
        Translate("a001", (0, "aaa bbb ccc"), (1, "Ana Maria Souza"));
        Translate("a002", (0, "aaa bbb ccc"));

        Build();

        var content = BuiltContent();
        Assert.Contains("dialogue|aaa bbb ccc\nname|Ana Maria Souza", content);
        Assert.Contains(@"narration|aaa\nbbb\nccc", content);
    }

    [Fact]
    public void Formatacao_nao_altera_o_text_json_em_disco()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "charWidth": "uniform" } }""");
        Translate("a001", (0, "aaa bbb ccc"));
        var before = File.ReadAllText(_project.TextPath("a001"));

        Build();

        Assert.Equal(before, File.ReadAllText(_project.TextPath("a001")));
    }

    [Fact]
    public void Caixa_acima_do_limite_de_linhas_vira_aviso_com_o_id_da_linha()
    {
        SaveProfile("""{ "wrap": { "enabled": true, "maxWidth": 5, "maxLines": 2, "charWidth": "uniform" } }""");
        Translate("a001", (0, "aaa bbb ccc"));

        var result = Build();

        Assert.Contains(result.Warnings, w => w.StartsWith("a001:000000: caixa 1 com 3 linhas (máximo 2)", StringComparison.Ordinal));
        Assert.StartsWith(@"dialogue|aaa\nbbb\nccc", BuiltContent());
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

        Assert.Contains("a001:000000", ex.Message);
        Assert.Contains("formatting.json", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_project.BuildDirectory));
    }

    [Fact]
    public void Formatting_json_invalido_aborta_apontando_o_campo()
    {
        SaveProfile("""{ "wrap": { "maxWidth": 0 } }""");
        Translate("a001", (0, "Hello"));

        var ex = Assert.Throws<FormattingProfileException>(() => Build());

        Assert.Equal("$.wrap.maxWidth", ex.Field);
        Assert.Equal(0, _engine.RebuildCalls);
    }

    [Fact]
    public void Traducao_igual_ao_original_ou_sem_status_nao_reconstroi()
    {
        var units = _engine.Scripts[0].Extract(new ScriptFile("a001.fake", File.ReadAllBytes(Path.Combine(_project.GameCacheDirectory, "pacote.fake", "a001.fake"))), new FakeContext()).Units;
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

    [Fact]
    public void Lista_de_uma_vez_os_casos_de_varias_linhas_e_scripts()
    {
        Translate("a001", (0, "«Olá»"), (1, "Aña"));
        Translate("a002", (0, "Chovia ª vez"));

        var ex = Assert.Throws<EncodingReportException>(() => Build());

        Assert.Equal(["a001:000000", "a001:000000", "a001:000000", "a001:000001", "a002:000000"], ex.Report.Blocking.Select(i => i.UnitId));
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
        var previous = Path.Combine(_project.BuildDirectory, "anterior.txt");
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
        File.WriteAllText(Path.Combine(_project.BuildDirectory, "anterior.txt"), "x");

        Build();

        Assert.False(File.Exists(Path.Combine(_project.BuildDirectory, "anterior.txt")));
        Assert.True(File.Exists(Path.Combine(_project.BuildDirectory, "pacote.fake")));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".tmp"));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".old"));
    }

    [Fact]
    public void Cancelamento_mantem_o_build_anterior_e_nao_deixa_temporario()
    {
        Directory.CreateDirectory(_project.BuildDirectory);
        var previous = Path.Combine(_project.BuildDirectory, "anterior.txt");
        File.WriteAllText(previous, "x");
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ProjectBuilder.Build(_project, [_engine], source.Token));

        Assert.True(File.Exists(previous));
        Assert.False(Directory.Exists(_project.BuildDirectory + ".tmp"));
    }

    [Fact]
    public void Nao_altera_a_pasta_do_jogo_nem_o_cache()
    {
        Translate("a001", (0, "Hello"));
        var gameBefore = File.ReadAllText(Path.Combine(_game, "a001.fake"));
        var cacheBefore = File.ReadAllText(Path.Combine(_project.GameCacheDirectory, "pacote.fake", "a001.fake"));

        Build();

        Assert.Equal(gameBefore, File.ReadAllText(Path.Combine(_game, "a001.fake")));
        Assert.Equal(cacheBefore, File.ReadAllText(Path.Combine(_project.GameCacheDirectory, "pacote.fake", "a001.fake")));
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
        var unknown = Assert.Throws<VNForgeException>(() => ProjectBuilder.Build(_project, [new NoRepackEngine { EngineId = "outra" }], TestContext.Current.CancellationToken));
        var unsupported = Assert.Throws<VNForgeException>(() => ProjectBuilder.Build(_project, [new NoRepackEngine()], TestContext.Current.CancellationToken));

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

        var ex = Assert.Throws<VNForgeException>(() => ProjectBuilder.Build(inside, [_engine], TestContext.Current.CancellationToken));

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
        ProjectBuilder.Build(_project, [_engine], TestContext.Current.CancellationToken, qa: new QaEngine([new QaEngineTests.FlagEmpty("traducao-vazia", severity)]), allowWarnings: allowWarnings);

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
    public void Aviso_marcado_como_ok_nao_para_o_build_e_conta_como_silenciado()
    {
        Translate("a001", (0, ""));
        MarkOk("a001:000000");

        var result = BuildWithQa(QaSeverity.Warning);

        Assert.Empty(result.Qa.Issues);
        Assert.Single(result.Qa.Silenced);
    }

    [Fact]
    public void Erro_marcado_como_ok_continua_parando_o_build()
    {
        Translate("a001", (0, ""));
        MarkOk("a001:000000");

        Assert.Throws<QaBlockedException>(() => BuildWithQa(QaSeverity.Error));
    }

    private sealed class FakeEngine : IEngine, IGameUnpacker, IGameRepacker
    {
        private readonly FakeFormat _format = new();

        public string Id => "fake";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [_format];

        public IReadOnlyList<IGamePatch> Patches => [];

        public int RebuildCalls => _format.Calls;

        public IReadOnlyList<RebuiltScript> LastRebuilt { get; private set; } = [];

        public int RepackCalls { get; private set; }

        public VNForgeException? RepackFailure { get; set; }

        public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            var archive = Path.Combine(destinationDirectory, "pacote.fake");
            Directory.CreateDirectory(archive);
            foreach (var file in Directory.EnumerateFiles(gameDir, "*.fake"))
            {
                File.Copy(file, Path.Combine(archive, Path.GetFileName(file)), overwrite: true);
            }

            return new GameUnpackResult([new UnpackedArchive("pacote.fake", "pacote.fake", 2, 0)]);
        }

        public UnpackedGame Open(string unpackedDirectory) =>
            new(
                new FakeContext(),
                Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(f => new UnpackedScript(Path.GetFileName(f), f, _format, null))
                    .ToList());

        public GameRepackResult Repack(string gameDir, string unpackedDirectory, IReadOnlyList<RebuiltScript> rebuilt, string outputDirectory, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RepackFailure is not null)
            {
                throw RepackFailure;
            }

            RepackCalls++;
            LastRebuilt = rebuilt;
            var contents = Directory.EnumerateFiles(Path.Combine(unpackedDirectory, "pacote.fake"))
                .Order(StringComparer.Ordinal)
                .Select(f => rebuilt.FirstOrDefault(r => r.Path == f)?.Content ?? File.ReadAllBytes(f))
                .Select(Encoding.UTF8.GetString);
            File.WriteAllText(Path.Combine(outputDirectory, "pacote.fake"), string.Join('\u0001', contents));
            return new GameRepackResult([new RepackedArchive("pacote.fake", rebuilt.Count)]);
        }
    }

    private sealed class FakeContext() : EngineContext("fake");

    private sealed class FakeFormat : IScriptFormat
    {
        public int Calls { get; private set; }

        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions => [".fake"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx)
        {
            var units = Encoding.UTF8.GetString(file.Content.Span).Split('\n').Select(line =>
            {
                var bar = line.IndexOf('|', StringComparison.Ordinal);
                return new TextUnit(file.UnitId(0), Enum.Parse<TextUnitKind>(line[..bar], ignoreCase: true), null, line[(bar + 1)..], null, TranslationStatus.Untranslated);
            }).Select((u, i) => u with { Id = file.UnitId(i) }).ToList();
            return new ScriptExtraction(units);
        }

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx)
        {
            Calls++;
            var lines = Encoding.UTF8.GetString(file.Content.Span).Split('\n');
            foreach (var unit in units.Where(u => u.NeedsRewrite))
            {
                var index = int.Parse(unit.Id[(unit.Id.IndexOf(':', StringComparison.Ordinal) + 1)..], System.Globalization.CultureInfo.InvariantCulture);
                try
                {
                    _ = encoder.Encode(unit.Translation!);
                }
                catch (EncoderFallbackException e)
                {
                    throw new EncoderFallbackException($"A tradução da unidade '{unit.Id}' não pode ser gravada: {e.Message}", e);
                }

                var bar = lines[index].IndexOf('|', StringComparison.Ordinal);
                lines[index] = lines[index][..(bar + 1)] + unit.Translation;
            }

            return Encoding.UTF8.GetBytes(string.Join('\n', lines));
        }
    }
}
