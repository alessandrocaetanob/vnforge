using VNForge.App.ViewModels;
using VNForge.Core.Library;

namespace VNForge.App.Tests;

/// <summary>O Jogar da Biblioteca (#549) sem janela: o tempo é contado pelo processo (um falso, com relógio que anda sozinho), e o último jogado sobe para "Continuar jogando".</summary>
public sealed class LibraryPlayTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _root = new();
    private readonly PlayClock _clock = new(Start);

    public void Dispose() => _root.Dispose();

    private string Config => _root.SubFolder("config");

    private LibraryGame AddGame(string folder, string title)
    {
        var path = _root.SubFolder(folder);
        File.WriteAllText(Path.Combine(path, folder + ".exe"), "x");
        return new LibraryGame(GameLibrary.IdFor(path), GameLibrary.NormalizeFolder(path), folder + ".exe", new LibraryEngine("yuris", "Yu-RIS", LibraryEngineSource.Engine), title, null, Start);
    }

    private LibraryViewModel Create(FakeGameLauncher launcher)
    {
        var file = new LibraryFile(Config);
        var playLog = new PlayLogFile(Config, _clock);
        var options = new PlayTrackingOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromDays(1), TimeSpan.FromDays(1));
        return new LibraryViewModel(
            file,
            new FakeFolderPicker(null),
            action => action(),
            new LibraryServices
            {
                Editor = new LibraryEditor(file, () => throw new NotSupportedException()),
                Clock = _clock,
                ReadTranslation = _ => null,
                Player = new GamePlayer(launcher, playLog, _clock, options),
                PlayLog = playLog,
            });
    }

    [Fact]
    public async Task Jogo_que_pede_localidade_japonesa_abre_pelo_Locale_Emulator_do_caminho_dos_Ajustes()
    {
        var game = AddGame("amaoto", "Amaoto no Hakoniwa");
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var emulator = _root.SubFolder("le");
        File.WriteAllText(Path.Combine(emulator, "LEProc.exe"), "x");
        var launcher = new FakeGameLauncher(_clock) { Script = [(TimeSpan.FromMinutes(5), GameProcessState.Exited)] };
        var library = Create(launcher);
        await library.LoadAsync();
        library.Select(library.Cards.Single());

        // Sem o caminho do Locale Emulator, o Jogar recusa e diz onde informar.
        library.Drawer!.RequiresJapaneseLocale = true;
        await library.Remembering;
        await library.PlayCommand.ExecuteAsync(library.Cards.Single());
        Assert.Contains("Locale Emulator", library.ErrorMessage);
        Assert.Null(launcher.Launched);

        var settings = new LocaleEmulatorSettingsViewModel(new LibraryFile(Config), new FakeFolderPicker(null)) { PathText = emulator };
        settings.SaveCommand.Execute(null);
        await library.PlayCommand.ExecuteAsync(library.Cards.Single());

        Assert.True(new LibraryFile(Config).Load().Games.Single().RequiresJapaneseLocale);
        Assert.True(launcher.Launched!.ViaLocaleEmulator);
        Assert.EndsWith("LEProc.exe", launcher.Launched.Executable);
        Assert.Equal([Path.Combine(game.Folder, "amaoto.exe")], launcher.Launched.Arguments.Select(a => a.Replace('/', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public async Task Se_gravar_a_localidade_japonesa_falha_o_interruptor_volta_ao_valor_gravado()
    {
        var game = AddGame("amaoto", "Amaoto no Hakoniwa");
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var library = Create(new FakeGameLauncher(_clock));
        await library.LoadAsync();
        library.Select(library.Cards.Single());
        File.WriteAllText(new LibraryFile(Config).Path, "{ não é json");

        library.Drawer!.RequiresJapaneseLocale = true;
        await library.Remembering;

        Assert.NotNull(library.ErrorMessage);
        Assert.False(library.Drawer!.RequiresJapaneseLocale);
    }

    [Fact]
    public async Task Aba_Jogar_do_projeto_mostra_o_tempo_as_sessoes_e_as_semanas_e_grava_o_interruptor_do_lancador()
    {
        var project = _root.SubFolder("projeto");
        var game = AddGame("amaoto", "Amaoto no Hakoniwa") with { ProjectPath = GameLibrary.NormalizeFolder(project) };
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var launcher = new FakeGameLauncher(_clock) { Script = [(TimeSpan.FromMinutes(25), GameProcessState.Running), (TimeSpan.FromMinutes(15), GameProcessState.Exited)] };
        var library = Create(launcher);
        await library.LoadAsync();
        var play = new GamePlayViewModel(library, _clock);
        play.Open(project);

        Assert.True(play.HasGame);
        Assert.Equal(("0 min", "ainda sem sessões"), (play.TotalText, play.AverageText));
        Assert.True(play.ShowsEmptyRows);

        await play.PlayCommand.ExecuteAsync(null);

        Assert.Equal(("40 min", "em 1 sessão · a média é de 40 min"), (play.TotalText, play.AverageText));
        Assert.Equal(["Direto"], play.Rows.Select(r => r.Via));
        Assert.Equal(PlayHistory.Weeks, play.Bars.Count);
        Assert.True(play.Bars[^1].IsCurrent);
        Assert.False(play.IsPlaying);
        play.ViewIndex = 0;
        Assert.Equal("Esta semana", play.Rows.Single().When);

        play.OpensLauncherOn = true;
        await library.Remembering;
        Assert.True(new LibraryFile(Config).Load().Find(game.Id).OpensLauncher);
        Assert.True(play.OpensLauncherOn);
    }

    [Fact]
    public async Task Interruptor_que_nao_gravou_volta_ao_valor_do_library_json_e_mostra_o_erro()
    {
        var project = _root.SubFolder("projeto");
        var game = AddGame("amaoto", "Amaoto no Hakoniwa") with { ProjectPath = GameLibrary.NormalizeFolder(project) };
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var library = Create(new FakeGameLauncher(_clock));
        await library.LoadAsync();
        var play = new GamePlayViewModel(library, _clock);
        play.Open(project);

        // O jogo sai do library.json por fora (outra janela, o vnf): gravar o interruptor falha.
        LibrarySamples.Save(Config, new GameLibrary([]));
        play.OpensLauncherOn = true;
        await library.Remembering;

        Assert.False(play.OpensLauncherOn);
        Assert.NotNull(library.ErrorMessage);
    }

    /// <summary>O relógio do teste: o tempo só anda quando o processo falso é consultado ou o teste manda.</summary>
    private sealed class PlayClock(DateTimeOffset wall) : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            _ticks += by.Ticks;
            wall += by;
        }

        public override DateTimeOffset GetUtcNow() => wall;

        public override long GetTimestamp() => _ticks;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FakeGameLauncher(PlayClock clock) : IGameLauncher
    {
        public (TimeSpan Advance, GameProcessState State)[] Script { get; set; } = [];

        public GameLaunch? Launched { get; private set; }

        public Exception? FailWith { get; set; }

        public Action? OnPoll { get; set; }

        public IGameProcess Start(GameLaunch launch)
        {
            Launched = launch;
            return FailWith is null ? new FakeProcess(clock, Script, OnPoll) : throw FailWith;
        }
    }

    private sealed class FakeProcess(PlayClock clock, (TimeSpan Advance, GameProcessState State)[] script, Action? onPoll) : IGameProcess
    {
        private int _polls;

        public GameProcessState Poll()
        {
            var (advance, state) = _polls < script.Length ? script[_polls] : (TimeSpan.Zero, GameProcessState.Exited);
            _polls++;
            clock.Advance(advance);
            onPoll?.Invoke();
            return state;
        }

        public void Dispose()
        {
        }
    }
}
