using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VNForge.Platform.Windows;

/// <summary>
/// As chamadas de <c>user32</c>, <c>gdi32</c> e <c>kernel32</c> que a captura de janela usa. Só o necessário, sem tratar erro: quem chama decide. Todas com assinatura
/// segura (<c>nint</c>, <c>ref</c>, <c>out</c> e <c>nint</c> para buffers alocados com <c>Marshal</c>), sem ponteiros.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class NativeWindows
{
    /// <summary>BITMAPINFOHEADER, que descreve a imagem de 32 bits que o <c>CreateDIBSection</c> cria.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>PW_CLIENTONLY | PW_RENDERFULLCONTENT: só a área do jogo, pelo que o DWM compõe (funciona com janela de DirectX em modo janela).</summary>
    public const uint PrintClientRenderFull = 0x1 | 0x2;

    /// <summary>SRCCOPY | CAPTUREBLT: copia da tela também as janelas em camadas.</summary>
    public const uint ScreenCopy = 0x00CC0020 | 0x40000000;

    public const uint ProcessQueryLimitedInformation = 0x1000;

    private const int PathBufferLength = 1024;

    /// <summary>GW_HWNDNEXT: a janela que vem abaixo na ordem de empilhamento.</summary>
    public const uint NextInOrder = 2;

    /// <summary>GW_OWNER (4): a janela dona, ou zero. Janela com dona é caixa de diálogo ou dica, não a janela principal.</summary>
    public const uint OwnerWindow = 4;

    /// <summary>GA_ROOT (2): a janela de nível mais alto de uma janela filha.</summary>
    public const uint RootAncestor = 2;

    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    public static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>As janelas de nível mais alto, da que está na frente para a de trás.</summary>
    public static List<nint> TopLevelWindows()
    {
        var found = new List<nint>();
        for (var window = GetTopWindow(0); window != 0; window = GetWindow(window, NextInOrder))
        {
            found.Add(window);
        }

        return found;
    }

    public static string TitleOf(nint window)
    {
        var length = GetWindowTextLengthW(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var capacity = length + 1;
        var buffer = Marshal.AllocHGlobal(capacity * sizeof(char));
        try
        {
            var read = GetWindowTextW(window, buffer, capacity);
            return read > 0 ? Marshal.PtrToStringUni(buffer, read) : string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>O caminho do programa dono da janela; nulo quando o sistema não deixa ler (processo de outro usuário ou com mais privilégio).</summary>
    public static string? ProgramOf(nint window)
    {
        if (GetWindowThreadProcessId(window, out var processId) == 0)
        {
            return null;
        }

        var process = OpenProcess(ProcessQueryLimitedInformation, 0, processId);
        if (process == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(PathBufferLength * sizeof(char));
        try
        {
            var size = (uint)PathBufferLength;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size) != 0 ? Marshal.PtrToStringUni(buffer, (int)size) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            CloseHandle(process);
        }
    }

    [LibraryImport("user32.dll")]
    public static partial nint GetTopWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetAncestor(nint window, uint flags);

    /// <summary>A janela visível mais à frente naquele ponto da tela, ou zero se o ponto está fora de qualquer janela.</summary>
    [LibraryImport("user32.dll")]
    public static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint window, out Rect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint window, ref Point point);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    private static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static partial int GetWindowTextW(nint window, nint text, int max);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PrintWindow(nint window, nint deviceContext, uint flags);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint window);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint window, nint deviceContext);

    [LibraryImport("user32.dll")]
    public static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint deviceContext);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint deviceContext);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint deviceContext, ref BitmapInfoHeader info, uint usage, out nint bits, nint section, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint deviceContext, nint item);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint item);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, int inherit, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int QueryFullProcessImageNameW(nint process, uint flags, nint name, ref uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
