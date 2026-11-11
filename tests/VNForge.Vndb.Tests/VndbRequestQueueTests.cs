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
}
