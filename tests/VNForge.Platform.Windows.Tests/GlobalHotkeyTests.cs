using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VNForge.Platform.Windows.Tests;

/// <summary>Registram uma tecla de verdade, por isso só rodam no Windows. Usam a F24, que nenhum teclado tem e nenhum programa registra, para não tomar a F9 de quem roda os testes.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class GlobalHotkeyTests
{
    private const uint F24 = 0x87;

    [Fact]
    public void Registra_a_tecla_uma_vez_e_a_libera_ao_descartar()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O atalho global só existe no Windows.");
        var hotkey = new GlobalHotkey(F24);

        using (var first = hotkey.Register(() => { }))
        {
            Assert.NotNull(first);
            Assert.Null(hotkey.Register(() => { }));
        }

        using var again = hotkey.Register(() => { });
        Assert.NotNull(again);
    }

    [Fact]
    public void Excecao_no_aviso_do_atalho_nao_derruba_a_thread_e_o_proximo_aperto_chega()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "O atalho global só existe no Windows.");
        using var second = new ManualResetEventSlim();
        var calls = 0;
        using var registration = new GlobalHotkey(F24).Register(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("falha no aviso");
            }

            second.Set();
        });
        var thread = ((GlobalHotkey.Registration)registration!).ThreadId;

        Assert.True(PostThreadMessage(thread, WmHotkey, 1, 0));
        Assert.True(PostThreadMessage(thread, WmHotkey, 1, 0));

        Assert.True(second.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    private const uint WmHotkey = 0x0312;

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
}
