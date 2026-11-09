using Microsoft.Extensions.Time.Testing;

namespace VNForge.Vndb.Tests;

public class VndbRequestQueueTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    [Fact]
    public void Os_200_primeiros_pedidos_passam_na_hora_e_o_201_espera_a_janela()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time);

        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(TimeSpan.Zero, queue.Reserve());
        }

        Assert.Equal(Window, queue.Reserve());
    }

    [Fact]
    public void O_pedido_seguinte_passa_assim_que_o_mais_antigo_sai_da_janela()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time);
        for (var i = 0; i < 200; i++)
        {
            queue.Reserve();
        }

        time.Advance(Window - TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromMilliseconds(1), queue.Reserve());

        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.Zero, queue.Reserve());
    }

    [Fact]
    public void Em_nenhuma_janela_de_5_minutos_passam_mais_de_200_pedidos()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time);
        var sent = new List<DateTimeOffset>();

        // 1050 pedidos o mais rápido possível: espera o que a fila mandar e tenta de novo.
        while (sent.Count < 1050)
        {
            var wait = queue.Reserve();
            if (wait == TimeSpan.Zero)
            {
                sent.Add(time.GetUtcNow());
            }
            else
            {
                time.Advance(wait);
            }
        }

        for (var i = 0; i < sent.Count; i++)
        {
            var inWindow = sent.Count(t => t >= sent[i] && t < sent[i] + Window);
            Assert.True(inWindow <= 200, $"{inWindow} pedidos na janela que começa no pedido {i}.");
        }

        // 1050 pedidos a 200 por janela são 5 janelas cheias e mais 50: o último sai no quinto reinício.
        Assert.Equal(TimeSpan.FromMinutes(25), sent[^1] - sent[0]);
    }

    [Fact]
    public async Task WaitTurnAsync_segura_o_pedido_201_ate_o_relogio_andar()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time);
        var tasks = Enumerable.Range(0, 201).Select(_ => queue.WaitTurnAsync(CancellationToken.None)).ToArray();

        Assert.Equal(200, tasks.Count(t => t.IsCompleted));
        Assert.False(tasks[200].IsCompleted);

        time.Advance(Window);
        await tasks[200].WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(tasks[200].IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitTurnAsync_cancelado_nao_gasta_um_lugar_da_janela()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time, maxRequests: 1);
        await queue.WaitTurnAsync(CancellationToken.None);
        using var cts = new CancellationTokenSource();
        var waiting = queue.WaitTurnAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        time.Advance(Window);
        Assert.Equal(TimeSpan.Zero, queue.Reserve());
        Assert.Equal(Window, queue.Reserve());
    }

    [Fact]
    public void Penalizar_bloqueia_a_fila_pelo_tempo_dito_mesmo_com_a_janela_vazia()
    {
        var time = new FakeTimeProvider();
        var queue = new VndbRequestQueue(time);

        queue.BlockFor(TimeSpan.FromSeconds(90));

        Assert.Equal(TimeSpan.FromSeconds(90), queue.Reserve());
        time.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.Zero, queue.Reserve());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Limite_invalido_e_recusado(int max)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VndbRequestQueue(new FakeTimeProvider(), max));
    }
}
