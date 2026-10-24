using System.Runtime.InteropServices;

namespace VNForge.App.Services;

/// <summary>
/// Se o app pode animar. Segue o Windows ("Mostrar animações", em Acessibilidade): com a opção desligada, toda animação do app vira troca
/// imediata. Os testes fixam o valor com <see cref="Force"/>.
/// </summary>
public static partial class MotionPreferences
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    private static bool? forced;

    /// <summary>Disparado quando <see cref="Force"/> ou <see cref="Refresh"/> muda o valor; a janela principal troca a classe <c>motion</c>.</summary>
    public static event EventHandler? Changed;

    public static bool AnimationsEnabled => forced ?? ReadSystem();

    /// <summary>Fixa o valor (testes e depuração). Descartar o retorno volta ao valor de antes.</summary>
    public static IDisposable Force(bool enabled)
    {
        var before = forced;
        forced = enabled;
        Changed?.Invoke(null, EventArgs.Empty);
        return new Release(before);
    }

    /// <summary>Avisa que o Windows pode ter mudado a configuração; quem escuta <see cref="Changed"/> relê <see cref="AnimationsEnabled"/>.</summary>
    public static void Refresh() => Changed?.Invoke(null, EventArgs.Empty);

    private static void Reset(bool? value)
    {
        forced = value;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static bool ReadSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        try
        {
            return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out var enabled, 0) || enabled;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint winIni);

    private sealed class Release(bool? before) : IDisposable
    {
        public void Dispose() => Reset(before);
    }
}
