using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

[Trait("Risco", "R6")]
public sealed class GamePlayerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Never = TimeSpan.FromDays(100);

    private readonly TempFolder _dir = new();
    private readonly TestClock _clock = new(Start);

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GameLaunch Launch => new(@"C:\Jogos\Vn\Vn.exe", [], @"C:\Jogos\Vn", @"C:\Jogos\Vn\Vn.exe", ViaLocaleEmulator: false);

    // O intervalo zero faz o Task.Delay voltar na hora; quem anda o relógio é o processo falso, a cada consulta.
    private static PlayTrackingOptions Options(TimeSpan? heartbeat = null, TimeSpan? maxStep = null) =>
        new(TimeSpan.Zero, TimeSpan.FromMinutes(5), heartbeat ?? Never, maxStep ?? Never);

    private GamePlayer Player(FakeLauncher launcher, PlayTrackingOptions? options = null) =>
        new(launcher, new PlayLogFile(_dir.Path), _clock, options ?? Options());

    private PlayLog Saved() => new PlayLogFile(_dir.Path).Load();

    [Fact]
    public async Task Suspenso_por_ate_o_limite_conta_e_por_mais_que_o_limite_nao()
    {
        var launcher = new FakeLauncher(
            _clock,
            (Minute * 10, GameProcessState.Running),
            (Minute * 5, GameProcessState.Suspended),
            (Minute * 10, GameProcessState.Running),
            (Minute * 6, GameProcessState.Suspended),
            (Minute * 10, GameProcessState.Running),
            (Minute * 10, GameProcessState.Exited));

        var session = await Player(launcher).PlayAsync("g1", Launch, Ct);

        // Os 5 min suspensos entram (até o limite, inclusive); os 6 min não.
        Assert.Equal(TimeSpan.FromMinutes(10 + 5 + 10 + 10 + 10), session.Duration);
        Assert.Equal(Start.AddMinutes(10 + 5 + 10 + 6 + 10 + 10), session.EndedAt);
    }

    [Fact]
    public async Task Arquivo_de_tempo_ilegivel_recusa_antes_de_abrir_o_jogo()
    {
        await File.WriteAllTextAsync(new PlayLogFile(_dir.Path).Path, "{ não é json", Ct);
        var launcher = new FakeLauncher(_clock, (Minute, GameProcessState.Exited));

        await Assert.ThrowsAsync<LibraryException>(() => Player(launcher).PlayAsync("g1", Launch, Ct));

        Assert.Null(launcher.Launched);
    }

    private FileStream HoldLock() =>
        new(new PlayLogFile(_dir.Path).Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private GamePlayer PlayerWithShortLock(FakeLauncher launcher, List<string> warnings, TimeSpan heartbeat) =>
        new(launcher, new PlayLogFile(_dir.Path, lockTimeout: TimeSpan.FromMilliseconds(50)), _clock, Options(heartbeat: heartbeat), warnings.Add);

    private sealed class FakeLauncher(TestClock clock, params (TimeSpan Advance, GameProcessState State)[] script) : IGameLauncher
    {
        public GameLaunch? Launched { get; private set; }

        public Exception? FailWith { get; init; }

        public Action<int>? OnPoll { get; init; }

        public FakeProcess? Process { get; private set; }

        public IGameProcess Start(GameLaunch launch)
        {
            if (FailWith is not null)
            {
                throw FailWith;
            }

            Launched = launch;
            return Process = new FakeProcess(clock, script, OnPoll);
        }
    }

    private sealed class FakeProcess(TestClock clock, (TimeSpan Advance, GameProcessState State)[] script, Action<int>? onPoll) : IGameProcess
    {
        private int _polls;

        public bool Disposed { get; private set; }

        public GameProcessState Poll()
        {
            var index = _polls++;
            var (advance, state) = index < script.Length ? script[index] : (TimeSpan.Zero, GameProcessState.Exited);
            clock.Advance(advance);
            onPoll?.Invoke(index);
            return state;
        }

        public void Dispose() => Disposed = true;
    }
}
