using System.Security.Cryptography;
using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Text;

namespace VNForge.Core.Tests.Projects;

public sealed class ProjectExtractorTests : IDisposable
{
    private const string GameExe = "Jogo.exe";
    private const string A001Fake = "a001.fake";
    private const string A002Fake = "a002.fake";
    private const string DialogueHi = "dialogue|Oi";
    private const string FirstUnitId = "a001:000000";
    private const string HelloText = "Hello";
    private const string DialogueHello = "dialogue|Olá";
    private const string PackageFake = "pacote.fake";

    private readonly TempFolder _temp = new();
    private readonly string _game;
    private readonly string _projectDir;

    public ProjectExtractorTests()
    {
        _game = Path.Combine(_temp.Path, "jogo");
        _projectDir = Path.Combine(_temp.Path, "projeto");
        Directory.CreateDirectory(_game);
    }

    public void Dispose() => _temp.Dispose();

    private void WriteGame(string name, string content) => File.WriteAllText(Path.Combine(_game, name), content);

    private ProjectFolder NewProject(string engineId = "fake", string? gamePath = null) =>
        ProjectFolder.Create(
            _projectDir,
            new ProjectConfig(
                "Teste",
                new GameConfig(gamePath ?? _game.Replace(Path.DirectorySeparatorChar, '/'), GameExe),
                new EngineConfig(engineId),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));

    private static Dictionary<string, string> HashTree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                f => Path.GetRelativePath(directory, f),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    [Fact]
    public void Grava_um_text_json_por_script_e_conta_as_linhas_por_kind()
    {
        WriteGame(A001Fake, "dialogue|Olá\ndialogue|Tudo bem?\nname|Ana");
        WriteGame(A002Fake, "choice|Sim\nnarration|Chovia.\nother|x");
        var project = NewProject();

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a001", "a002"], result.Scripts.Select(s => s.Key));
        Assert.Equal([3, 3], result.Scripts.Select(s => s.Lines));
        Assert.Equal(6, result.TotalLines);
        Assert.Equal(2, result.LinesByKind[TextUnitKind.Dialogue]);
        Assert.Equal(1, result.LinesByKind[TextUnitKind.Narration]);
        Assert.Equal(1, result.LinesByKind[TextUnitKind.Choice]);
        Assert.Equal(1, result.LinesByKind[TextUnitKind.Name]);
        Assert.Equal(0, result.LinesByKind[TextUnitKind.System]);
        Assert.Equal(1, result.LinesByKind[TextUnitKind.Other]);

        var units = project.LoadText("a001");
        Assert.Equal([FirstUnitId, "a001:000001", "a001:000002"], units.Select(u => u.Id));
        Assert.Equal(["Olá", "Tudo bem?", "Ana"], units.Select(u => u.Source));
        Assert.All(units, u => Assert.Equal(TranslationStatus.Untranslated, u.Status));
        Assert.Equal(["a001", "a002"], project.LoadAllText().Keys);
    }

    [Fact]
    public void Relata_cada_script_lido_em_ordem_e_um_relato_final_com_todos()
    {
        WriteGame(A001Fake, DialogueHi);
        WriteGame(A002Fake, DialogueHi);
        var reports = new List<ScriptWorkProgress>();

        ProjectExtractor.Extract(NewProject(), [new FakeEngine()], new SyncProgress<ScriptWorkProgress>(reports.Add), TestContext.Current.CancellationToken);

        Assert.Equal([A001Fake, A002Fake, string.Empty], reports.Select(r => r.Name));
        Assert.Equal([0, 1, 2], reports.Select(r => r.Done));
        Assert.All(reports, r => Assert.Equal(2, r.Total));
    }

