using VNForge.App.Services;
using VNForge.App.ViewModels;
using System.Text;
using VNForge.Core.Engines;
using VNForge.Core.Projects;
using VNForge.Core.Qa;
using VNForge.Core.Text;

namespace VNForge.App.Tests;

[Trait("Risco", "R1")]
public sealed class BuildViewModelTests : IDisposable
{
    private const string Ysbin = "ysbin.ypf";
    private const string Original = "original";
    private const string Traduzido = "traduzido";

    private readonly TempFolder temp = new();
    private readonly string game;
    private readonly ProjectFolder folder;
    private readonly RecordingStarter starter = new();

    public BuildViewModelTests()
    {
        game = temp.SubFolder("jogo");
        File.WriteAllText(Path.Combine(game, Ysbin), Original);
        File.WriteAllText(Path.Combine(game, "jogo.exe"), "MZ");
        folder = ProjectFolder.Create(temp.SubFolder("projeto"), new ProjectConfig(
            "Teste",
            new GameConfig(game, "jogo.exe"),
            new EngineConfig("yuris"),
            new LanguagesConfig("ja", "pt-BR"),
            new EncodingConfig("fold"),
            AiConfig.Default));
        folder.SaveState(new PipelineState([], new Dictionary<string, StageState>(), game));
    }

    public void Dispose() => temp.Dispose();

    private string GameFile => Path.Combine(game, Ysbin);

    private void PutBuild(string name = Ysbin, string content = Traduzido)
    {
        var path = Path.Combine(folder.BuildDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task<BuildViewModel> OpenAsync()
    {
        var vm = new BuildViewModel(new BackgroundTaskService(), [new FakeEngine()], starter);
        vm.Project = ProjectSummaryReader.Read(folder.Root);
        await vm.ReloadAsync();
        return vm;
    }

    [Fact]
    public async Task Injetar_pede_confirmacao_e_so_grava_depois_de_confirmar()
    {
        PutBuild();
        var vm = await OpenAsync();

        vm.InjectCommand.Execute(null);

        Assert.True(vm.IsConfirming);
        Assert.Contains("1 arquivo(s)", vm.ConfirmationText);
        Assert.Equal(new PlannedChange(Ysbin, "Substitui o original"), Assert.Single(vm.ConfirmationFiles));
        Assert.Equal(Original, File.ReadAllText(GameFile));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(vm.IsConfirming);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
        var backup = Assert.Single(Directory.GetDirectories(folder.BackupDirectory));
        Assert.Equal(Original, File.ReadAllText(Path.Combine(backup, Ysbin)));
        Assert.Equal(backup, vm.BackupLocation);
        Assert.Contains(vm.Log, l => l.Message.StartsWith("Backup: ", StringComparison.Ordinal));
        Assert.Contains(vm.Log, l => l.Message.Contains("Injetado: 1 arquivo(s)", StringComparison.Ordinal));
        Assert.True(vm.RestoreCommand.CanExecute(null));
        Assert.False(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_pede_confirmacao_e_devolve_o_original()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));

        vm.RestoreCommand.Execute(null);

        Assert.True(vm.IsConfirming);
        Assert.Contains("Restaurar", vm.ConfirmationText);
        Assert.Equal(new PlannedChange(Ysbin, "Devolve o original"), Assert.Single(vm.ConfirmationFiles));
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.Contains(vm.Log, l => l.Message.Contains("Restaurado: 1 arquivo(s)", StringComparison.Ordinal));
        Assert.True(vm.InjectCommand.CanExecute(null));
    }

    [Fact]
    public async Task Restaurar_remove_o_arquivo_que_a_injecao_criou()
    {
        PutBuild("pac/extra.ypf", "novo");
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        var extra = Path.Combine(game, "pac", "extra.ypf");
        Assert.True(File.Exists(extra));

        vm.RestoreCommand.Execute(null);
        Assert.Equal(new PlannedChange("pac/extra.ypf", "Remove o arquivo criado pela injeção"), Assert.Single(vm.ConfirmationFiles));
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.False(File.Exists(extra));
        Assert.Contains(vm.Log, l => l.Message.Contains("removido: 1 arquivo(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Build_que_mudou_depois_do_plano_confirmado_nao_e_gravado_no_jogo()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        PutBuild(content: "outro conteúdo, que a tela não mostrou");

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("mudou depois da conferência", vm.ErrorMessage);
        Assert.Equal(Original, File.ReadAllText(GameFile));
        Assert.False(Directory.Exists(folder.BackupDirectory) && Directory.GetDirectories(folder.BackupDirectory).Length > 0);
    }

    [Fact]
    public async Task Backup_mais_novo_que_o_confirmado_nao_e_ignorado_na_restauracao()
    {
        PutBuild();
        var vm = await OpenAsync();
        vm.InjectCommand.Execute(null);
        await vm.ConfirmCommand.ExecuteAsync(null);
        vm.RestoreCommand.Execute(null);
        var newer = Path.Combine(folder.BackupDirectory, "9999-12-31_2359");
        Directory.CreateDirectory(newer);
        var manifest = Directory.GetDirectories(folder.BackupDirectory).Where(d => d != newer).Single();
        File.Copy(Path.Combine(manifest, "vnforge-backup.json"), Path.Combine(newer, "vnforge-backup.json"));
        File.Copy(Path.Combine(manifest, Ysbin), Path.Combine(newer, Ysbin));

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Contains("Os backups mudaram depois da conferência", vm.ErrorMessage);
        Assert.Equal(Traduzido, File.ReadAllText(GameFile));
    }

    // ---- Tela no nível do design v2 (#246) ----


    private sealed class ErrorOnOlaRule : UnitQaRule
    {
        public override string Id => "erro-ola";

        public override string Description => "Teste.";

        public override QaSeverity Severity => QaSeverity.Error;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == "Olá")
            {
                yield return "recusada";
            }
        }
    }

    private sealed class ErrorOnEveryLineRule : UnitQaRule
    {
        public override string Id => "erro-todas";

        public override string Description => "Teste.";

        public override QaSeverity Severity => QaSeverity.Error;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            yield return "recusada";
        }
    }

