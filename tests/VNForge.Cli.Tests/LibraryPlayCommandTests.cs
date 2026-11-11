using System.Text.Json;
using VNForge.Core.Library;

namespace VNForge.Cli.Tests;

public sealed class LibraryPlayCommandTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private readonly CliHarness _cli = new();
    private readonly ManualClock _clock = new(Start);

    public LibraryPlayCommandTests()
    {
        _cli.Cli.Clock = _clock;
        _cli.Cli.PlayTracking = new PlayTrackingOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromDays(100), TimeSpan.FromDays(100));
    }

    public void Dispose() => _cli.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string GameFolder => Path.Combine(_cli.Root, "colecao", "CLANNAD");

    private async Task<RunResult> Run(params string[] args) => await _cli.RunAsync(args, Ct);

    private static JsonElement Data(RunResult result) => JsonDocument.Parse(result.Output).RootElement.GetProperty("data");

    private LibraryFile File => new(_cli.UserConfigDirectory);

    // Arquivos vazios: o lançador é falso e nada é executado.
    private LibraryGame AddGame(string? exe = "CLANNAD.exe", bool japanese = false, string title = "CLANNAD", string folderName = "CLANNAD")
    {
        var folder = Path.Combine(_cli.Root, "colecao", folderName);
        Directory.CreateDirectory(folder);
        if (exe is not null)
        {
            System.IO.File.WriteAllText(Path.Combine(folder, exe), "x");
        }

        var game = new LibraryGame(GameLibrary.IdFor(folder), folder, exe, LibraryEngine.Unknown, title, null, Start) { RequiresJapaneseLocale = japanese };
        var library = File.Load();
        library.Merge([game], Path.Combine(_cli.Root, "colecao"));
        File.Save(library);
        return game;
    }

    private string AddEmulator()
    {
        var folder = Path.Combine(_cli.Root, "LE");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "LEProc.exe");
        System.IO.File.WriteAllText(exe, "x");
        return exe;
    }

    private FakeLauncher UseLauncher(params (TimeSpan Advance, GameProcessState State)[] script)
    {
        var launcher = new FakeLauncher(_clock, script);
        _cli.Cli.GameLauncher = launcher;
        return launcher;
    }

    [Fact]
    public async Task Play_json_traz_a_sessao_e_o_total()
    {
        AddGame();
        UseLauncher((TimeSpan.FromMinutes(30), GameProcessState.Exited));
        await Run("library", "play", "CLANNAD");
        UseLauncher((TimeSpan.FromMinutes(15), GameProcessState.Exited));

        var data = Data(await Run("library", "play", "CLANNAD", "--json"));

        Assert.Equal(900, data.GetProperty("session").GetProperty("seconds").GetInt64());
        Assert.False(data.GetProperty("session").GetProperty("interrupted").GetBoolean());
        var play = data.GetProperty("game").GetProperty("play");
        Assert.Equal(2700, play.GetProperty("totalSeconds").GetInt64());
        Assert.Equal(2, play.GetProperty("sessions").GetInt32());
        Assert.Equal("45 min", play.GetProperty("total").GetString());
    }

    [Fact]
    public async Task Locale_ja_na_chamada_liga_o_Locale_Emulator_so_dessa_vez()
    {
        AddGame();
        await Run("library", "emulator", AddEmulator());
        var launcher = UseLauncher((TimeSpan.FromMinutes(5), GameProcessState.Exited));

        await Run("library", "play", "CLANNAD", "--locale", "ja");
        Assert.True(launcher.Launched!.ViaLocaleEmulator);

        await Run("library", "play", "CLANNAD");
        Assert.False(launcher.Launched.ViaLocaleEmulator);
        Assert.False(File.Load().Games.Single().RequiresJapaneseLocale);
    }

    [Fact]
    public async Task Locale_que_nao_e_ja_e_recusado_sem_abrir_o_jogo()
    {
        AddGame();
        var launcher = UseLauncher((TimeSpan.FromMinutes(5), GameProcessState.Exited));

        var result = await Run("library", "play", "CLANNAD", "--locale", "ko");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--locale aceita ja", result.Error);
        Assert.Null(launcher.Launched);
    }

    [Fact]
    public async Task Jogo_sem_executavel_e_recusado()
    {
        AddGame(exe: null);

        var result = await Run("library", "play", "CLANNAD");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("não tem executável", result.Error);
    }

    [Fact]
    public async Task List_mostra_tempo_e_ultimo_jogado_e_recent_poe_o_ultimo_no_topo()
    {
        AddGame(title: "Alfa", folderName: "Alfa", exe: "Alfa.exe");
        AddGame(title: "Bravo", folderName: "Bravo", exe: "Bravo.exe");
        AddGame(title: "Charlie", folderName: "Charlie", exe: "Charlie.exe");
        UseLauncher((TimeSpan.FromMinutes(90), GameProcessState.Exited));
        await Run("library", "play", "Alfa");
        UseLauncher((TimeSpan.FromMinutes(10), GameProcessState.Exited));
        await Run("library", "play", "Charlie");

        var table = await Run("library", "list", "--recent");
        var json = Data(await Run("library", "list", "--recent", "--json"));
        var byTitle = Data(await Run("library", "list", "--json"));

        Assert.Contains("1 h 30 min", table.Output);
        Assert.Contains("10 min", table.Output);
        Assert.Equal(["Charlie", "Alfa", "Bravo"], json.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()));
        Assert.Equal(["Alfa", "Bravo", "Charlie"], byTitle.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("title").GetString()));
        var charlie = json.GetProperty("games")[0].GetProperty("play");
        Assert.Equal(600, charlie.GetProperty("totalSeconds").GetInt64());
        Assert.Equal(0, json.GetProperty("games")[2].GetProperty("play").GetProperty("sessions").GetInt32());
        Assert.False(json.GetProperty("games")[2].GetProperty("play").TryGetProperty("lastPlayedAt", out _));
    }

    [Fact]
    public async Task List_de_um_jogo_mostra_o_tempo_e_a_localidade()
    {
        AddGame(japanese: true);
        UseLauncher((TimeSpan.FromMinutes(25), GameProcessState.Exited));
        await Run("library", "emulator", AddEmulator());
        await Run("library", "play", "CLANNAD");

        var result = await Run("library", "list", "CLANNAD");

        Assert.Contains("Tempo de jogo: 25 min em 1 sessão", result.Output);
        Assert.Contains("Localidade japonesa: sim", result.Output);
    }

    [Fact]
    public async Task Option_liga_e_desliga_a_localidade_japonesa_do_jogo()
    {
        AddGame();

        var on = await Run("library", "option", "CLANNAD", "--japanese-locale", "sim");
        var same = Data(await Run("library", "option", "CLANNAD", "--japanese-locale", "sim", "--json"));
        var off = await Run("library", "option", "CLANNAD", "--japanese-locale", "nao");

        Assert.Contains("ligada", on.Output);
        Assert.False(same.GetProperty("changed").GetBoolean());
        Assert.Contains("desligada", off.Output);
        Assert.False(File.Load().Games.Single().RequiresJapaneseLocale);
    }

    [Fact]
    public async Task Emulator_grava_o_LEProc_acha_pela_pasta_e_esquece_com_clear()
    {
        var emulator = AddEmulator();

        var byFolder = Data(await Run("library", "emulator", Path.GetDirectoryName(emulator)!, "--json"));
        Assert.Equal(emulator, byFolder.GetProperty("localeEmulator").GetString());
        Assert.Equal(emulator, File.Load().LocaleEmulatorPath);
        Assert.Contains(emulator, (await Run("library", "emulator")).Output);

        await Run("library", "emulator", "--clear");
        Assert.Null(File.Load().LocaleEmulatorPath);
        Assert.Contains("não configurado", (await Run("library", "emulator")).Output);
    }

    [Fact]
    public async Task List_avisa_da_sessao_invalida_do_arquivo_de_tempo_e_segue()
    {
        AddGame();
        await System.IO.File.WriteAllTextAsync(
            new PlayLogFile(_cli.UserConfigDirectory).Path,
            """{"version":1,"games":{"g1":[{"id":"a","startedAt":"2026-11-09T20:00:00+00:00","endedAt":"2026-11-09T19:00:00+00:00","duration":"00:30:00"}]}}""",
            Ct);

        var result = await Run("library", "list");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Aviso:", result.Error);
        Assert.Contains("CLANNAD", result.Output);
        var copy = Assert.Single(Directory.GetFiles(_cli.UserConfigDirectory, "playtime.json.invalido-*"));
        Assert.Contains(copy, result.Error);

        // Jogar regrava o arquivo sem a sessão inválida; a cópia do original continua uma só.
        UseLauncher((TimeSpan.FromMinutes(5), GameProcessState.Exited));
        var played = await Run("library", "play", "CLANNAD");
        Assert.Equal(0, played.ExitCode);
        Assert.Equal([copy], Directory.GetFiles(_cli.UserConfigDirectory, "playtime.json.invalido-*"));
        Assert.Empty(new PlayLogFile(_cli.UserConfigDirectory).Load().Warnings);
    }

    [Fact]
    public async Task Emulator_recusa_caminho_que_nao_leva_a_um_LEProc()
    {
        var result = await Run("library", "emulator", Path.Combine(_cli.Root, "nada"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("não foi encontrado", result.Error);
        Assert.Null(File.Load().LocaleEmulatorPath);
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            _now += by;
            _ticks += by.Ticks;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _ticks;
    }

    private sealed class FakeLauncher(ManualClock clock, (TimeSpan Advance, GameProcessState State)[] script) : IGameLauncher
    {
        public GameLaunch? Launched { get; private set; }

        public IGameProcess Start(GameLaunch launch)
        {
            Launched = launch;
            return new FakeProcess(clock, script);
        }
    }

    private sealed class FakeProcess(ManualClock clock, (TimeSpan Advance, GameProcessState State)[] script) : IGameProcess
    {
        private int _polls;

        public GameProcessState Poll()
        {
            var (advance, state) = _polls < script.Length ? script[_polls] : (TimeSpan.Zero, GameProcessState.Exited);
            _polls++;
            clock.Advance(advance);
            return state;
        }

        public void Dispose()
        {
        }
    }
}
