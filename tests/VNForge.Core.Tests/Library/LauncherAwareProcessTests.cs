using VNForge.Core.Library;

namespace VNForge.Core.Tests.Library;

public sealed class LauncherAwareProcessTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    private readonly TestClock _clock = new(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
    private GameProcessState _launcher = GameProcessState.Running;
    private GameProcessState? _game;

    private LauncherAwareProcess Process() => new(new FakeLauncherProcess(this), () => _game, _clock, Grace, MaxWait);

    [Fact]
    public void Segue_o_processo_do_jogo_mesmo_depois_que_o_lancador_sai()
    {
        var process = Process();

        Assert.Equal(GameProcessState.Waiting, process.Poll());

        _game = GameProcessState.Running;
        Assert.Equal(GameProcessState.Running, process.Poll());

        _launcher = GameProcessState.Exited;
        Assert.Equal(GameProcessState.Running, process.Poll());

        _game = GameProcessState.Suspended;
        Assert.Equal(GameProcessState.Suspended, process.Poll());

        _game = null;
        Assert.Equal(GameProcessState.Exited, process.Poll());
    }

    [Fact]
    public void Lancador_que_sai_sem_o_jogo_aparecer_espera_a_tolerancia_e_encerra()
    {
        var process = Process();
        _launcher = GameProcessState.Exited;

        Assert.Equal(GameProcessState.Waiting, process.Poll());
        _clock.Advance(Grace - TimeSpan.FromSeconds(1));
        Assert.Equal(GameProcessState.Waiting, process.Poll());

        _game = GameProcessState.Running;
        Assert.Equal(GameProcessState.Running, process.Poll());

        _game = null;
        Assert.Equal(GameProcessState.Exited, process.Poll());
    }

    [Fact]
    public void Lancador_que_sai_e_o_jogo_nunca_abre_encerra_depois_da_tolerancia()
    {
        var process = Process();
        _launcher = GameProcessState.Exited;

        Assert.Equal(GameProcessState.Waiting, process.Poll());
        _clock.Advance(Grace);
        Assert.Equal(GameProcessState.Exited, process.Poll());
    }

    [Fact]
    public void Lancador_aberto_sem_o_jogo_aparecer_encerra_depois_do_limite_sem_ter_contado()
    {
        var process = Process();

        Assert.Equal(GameProcessState.Waiting, process.Poll());
        _clock.Advance(MaxWait - TimeSpan.FromSeconds(1));
        Assert.Equal(GameProcessState.Waiting, process.Poll());
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(GameProcessState.Exited, process.Poll());
    }

    [Fact]
    public void O_limite_de_espera_nao_corta_um_jogo_que_ja_apareceu()
    {
        var process = Process();
        _game = GameProcessState.Running;
        Assert.Equal(GameProcessState.Running, process.Poll());

        _clock.Advance(MaxWait * 2);

        Assert.Equal(GameProcessState.Running, process.Poll());
    }

    private sealed class FakeLauncherProcess(LauncherAwareProcessTests test) : IGameProcess
    {
        public GameProcessState Poll() => test._launcher;

        public void Dispose()
        {
        }
    }
}
