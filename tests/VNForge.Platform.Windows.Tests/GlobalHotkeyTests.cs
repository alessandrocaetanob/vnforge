using System.Runtime.Versioning;

namespace VNForge.Platform.Windows.Tests;

/// <summary>Registram uma tecla de verdade, por isso só rodam no Windows. Usam a F24, que nenhum teclado tem e nenhum programa registra, para não tomar a F9 de quem roda os testes.</summary>
[SupportedOSPlatform("windows")]
public sealed class GlobalHotkeyTests
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
}