    private sealed class BadTranslationRule : UnitQaRule
    {
        public override string Id => "traducao-ruim";

        public override string Description => "Teste.";

        public override QaSeverity Severity => QaSeverity.Warning;

        protected override IEnumerable<string> CheckUnit(TextUnit unit, QaContext context)
        {
            if (unit.Translation == "Olá")
            {
                yield return "recusada";
            }
        }
    }

    private sealed class BuildableEngine : IEngine, IGameUnpacker, IGameRepacker
    {
        private readonly BuildableFormat format = new();

        public string Id => "yuris";

        public EngineDetection Detect(string gameDir) => EngineDetection.NotDetected;

        public IReadOnlyList<IArchiveFormat> Archives => [];

        public IReadOnlyList<IScriptFormat> Scripts => [format];

        public IReadOnlyList<IGamePatch> Patches => [];

        public GameUnpackResult Unpack(string gameDir, string destinationDirectory, CancellationToken cancellationToken = default) => new([]);

        public UnpackedGame Open(string unpackedDirectory) =>
            new(new BuildableContext(), [.. Directory.EnumerateFiles(unpackedDirectory, "*", SearchOption.AllDirectories).Select(f => new UnpackedScript(Path.GetFileName(f), f, format, null))]);

        public GameRepackResult Repack(string gameDir, string unpackedDirectory, IReadOnlyList<RebuiltScript> rebuilt, string outputDirectory, Func<string, string>? originalOf = null, CancellationToken cancellationToken = default)
        {
            File.WriteAllText(Path.Combine(outputDirectory, Ysbin), "reconstruido");
            return new GameRepackResult([new RepackedArchive(Ysbin, rebuilt.Count)]);
        }
    }

    private sealed class BuildableContext() : EngineContext("yuris");

    private sealed class BuildableFormat : IScriptFormat
    {
        public string Id => "fake";

        public IReadOnlyList<string> FileExtensions => [".fake"];

        public ScriptExtraction Extract(ScriptFile file, EngineContext ctx) => new([]);

        public byte[] Rebuild(ScriptFile file, IReadOnlyList<TextUnit> units, ITextEncoder encoder, EngineContext ctx) => Encoding.UTF8.GetBytes("y");
    }

    private sealed class BlockingStarter : IGameProcessStarter
    {
        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public void Start(string executablePath, string workingDirectory)
        {
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class RecordingStarter : IGameProcessStarter
    {
        public string? Executable { get; private set; }

        public string? WorkingDirectory { get; private set; }

        public void Start(string executablePath, string workingDirectory)
        {
            Executable = executablePath;
            WorkingDirectory = workingDirectory;
        }
    }

    /// <summary>Hora fixa; cada leitura do cronômetro avança o passo, então a duração de uma operação é conhecida.</summary>
    private sealed class SteppingClock(DateTimeOffset now, TimeSpan step) : TimeProvider
    {
        private long ticks;

        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp()
        {
            var current = ticks;
            ticks += (long)step.TotalMilliseconds;
            return current;
        }
    }

}
