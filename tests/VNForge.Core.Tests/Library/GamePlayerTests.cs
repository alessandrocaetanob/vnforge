using VNForge.Core.Library;
using VNForge.Core.Tests.Projects;

namespace VNForge.Core.Tests.Library;

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
    public async Task Conta_do_abrir_ao_fechar_e_grava_a_sessao()
    {
        var launcher = new FakeLauncher(_clock, (Minute * 30, GameProcessState.Running), (Minute * 30, GameProcessState.Running), (Minute * 15, GameProcessState.Exited));

        var session = await Player(launcher).PlayAsync("g1", Launch, Ct);

        Assert.Equal(TimeSpan.FromMinutes(75), session.Duration);
        Assert.Equal(Start, session.StartedAt);
        Assert.Equal(Start.AddMinutes(75), session.EndedAt);
        Assert.False(session.Interrupted);
        Assert.False(session.Open);
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
        var launcher = new FakeLauncher(_clock, (Minute * 20, GameProcessState.Running), (Minute * 10, GameProcessState.Running)) { OnPoll = index => { if (index == 1) { source.Cancel(); } } };

        var session = await Player(launcher).PlayAsync("g1", Launch, source.Token);

        Assert.True(session.Interrupted);
        Assert.False(session.Open);
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

    [Fact]
    public async Task Relogio_de_parede_que_volta_no_meio_da_partida_nao_muda_o_tempo_nem_gera_sessao_negativa()
    {
        var launcher = new FakeLauncher(_clock, (Minute * 30, GameProcessState.Running), (Minute * 30, GameProcessState.Exited))
        {
            OnPoll = index => { if (index == 0) { _clock.SetWall(Start.AddHours(-3)); } },
        };

        var session = await Player(launcher).PlayAsync("g1", Launch, Ct);

        Assert.Equal(Start, session.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(60), session.Duration);
        Assert.Equal(Start.AddMinutes(60), session.EndedAt);
        Assert.True(session.EndedAt >= session.StartedAt);
        Assert.Equal([session], Saved().SessionsOf("g1"));
    }

    [Fact]
    public async Task Salto_de_horas_do_PC_que_dormiu_conta_no_maximo_um_passo()
    {
        var launcher = new FakeLauncher(_clock, (TimeSpan.FromSeconds(5), GameProcessState.Running), (TimeSpan.FromHours(8), GameProcessState.Running), (TimeSpan.FromSeconds(5), GameProcessState.Exited));

        var session = await Player(launcher, Options(maxStep: TimeSpan.FromSeconds(10))).PlayAsync("g1", Launch, Ct);

        Assert.Equal(TimeSpan.FromSeconds(5 + 10 + 5), session.Duration);
        Assert.True(session.Duration < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Sessao_fica_aberta_com_o_ultimo_instante_visto_e_fecha_no_fim()
    {
        PlayLog? midway = null;
        var launcher = new FakeLauncher(_clock, (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Exited))
        {
            // A batida de 10 min cai depois da terceira consulta (12 min); a quarta já a encontra no arquivo.
            OnPoll = index => { if (index == 3) { midway = Saved(); } },
        };

        var session = await Player(launcher, Options(heartbeat: TimeSpan.FromMinutes(10))).PlayAsync("g1", Launch, Ct);

        var open = Assert.Single(midway!.SessionsOf("g1"));
        Assert.True(open.Open);
        Assert.Equal(TimeSpan.FromMinutes(12), open.Duration);
        Assert.Equal(Start.AddMinutes(12), open.EndedAt);
        var closed = Assert.Single(Saved().SessionsOf("g1"));
        Assert.Equal(open.Id, closed.Id);
        Assert.False(closed.Open);
        Assert.Equal(TimeSpan.FromMinutes(20), closed.Duration);
        Assert.Equal(session, closed);
    }

    [Fact]
    public async Task App_que_fecha_no_meio_deixa_a_sessao_interrompida_com_o_que_foi_contado()
    {
        var crash = new FakeLauncher(_clock, (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Running)) { OnPoll = index => { if (index == 2) { throw new InvalidOperationException("o app fechou"); } } };
        var options = Options(heartbeat: TimeSpan.FromMinutes(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Player(crash, options).PlayAsync("g1", Launch, Ct));

        // Ainda dentro do prazo: a sessão aberta é de quem está jogando agora, e fica como está.
        var lost = Assert.Single(Saved().SessionsOf("g1"));
        Assert.True(lost.Open);
        Assert.Equal(TimeSpan.FromMinutes(8), lost.Duration);
        _clock.Advance(Minute);
        await Player(new FakeLauncher(_clock, (Minute, GameProcessState.Exited)), options).PlayAsync("g2", Launch, Ct);
        Assert.True(Assert.Single(Saved().SessionsOf("g1")).Open);

        // Passado o prazo, a próxima partida a fecha como interrompida; o tempo contado continua no total.
        _clock.Advance(TimeSpan.FromHours(1));
        await Player(new FakeLauncher(_clock, (Minute, GameProcessState.Exited)), options).PlayAsync("g2", Launch, Ct);

        var recovered = Assert.Single(Saved().SessionsOf("g1"));
        Assert.False(recovered.Open);
        Assert.True(recovered.Interrupted);
        Assert.Equal(TimeSpan.FromMinutes(8), Saved().StatsOf("g1").Total);
        Assert.Equal(Start.AddMinutes(8), Saved().StatsOf("g1").LastPlayedAt);
    }

    private FileStream HoldLock() =>
        new(new PlayLogFile(_dir.Path).Path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private GamePlayer PlayerWithShortLock(FakeLauncher launcher, List<string> warnings, TimeSpan heartbeat) =>
        new(launcher, new PlayLogFile(_dir.Path, lockTimeout: TimeSpan.FromMilliseconds(50)), _clock, Options(heartbeat: heartbeat), warnings.Add);

    [Fact]
    public async Task Trava_presa_o_tempo_todo_nao_derruba_a_partida_e_avisa_o_tempo_jogado()
    {
        var warnings = new List<string>();
        var launcher = new FakeLauncher(_clock, (Minute * 30, GameProcessState.Running), (Minute * 30, GameProcessState.Exited));
        PlaySession session;
        using (HoldLock())
        {
            session = await PlayerWithShortLock(launcher, warnings, TimeSpan.FromMinutes(10)).PlayAsync("g1", Launch, Ct);
        }

        Assert.Equal(TimeSpan.FromMinutes(60), session.Duration);
        Assert.False(session.Open);
        Assert.Equal(2, warnings.Count);
        Assert.Contains("1 h 00 min", warnings[1]);
        Assert.False(File.Exists(new PlayLogFile(_dir.Path).Path));
    }

    [Fact]
    public async Task Trava_que_solta_no_meio_da_partida_grava_na_proxima_batida_e_no_fim()
    {
        var warnings = new List<string>();
        var hold = HoldLock();
        var launcher = new FakeLauncher(_clock, (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Running), (Minute * 4, GameProcessState.Exited))
        {
            OnPoll = index => { if (index == 0) { hold.Dispose(); } },
        };

        var session = await PlayerWithShortLock(launcher, warnings, TimeSpan.FromMinutes(5)).PlayAsync("g1", Launch, Ct);

        Assert.Single(warnings);
        Assert.Contains("início da sessão", warnings[0]);
        Assert.Equal([session], Saved().SessionsOf("g1"));
        Assert.Equal(TimeSpan.FromMinutes(16), session.Duration);
    }

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
