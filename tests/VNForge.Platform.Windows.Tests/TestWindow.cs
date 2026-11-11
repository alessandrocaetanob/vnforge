using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace VNForge.Platform.Windows.Tests;

/// <summary>
/// Uma janela de verdade criada pelo próprio teste: popup de 200x120 pixels com fundo liso, sem nada que dependa de outro programa. O procedimento da janela é o
/// <c>DefWindowProc</c> do sistema, que pinta o pincel de fundo da classe, então não há código gerenciado a ser chamado de volta pelo Windows. Use e descarte
/// na mesma thread: a janela pertence à thread que a criou.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class TestWindow : IDisposable
{
    public const int Width = 200;
    public const int Height = 120;

    private const uint PopupVisible = 0x80000000 | 0x10000000;
    private const uint ToolWindow = 0x80;
    private const int ShowNoActivate = 4;
    private const int ShowMinimize = 6;
    private const uint Remove = 1;

    private static readonly nint PerMonitorAwareV2 = -4;

    private readonly string _className = "VNForgeTestWindow" + Guid.NewGuid().ToString("N");
    private readonly nint _classNameBuffer;
    private readonly nint _previousDpi;
    private readonly nint _brush;

    public TestWindow(byte red, byte green, byte blue)
    {
        // A janela nasce ciente de DPI, para o tamanho de cliente ser o pedido em pixels e não ampliado pela escala da tela.
        _previousDpi = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        Title = "VNForge captura de teste " + Guid.NewGuid().ToString("N");
        _brush = CreateSolidBrush((uint)(red | (green << 8) | (blue << 16)));
        var instance = GetModuleHandleW(null);
        _classNameBuffer = Marshal.StringToHGlobalUni(_className);
        var defaultProcedure = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), "DefWindowProcW");
        var window = new WindowClass
        {
            Size = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure = defaultProcedure,
            Instance = instance,
            Background = _brush,
            ClassName = _classNameBuffer,
        };
        if (RegisterClassExW(ref window) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx falhou ({Marshal.GetLastWin32Error()}).");
        }

        Handle = CreateWindowExW(ToolWindow, _className, Title, PopupVisible, 120, 120, Width, Height, 0, 0, instance, 0);
        if (Handle == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx falhou ({Marshal.GetLastWin32Error()}).");
        }

        ShowWindow(Handle, ShowNoActivate);
        UpdateWindow(Handle);
        Pump();
    }

    public nint Handle { get; }

    public string Title { get; }

    public void Minimize()
    {
        ShowWindow(Handle, ShowMinimize);
        Pump();
    }

    /// <summary>Atende as mensagens da janela por um instante, para o DWM ter a imagem composta antes da captura.</summary>
    public static void Pump()
    {
        var until = DateTime.UtcNow.AddMilliseconds(300);
        while (DateTime.UtcNow < until)
        {
            while (PeekMessageW(out var message, 0, 0, 0, Remove))
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }

            Thread.Sleep(10);
        }
    }

    public void Dispose()
    {
        DestroyWindow(Handle);
        UnregisterClassW(_className, GetModuleHandleW(null));
        DeleteObject(_brush);
        Marshal.FreeHGlobal(_classNameBuffer);
        if (_previousDpi != 0)
        {
            SetThreadDpiAwarenessContext(_previousDpi);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public nint Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public nint MenuName;
        public nint ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(ref WindowClass windowClass);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterClassW(string className, nint instance);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Message message, nint window, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref Message message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(ref Message message);

    [LibraryImport("user32.dll")]
    private static partial nint SetThreadDpiAwarenessContext(nint context);

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint item);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
