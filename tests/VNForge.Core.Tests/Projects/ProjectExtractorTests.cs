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

    private static TextUnit Translated(string id, string source, string translation, TranslationStatus status = TranslationStatus.Translated) =>
        new(id, TextUnitKind.Dialogue, null, source, translation, status) { Notes = "nota" };

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
    public void Extracao_que_falha_nao_grava_estado()
    {
        WriteGame(A001Fake, DialogueHello);
        WriteGame(A002Fake, "quebrado");
        var project = NewProject();

        Assert.Throws<VNForgeException>(() => ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken));

        Assert.False(File.Exists(project.StatePath));
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
    public void Scripts_json_quebrado_nao_bloqueia_a_extracao_nem_e_apagado()
    {
        WriteGame(A001Fake, DialogueHello);
        var project = NewProject();
        File.WriteAllText(project.ScriptNamesPath, "isto nao e json");

        ProjectExtractor.Extract(project, [new FakeEngine()], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("isto nao e json", File.ReadAllText(project.ScriptNamesPath));
        Assert.True(File.Exists(project.TextPath("a001")));
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

    private sealed class FakeEngine(string? displayName = "Prologo", string? folder = null) : IEngine, IGameUnpacker
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
                    .Select(f => new UnpackedScript(Path.GetFileName(f), f, _format, displayName) { Folder = folder })
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
