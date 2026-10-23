using System.Text;
using VNForge.Core.Engines;
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

    private BuildResult Build() => ProjectBuilder.Build(_project, [_engine], TestContext.Current.CancellationToken);

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
        Translate("a001", (0, "«ªã"));

        var result = Build();

        Assert.Equal(["\"", "a", "a"], result.Encoding.Replaced.Select(i => i.Replacement));
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
