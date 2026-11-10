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
            editor: new LibraryEditor(file, () => throw new NotSupportedException()),
            clock: _clock,
            readTranslation: _ => null,
            player: new GamePlayer(launcher, playLog, _clock, options),
            playLog: playLog);
    }

    [Fact]
    public async Task Jogar_conta_o_tempo_pelo_processo_e_poe_o_ultimo_jogado_no_topo_de_Continuar_jogando()
    {
        var clannad = AddGame("clannad", "CLANNAD");
        var amaoto = AddGame("amaoto", "Amaoto no Hakoniwa");
        LibrarySamples.Save(Config, new GameLibrary([clannad, amaoto]));
        var launcher = new FakeGameLauncher(_clock);
        var library = Create(launcher);
        await library.LoadAsync();
        Assert.False(library.HasContinue);

        launcher.Script = [(TimeSpan.FromMinutes(25), GameProcessState.Running), (TimeSpan.FromMinutes(15), GameProcessState.Exited)];
        await library.PlayCommand.ExecuteAsync(library.Cards.Single(c => c.Title == "CLANNAD"));
        _clock.Advance(TimeSpan.FromHours(2));
        launcher.Script = [(TimeSpan.FromMinutes(10), GameProcessState.Exited)];
        await library.PlayCommand.ExecuteAsync(library.Cards.Single(c => c.Title == "Amaoto no Hakoniwa"));

        var played = new PlayLogFile(Config).Load();
        Assert.Equal(TimeSpan.FromMinutes(40), played.StatsOf(clannad.Id).Total);
        Assert.Equal(TimeSpan.FromMinutes(10), played.StatsOf(amaoto.Id).Total);
        Assert.Equal(["Amaoto no Hakoniwa", "CLANNAD"], library.Continue.Select(c => c.Title));
        Assert.EndsWith("· 40 min", library.Continue[1].Detail);
        Assert.Null(library.PlayingId);
        Assert.Null(library.ErrorMessage);

        library.Select(library.Cards.Single(c => c.Title == "CLANNAD"));
        Assert.StartsWith("40 min em 1 sessão · último jogado Hoje, ", library.Drawer!.PlayedText);
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
    public async Task Falha_do_lancador_ou_jogo_apagado_aparece_no_aviso_e_nao_derruba_o_comando()
    {
        var game = AddGame("amaoto", "Amaoto no Hakoniwa");
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var launcher = new FakeGameLauncher(_clock) { FailWith = new IOException("O disco sumiu.") };
        var library = Create(launcher);
        await library.LoadAsync();

        await library.PlayCommand.ExecuteAsync(library.Cards.Single());
        Assert.Equal("O disco sumiu.", library.ErrorMessage);
        Assert.Null(library.PlayingId);

        new LibraryFile(Config).Update(l => l.Remove(game.Id));
        await library.PlayCommand.ExecuteAsync(library.Cards.Single());
        Assert.NotEqual("O disco sumiu.", library.ErrorMessage);
        Assert.NotEmpty(library.ErrorMessage!);
    }

    [Fact]
    public async Task Continuar_jogando_segue_a_busca_da_grade_e_mostra_o_jogo_aberto_como_Jogando()
    {
        var clannad = AddGame("clannad", "CLANNAD");
        var amaoto = AddGame("amaoto", "Amaoto no Hakoniwa");
        LibrarySamples.Save(Config, new GameLibrary([clannad, amaoto]));
        var launcher = new FakeGameLauncher(_clock) { Script = [(TimeSpan.FromMinutes(5), GameProcessState.Exited)] };
        var library = Create(launcher);
        await library.LoadAsync();
        foreach (var card in library.Cards.ToList())
        {
            await library.PlayCommand.ExecuteAsync(card);
        }

        Assert.Equal(2, library.Continue.Count);
        library.SearchText = "CLANNAD";
        Assert.Equal(["CLANNAD"], library.Continue.Select(c => c.Title));

        var open = new List<bool>();
        launcher.OnPoll = () => open.Add(library.Continue.Single().Card.IsPlaying);
        await library.PlayCommand.ExecuteAsync(library.Cards.Single());
        Assert.Equal([true], open);
        Assert.False(library.Continue.Single().Card.IsPlaying);
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
    public void O_ultimo_jogado_e_dito_pela_data_do_PC()
    {
        var now = Start.AddDays(3);
        var last = new PlayStats(TimeSpan.FromMinutes(95), Start, 2);

        Assert.StartsWith("Hoje, ", PlayTime.LastPlayed(Start, Start.AddMinutes(30)));
        Assert.StartsWith("Ontem, ", PlayTime.LastPlayed(Start, Start.AddHours(24)));
        Assert.Equal("Há 3 dias", PlayTime.LastPlayed(Start, now));
        Assert.Equal("1 h 35 min em 2 sessões · último jogado Há 3 dias", PlayTime.Summary(last, now));
        Assert.Equal("Ainda não jogado", PlayTime.Summary(PlayStats.None, now));
    }

    /// <summary>O relógio do teste: o tempo só anda quando o processo falso é consultado ou o teste manda.</summary>
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
    public async Task Aba_Jogar_lista_as_quatro_sessoes_mais_recentes_como_o_quadro_e_o_tempo_total_conta_todas()
    {
        var project = _root.SubFolder("projeto");
        var game = AddGame("amaoto", "Amaoto no Hakoniwa") with { ProjectPath = GameLibrary.NormalizeFolder(project) };
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var playLog = new PlayLogFile(Config, _clock);
        for (var daysAgo = 1; daysAgo <= 6; daysAgo++)
        {
            var started = Start.AddDays(-daysAgo);
            playLog.Record(game.Id, new PlaySession(PlaySession.NewId(), started, started.AddMinutes(30), TimeSpan.FromMinutes(30)));
        }

        var library = Create(new FakeGameLauncher(_clock));
        await library.LoadAsync();
        var play = new GamePlayViewModel(library, _clock);
        play.Open(project);

        Assert.Equal(4, play.Rows.Count);
        Assert.Equal("Ontem", play.Rows[0].When.Split(',')[0]);
        Assert.Equal("3 h 00 min", play.TotalText);
        Assert.StartsWith("em 6 sessões", play.AverageText);
    }

    [Fact]
    public async Task Interruptor_do_lancador_mexido_logo_antes_do_Jogar_vale_na_abertura()
    {
        var project = _root.SubFolder("projeto");
        var game = AddGame("amaoto", "Amaoto no Hakoniwa") with { ProjectPath = GameLibrary.NormalizeFolder(project) };
        LibrarySamples.Save(Config, new GameLibrary([game]));
        var launcher = new FakeGameLauncher(_clock) { Script = [(TimeSpan.FromMinutes(5), GameProcessState.Exited)] };
        var library = Create(launcher);
        await library.LoadAsync();
        var play = new GamePlayViewModel(library, _clock);
        play.Open(project);

        play.OpensLauncherOn = true;
        await play.PlayCommand.ExecuteAsync(null);

        Assert.True(launcher.Launched!.OpensLauncher);
    }

    [Fact]
    public async Task Abrir_a_aba_com_a_partida_em_curso_nao_reinicia_o_relogio_e_projeto_sem_jogo_nao_joga()
    {
        var project = _root.SubFolder("projeto");
        var game = AddGame("amaoto", "Amaoto no Hakoniwa") with { ProjectPath = GameLibrary.NormalizeFolder(project) };
        LibrarySamples.Save(Config, new GameLibrary([game]));
        GamePlayViewModel? play = null;
        LibraryViewModel? library = null;
        var shown = new List<string>();
        var launcher = new FakeGameLauncher(_clock) { Script = [(TimeSpan.FromMinutes(25), GameProcessState.Running), (TimeSpan.FromMinutes(5), GameProcessState.Exited)] };
        launcher.OnPoll = () =>
        {
            // A aba só abre aos 25 minutos de partida.
            if (play is null)
            {
                play = new GamePlayViewModel(library!, _clock);
                play.Open(project);
                shown.Add(play.ElapsedText);
            }
        };
        library = Create(launcher);
        await library.LoadAsync();

        await library.PlayCommand.ExecuteAsync(library.Cards.Single());

        Assert.Equal(["00:25:00"], shown);
        var semJogo = new GamePlayViewModel(library, _clock);
        semJogo.Open(_root.SubFolder("outro-projeto"));
        Assert.False(semJogo.HasGame);
        Assert.False(semJogo.PlayCommand.CanExecute(null));
    }

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