    [Fact]
    public void Descompacta_para_o_cache_do_projeto_em_game()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(project.Root, ".cache", "game"), project.GameCacheDirectory);
        Assert.Equal(project.GameCacheDirectory, result.CacheDirectory);
        Assert.True(File.Exists(Path.Combine(project.GameCacheDirectory, PackageFake, A001Fake)));
        Assert.Equal(new UnpackedArchive(A001Fake, PackageFake, 1, 13), Assert.Single(result.Archives));
    }

    [Fact]
    public void Nao_altera_a_pasta_do_jogo()
    {
        WriteGame(A001Fake, DialogueHello);
        WriteGame(A002Fake, "name|Ana");
        WriteGame(GameExe, "MZ");
        Directory.CreateDirectory(Path.Combine(_game, "sub"));
        File.WriteAllText(Path.Combine(_game, "sub", "x.dat"), "dados");
        var before = HashTree(_game);
        var project = NewProject();

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before, HashTree(_game));
    }

    private static TextUnit Translated(string id, string source, string translation, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, null, source, translation, status) { Notes = "nota" };

    [Fact]
    public void Reextrair_sem_mudanca_no_jogo_nao_altera_o_text_json()
    {
        WriteGame(A001Fake, "dialogue|Olá\ndialogue|Tudo bem?");
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        var units = project.LoadText("a001");
        project.SaveText("a001", [units[0] with { Translation = HelloText, Status = TranslationStatus.Approved }, units[1]]);
        var before = HashTree(project.TextDirectory);

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before, HashTree(project.TextDirectory));
        Assert.Equal(new ReextractionSummary(Kept: 2, Outdated: 0, Added: 0, Removed: 0), result.Reextraction);
    }

    [Fact]
    public void Reextrair_marca_a_linha_de_original_alterado_e_guarda_a_traducao_antiga()
    {
        WriteGame(A001Fake, "dialogue|Olá\ndialogue|Tudo bem?");
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        project.SaveText("a001", [Translated(FirstUnitId, "Olá", HelloText), Translated("a001:000001", "Tudo bem?", "All good?", TranslationStatus.Reviewed)]);
        WriteGame(A001Fake, "dialogue|Olá\ndialogue|Tudo bem mesmo?");

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        var units = project.LoadText("a001");
        Assert.Equal(HelloText, units[0].Translation);
        Assert.Equal(TranslationStatus.Translated, units[0].Status);
        Assert.Null(units[0].PreviousSource);
        Assert.Equal("Tudo bem mesmo?", units[1].Source);
        Assert.Equal("All good?", units[1].Translation);
        Assert.Equal(TranslationStatus.Untranslated, units[1].Status);
        Assert.Equal("Tudo bem?", units[1].PreviousSource);
        Assert.Equal("nota", units[1].Notes);
        Assert.False(units[1].HasTranslation);
        Assert.Equal(new ReextractionSummary(Kept: 1, Outdated: 1, Added: 0, Removed: 0), result.Reextraction);
    }

    [Fact]
    public void Reextrair_de_novo_com_a_marca_pendente_mantem_o_original_antigo()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        project.SaveText("a001", [Translated(FirstUnitId, "Olá", HelloText)]);
        WriteGame(A001Fake, DialogueHi);
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        WriteGame(A001Fake, "dialogue|Oie");

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        var unit = Assert.Single(project.LoadText("a001"));
        Assert.Equal("Oie", unit.Source);
        Assert.Equal(HelloText, unit.Translation);
        Assert.Equal("Olá", unit.PreviousSource);
    }

    [Fact]
    public void Reextrair_limpa_a_marca_depois_que_a_linha_foi_tratada()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        project.SaveText("a001", [Translated(FirstUnitId, "Oi", "Hi") with { PreviousSource = "Olá antigo" }]);
        WriteGame(A001Fake, DialogueHi);

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(project.LoadText("a001")).PreviousSource);
    }

    [Fact]
    public void Reextrair_usa_o_locator_e_o_kind_da_nova_extracao()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        project.SaveText("a001", [Translated(FirstUnitId, "Olá", HelloText) with { Kind = TextUnitKind.System }]);
        WriteGame(A001Fake, DialogueHello);

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TextUnitKind.Dialogue, Assert.Single(project.LoadText("a001")).Kind);
    }

    [Fact]
    public void Reextrair_acrescenta_linhas_novas_e_guarda_o_texto_anterior_quando_algo_sai()
    {
        WriteGame(A001Fake, "dialogue|Olá\ndialogue|Tchau");
        WriteGame(A002Fake, "dialogue|Um");
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        project.SaveText("a001", [Translated(FirstUnitId, "Olá", HelloText), Translated("a001:000001", "Tchau", "Bye")]);
        project.SaveText("a002", [Translated("a002:000000", "Um", "One")]);
        var beforeA001 = File.ReadAllBytes(project.TextPath("a001"));
        File.Delete(Path.Combine(_game, A002Fake));
        WriteGame(A001Fake, DialogueHello);
        WriteGame("a003.fake", "dialogue|Novo");

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["a001", "a003"], project.LoadAllText().Keys.Order());
        Assert.Equal(new ReextractionSummary(Kept: 1, Outdated: 0, Added: 1, Removed: 2), result.Reextraction);
        var folder = Assert.Single(Directory.GetDirectories(project.TextBackupDirectory));
        Assert.Equal(beforeA001, File.ReadAllBytes(Path.Combine(folder, "a001.json")));
        Assert.True(File.Exists(Path.Combine(folder, "a002.json")));

        WriteGame(A001Fake, "dialogue|Outra");
        // O nome da pasta é o instante em ms: tira a primeira do caminho em vez de esperar o relógio andar.
        Directory.Move(folder, Path.Combine(project.TextBackupDirectory, "20000101-000000-000"));
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, Directory.GetDirectories(project.TextBackupDirectory).Length);
    }

    [Fact]
    public void Reextrair_sem_perdas_nao_cria_a_copia_do_texto_anterior()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(Directory.Exists(project.TextBackupDirectory));
    }

    [Fact]
    public void Grava_o_hash_dos_pacotes_originais_e_marca_a_etapa_extract()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        var state = project.LoadState();
        var original = Assert.Single(state.Originals);
        var bytes = File.ReadAllBytes(Path.Combine(_game, A001Fake));
        Assert.Equal(A001Fake, original.Path);
        Assert.Equal(bytes.Length, original.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), original.Sha256);
        Assert.Equal(StageStatus.Completed, state.Stages["extract"].Status);
    }

    [Fact]
    public void Extrair_de_novo_sem_mudanca_deixa_o_state_json_identico()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        var before = File.ReadAllBytes(project.StatePath);
        foreach (var file in Directory.EnumerateFiles(project.TextDirectory))
        {
            File.Delete(file);
        }

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before, File.ReadAllBytes(project.StatePath));
    }

    [Fact]
    public void Extracao_que_falha_nao_grava_estado()
    {
        WriteGame(A001Fake, DialogueHello);
        WriteGame(A002Fake, "quebrado");
        var project = NewProject();

        Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(project.StatePath));
    }

    [Fact]
    public void Cache_antigo_e_apagado_antes_de_descompactar()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        var stale = Path.Combine(project.GameCacheDirectory, "velho.fake", "sobra.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "x");

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void Script_ilegivel_aborta_citando_o_script_e_nao_grava_texto()
    {
        WriteGame(A001Fake, DialogueHello);
        WriteGame(A002Fake, "quebrado");
        var project = NewProject();

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(A002Fake, ex.Message);
        Assert.Contains("linha sem '|'", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(project.TextDirectory));
    }

    [Fact]
    public void Avisos_da_engine_saem_com_o_nome_do_script()
    {
        WriteGame(A001Fake, "dialogue|Olá\n#aviso");
        var project = NewProject();

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("a001.fake: linha de comentário ignorada", result.Warnings);
        Assert.Contains("aviso do unpack", result.Warnings);
        Assert.Contains("aviso do open", result.Warnings);
    }

    [Fact]
    public void Nome_legivel_do_script_vai_para_o_resultado()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Prologo", Assert.Single(result.Scripts).Name);
    }

    [Fact]
    public void Nome_legivel_do_script_fica_no_projeto()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Prologo", project.LoadScriptNames().DisplayName("a001"));
    }

    [Fact]
    public void Reextracao_mantem_o_nome_que_o_usuario_deu_ao_script()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);
        project.RenameScript("a001", "Capítulo 1");

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        var names = project.LoadScriptNames();
        Assert.Equal("Capítulo 1", names.DisplayName("a001"));
        Assert.Equal("Prologo", Assert.Single(names.Entries).Detected);
    }

    [Fact]
    public void Jogo_sem_nome_legivel_nao_cria_o_scripts_json()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();

        ProjectExtractor.Extract(project, [new FakeEngine(displayName: null)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(File.Exists(project.ScriptNamesPath));
    }

    [Fact]
    public void Duas_chaves_iguais_abortam_citando_os_dois_arquivos()
    {
        WriteGame(A001Fake, DialogueHello);
        WriteGame("A001.other", DialogueHi);
        var project = NewProject();

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("mesma chave 'a001'", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(project.TextDirectory));
    }

    [Fact]
    public void Falha_ao_gravar_o_texto_remove_o_que_ja_foi_gravado()
    {
        WriteGame(A001Fake, DialogueHello);
        WriteGame(A002Fake, DialogueHi);
        var project = NewProject();
        // Um diretório no lugar do arquivo de a002 faz a troca atômica do segundo script falhar.
        Directory.CreateDirectory(project.TextPath("a002"));

        Assert.ThrowsAny<Exception>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(project.TextPath("a001")));
    }

    [Fact]
    public void Cancelamento_antes_da_gravacao_nao_deixa_text_pela_metade()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: source.Token));

        Assert.Empty(Directory.EnumerateFiles(project.TextDirectory));
    }

    [Fact]
    public void Engine_desconhecida_e_erro_em_portugues()
    {
        var project = NewProject(engineId: "outra");

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("engine 'outra'", ex.Message);
    }

    [Fact]
    public void Engine_sem_suporte_a_extracao_e_erro_em_portugues()
    {
        var project = NewProject();

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new NoUnpackEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("ainda não suporta a extração", ex.Message);
    }

    [Fact]
    public void Pasta_do_jogo_inexistente_e_erro_em_portugues()
    {
        var project = NewProject(gamePath: Path.Combine(_temp.Path, "nao-existe").Replace(Path.DirectorySeparatorChar, '/'));

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("A pasta do jogo não existe", ex.Message);
    }

    [Fact]
    public void Game_path_relativo_vale_a_partir_da_pasta_do_projeto()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject(gamePath: "../jogo");

        var result = ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_game, result.GameDirectory);
    }

    [Fact]
    public void Projeto_dentro_da_pasta_do_jogo_e_recusado_sem_gravar_nada()
    {
        var inside = Path.Combine(_game, "projeto");
        var project = ProjectFolder.Create(
            inside,
            new ProjectConfig(
                "Teste",
                new GameConfig(_game.Replace(Path.DirectorySeparatorChar, '/'), GameExe),
                new EngineConfig("fake"),
                new LanguagesConfig("ja", "pt-BR"),
                new EncodingConfig("cp1252"),
                AiConfig.Default));
        WriteGame(A001Fake, DialogueHello);
        var before = HashTree(_game);

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("fica dentro da pasta do jogo", ex.Message);
        Assert.Equal(before, HashTree(_game));
    }

    [Fact]
    public void Cache_que_e_uma_juncao_para_dentro_do_jogo_e_recusado_sem_gravar_nada()
    {
        WriteGame(A001Fake, DialogueHello);
        var inside = Path.Combine(_game, "destino");
        Directory.CreateDirectory(inside);
        var project = NewProject();
        Directory.Delete(project.CacheDirectory);
        CreateDirectoryLink(project.CacheDirectory, inside);
        var before = HashTree(_game);

        try
        {
            var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains("fica dentro da pasta do jogo", ex.Message);
            Assert.Equal(before, HashTree(_game));
        }
        finally
        {
            Directory.Delete(project.CacheDirectory);
        }
    }

    [Fact]
    public void Jogo_sem_nenhum_script_depois_de_descompactar_e_erro()
    {
        WriteGame(GameExe, "MZ");
        var project = NewProject();

        var ex = Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("Nenhum script foi encontrado", ex.Message);
        Assert.Empty(Directory.EnumerateFiles(project.TextDirectory));
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            })!;
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
    }

    private sealed class NoUnpackEngine : IEngine
    {
        public string Id => "fake";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [];

        public IReadOnlyList<IGamePatch> Patches => [];
    }

    private sealed class FakeEngine(string? displayName = "Prologo") : IEngine, IGameUnpacker
    {
        private readonly FakeFormat _format = new();

        public string Id => "fake";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [_format];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default)
        {
            var archive = Path.Combine(destinationDirectory, PackageFake);
            Directory.CreateDirectory(archive);
            var count = 0;
            long bytes = 0;
            foreach (var file in Directory.EnumerateFiles(gameDir, "*.fake").Concat(Directory.EnumerateFiles(gameDir, "*.other")))
            {
                var content = File.ReadAllBytes(file);
                File.WriteAllBytes(Path.Combine(archive, Path.GetFileName(file)), content);
                count++;
                bytes += content.Length;
            }

            return new GameUnpackResult([new UnpackedArchive(A001Fake, PackageFake, count, bytes)]) { Warnings = ["aviso do unpack"] };
        }

        public UnpackedGame Open(string unpackedDirectory) =>
            new(
                new FakeContext(),
                Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(f => new UnpackedScript(Path.GetFileName(f), f, _format, displayName))
                    .ToList())
            { Warnings = ["aviso do open"] };
    }

    private sealed class FakeContext() : EngineContext("fake");

    private sealed class FakeFormat : IScriptFormat
    {
        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions => [".fake"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx)
        {
            var units = new List<TextUnit>();
            var warnings = new List<string>();
            foreach (var line in Encoding.UTF8.GetString(file.Content.Span).Split('\n'))
            {
                if (line.StartsWith('#'))
                {
                    warnings.Add("linha de comentário ignorada");
                    continue;
                }

                var bar = line.IndexOf('|', StringComparison.Ordinal);
                if (bar < 0)
                {
                    throw new InvalidDataException("linha sem '|'");
                }

                var kind = Enum.Parse<TextUnitKind>(line[..bar], ignoreCase: true);
                units.Add(new TextUnit(file.UnitId(units.Count), kind, null, line[(bar + 1)..], null, TranslationStatus.Untranslated));
            }

            return new ScriptExtraction(units) { Warnings = warnings };
        }

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx) =>
            throw new NotSupportedException();
    }
}
