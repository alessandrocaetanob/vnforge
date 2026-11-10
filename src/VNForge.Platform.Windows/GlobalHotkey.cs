using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VNForge.Core.Library;

namespace VNForge.Platform.Windows;

/// <summary>
/// O <see cref="IGlobalHotkey"/> de verdade: <c>RegisterHotKey</c> numa thread própria com a fila de mensagens, que recebe o <c>WM_HOTKEY</c> mesmo com o jogo em foco.
/// A tecla é a F9, sem repetir enquanto segurada. Se outro programa já a registrou, <see cref="Register"/> devolve nulo.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class GlobalHotkey(uint virtualKey = GlobalHotkey.VirtualKeyF9) : IGlobalHotkey
{
    public const uint VirtualKeyF9 = 0x78;

    private const uint NoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const int HotkeyId = 1;

    public IDisposable? Register(Action pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        using var started = new ManualResetEventSlim();
        var threadId = 0u;
        var registered = false;
        var thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            // A fila da thread só existe depois da primeira consulta: sem ela o WM_QUIT do Dispose se perderia.
            PeekMessageW(out _, 0, 0, 0, 0);
            registered = RegisterHotKey(0, HotkeyId, NoRepeat, virtualKey);
            started.Set();
            if (!registered)
            {
                return;
            }

            try
            {
                while (GetMessageW(out var message, 0, 0, 0) > 0)
                {
                    if (message.Message == WmHotkey)
                    {
                        try
                        {
                            pressed();
                        }
                        catch (Exception e)
                        {
                            // Uma exceção solta numa thread de fundo derrubaria o app inteiro; quem registra trata os próprios erros.
                            System.Diagnostics.Debug.WriteLine(e);
                        }
                    }
                }
            }
            finally
            {
                UnregisterHotKey(0, HotkeyId);
            }
        })
        {
            IsBackground = true,
            Name = "VNForge global hotkey",
        };
        thread.Start();
        started.Wait();
        return registered ? new Registration(thread, threadId) : null;
    }

    internal sealed class Registration(Thread thread, uint threadId) : IDisposable
    {
        internal uint ThreadId => threadId;

        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                PostThreadMessageW(threadId, WmQuit, 0, 0);
                thread.Join();
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Window;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out Msg message, nint window, uint minFilter, uint maxFilter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint window, uint minFilter, uint maxFilter, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
