using Microsoft.Extensions.Time.Testing;
using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

public sealed class GamePlayerTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 9, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private readonly TempFolder _dir = new();
    private readonly FakeTimeProvider _clock = new(Start);

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GameLaunch Launch => new(@"C:\Jogos\Vn\Vn.exe", [], @"C:\Jogos\Vn", @"C:\Jogos\Vn\Vn.exe", ViaLocaleEmulator: false);

    // O intervalo zero faz o Task.Delay voltar na hora; quem anda o relógio é o processo falso, a cada consulta.
    private GamePlayer Player(FakeLauncher launcher, PlayTrackingOptions? options = null) =>
        new(launcher, new PlayLogFile(_dir.Path), _clock, options ?? new PlayTrackingOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

    private PlayLog Saved() => new PlayLogFile(_dir.Path).Load();

    [Fact]
    public async Task Conta_do_abrir_ao_fechar_e_grava_a_sessao()
    {
        var launcher = new FakeLauncher(_clock, (Minute * 30, GameProcessState.Running), (Minute * 30, GameProcessState.Running), (Minute * 15, GameProcessState.Exited));

        var session = await Player(launcher).PlayAsync("g1", Launch, Ct);

        Assert.Equal(TimeSpan.FromMinutes(75), session.Duration);
        Assert.Equal(Start, session.StartedAt);
        Assert.Equal(Start.AddMinutes(75), session.EndedAt);
        Assert.False(session.Interrupted);
        Assert.Equal([session], Saved().SessionsOf("g1"));
        Assert.Equal(Launch, launcher.Launched);
        Assert.True(launcher.Process!.Disposed);
    }

    [Fact]
    public async Task Lancador_que_abre_o_jogo_e_sai_nao_encerra_a_contagem()
    {
        // O processo aberto (o lançador) sai, mas o job ainda tem o jogo: o estado segue Running até o último filho sair.
        var launcher = new FakeLauncher(_clock, (Minute, GameProcessState.Running), (Minute * 40, GameProcessState.Running), (Minute * 40, GameProcessState.Running), (Minute, GameProcessState.Exited));

        var session = await Player(launcher).PlayAsync("g1", Launch, Ct);

        Assert.Equal(TimeSpan.FromMinutes(82), session.Duration);
    }

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
    public async Task Fechar_com_o_jogo_suspenso_por_mais_que_o_limite_nao_conta_o_trecho()
    {
        var launcher = new FakeLauncher(_clock, (Minute * 20, GameProcessState.Running), (Minute * 8, GameProcessState.Suspended), (Minute, GameProcessState.Exited));

        var session = await Player(launcher).PlayAsync("g1", Launch, Ct);

        Assert.Equal(TimeSpan.FromMinutes(21), session.Duration);
    }

    [Fact]
    public async Task Cancelar_para_a_contagem_e_grava_a_sessao_como_interrompida()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var launcher = new FakeLauncher(_clock, (Minute * 20, GameProcessState.Running), (Minute * 10, GameProcessState.Running)) { OnPoll = (index, _) => { if (index == 1) { source.Cancel(); } } };

        var session = await Player(launcher).PlayAsync("g1", Launch, source.Token);

        Assert.True(session.Interrupted);
        Assert.Equal(TimeSpan.FromMinutes(30), session.Duration);
        Assert.Equal([session], Saved().SessionsOf("g1"));
        Assert.True(launcher.Process!.Disposed);
    }

    [Fact]
    public async Task Processo_que_nao_abre_nao_grava_nada()
    {
        var launcher = new FakeLauncher(_clock) { FailWith = new LibraryException("não abriu") };

        var error = await Assert.ThrowsAsync<LibraryException>(() => Player(launcher).PlayAsync("g1", Launch, Ct));

        Assert.Equal("não abriu", error.Message);
        Assert.False(File.Exists(new PlayLogFile(_dir.Path).Path));
    }

    [Fact]
    public async Task Arquivo_de_tempo_ilegivel_recusa_antes_de_abrir_o_jogo()
    {
        await File.WriteAllTextAsync(new PlayLogFile(_dir.Path).Path, "{ não é json", Ct);
        var launcher = new FakeLauncher(_clock, (Minute, GameProcessState.Exited));

        await Assert.ThrowsAsync<LibraryException>(() => Player(launcher).PlayAsync("g1", Launch, Ct));

        Assert.Null(launcher.Launched);
    }

    [Fact]
    public async Task Sessoes_somam_e_o_ultimo_jogado_vai_para_o_topo()
    {
        await Player(new FakeLauncher(_clock, (Minute * 30, GameProcessState.Exited))).PlayAsync("g1", Launch, Ct);
        _clock.Advance(TimeSpan.FromHours(2));
        await Player(new FakeLauncher(_clock, (Minute * 45, GameProcessState.Exited))).PlayAsync("g1", Launch, Ct);
        _clock.Advance(TimeSpan.FromHours(1));
        await Player(new FakeLauncher(_clock, (Minute * 5, GameProcessState.Exited))).PlayAsync("g2", Launch, Ct);

        var log = Saved();
        var stats = log.StatsOf("g1");

        Assert.Equal(TimeSpan.FromMinutes(75), stats.Total);
        Assert.Equal(2, stats.Sessions);
        Assert.Equal(Start.AddMinutes(30 + 120 + 45), stats.LastPlayedAt);
        Assert.Equal(PlayStats.None, log.StatsOf("g3"));
    }

    private sealed class FakeLauncher(FakeTimeProvider clock, params (TimeSpan Advance, GameProcessState State)[] script) : IGameLauncher
    {
        public GameLaunch? Launched { get; private set; }

        public Exception? FailWith { get; init; }

        public Action<int, FakeProcess>? OnPoll { get; init; }

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

    private sealed class FakeProcess(FakeTimeProvider clock, (TimeSpan Advance, GameProcessState State)[] script, Action<int, FakeProcess>? onPoll) : IGameProcess
    {
        private int _polls;

        public bool Disposed { get; private set; }

        public GameProcessState Poll()
        {
            var index = _polls++;
            var (advance, state) = index < script.Length ? script[index] : (TimeSpan.Zero, GameProcessState.Exited);
            clock.Advance(advance);
            onPoll?.Invoke(index, this);
            return state;
        }

        public void Dispose() => Disposed = true;
    }
}
