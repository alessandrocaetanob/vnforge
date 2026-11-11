using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using VNForge.Core.Library;

namespace VNForge.Platform.Windows;

/// <summary>O <see cref="IWindowSystem"/> de verdade: <c>user32</c> e <c>gdi32</c> por <see cref="NativeWindows"/>. Só tem encanamento; as decisões ficam em <see cref="WindowCapturer"/>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class NativeWindowSystem : IWindowSystem
{
    public IReadOnlyList<nint> TopLevelWindows() => NativeWindows.TopLevelWindows();

    public bool Exists(nint window) => NativeWindows.IsWindow(window);

    public bool IsVisible(nint window) => NativeWindows.IsWindowVisible(window);

    public bool IsMinimized(nint window) => NativeWindows.IsIconic(window);

    public bool HasOwner(nint window) => NativeWindows.GetWindow(window, NativeWindows.OwnerWindow) != 0;

    public string TitleOf(nint window) => NativeWindows.TitleOf(window);

    public string? ProgramOf(nint window) => NativeWindows.ProgramOf(window);

    public bool TryGetClientSize(nint window, out int width, out int height)
    {
        var ok = NativeWindows.GetClientRect(window, out var rect);
        (width, height) = (rect.Right, rect.Bottom);
        return ok;
    }

    public nint Foreground() => NativeWindows.GetForegroundWindow();

    public nint RootOf(nint window) => NativeWindows.GetAncestor(window, NativeWindows.RootAncestor);

    public nint WindowAt(int screenX, int screenY) => NativeWindows.WindowFromPoint(new NativeWindows.Point { X = screenX, Y = screenY });

    public bool TryClientToScreen(nint window, int x, int y, out int screenX, out int screenY)
    {
        var point = new NativeWindows.Point { X = x, Y = y };
        var ok = NativeWindows.ClientToScreen(window, ref point);
        (screenX, screenY) = (point.X, point.Y);
        return ok;
    }

    public PrintedWindow Print(nint window, int width, int height)
    {
        using var canvas = new Canvas(width, height);
        var succeeded = NativeWindows.PrintWindow(window, canvas.Device, NativeWindows.PrintClientRenderFull);
        return new PrintedWindow(succeeded, canvas.Read());
    }

    public byte[]? CopyFromScreen(nint window, int width, int height)
    {
        if (!TryClientToScreen(window, 0, 0, out var originX, out var originY))
        {
            return null;
        }

        using var canvas = new Canvas(width, height);
        return NativeWindows.BitBlt(canvas.Device, 0, 0, width, height, canvas.Screen, originX, originY, NativeWindows.ScreenCopy) ? canvas.Read() : null;
    }

    public IDisposable EnterDpiAwareness() => new DpiScope(NativeWindows.SetThreadDpiAwarenessContext(NativeWindows.PerMonitorAwareV2));

    private sealed class DpiScope(nint previous) : IDisposable
    {
        public void Dispose()
        {
            if (previous != 0)
            {
                NativeWindows.SetThreadDpiAwarenessContext(previous);
            }
        }
    }

    /// <summary>Uma imagem de 32 bits (DIB) selecionada numa área de cópia compatível com a tela, com tudo o que precisa ser devolvido ao sistema no <see cref="Dispose"/>.</summary>
    private sealed class Canvas : IDisposable
    {
        private readonly int _width;
        private readonly int _height;
        private readonly nint _memory;
        private readonly nint _bitmap;
        private readonly nint _previous;
        private readonly nint _bits;

        public Canvas(int width, int height)
        {
            (_width, _height) = (width, height);
            Screen = NativeWindows.GetDC(0);
            if (Screen == 0)
            {
                throw new LibraryException("O Windows não deu acesso à tela para a captura.");
            }

            try
            {
                _memory = NativeWindows.CreateCompatibleDC(Screen);
                if (_memory == 0)
                {
                    throw new LibraryException("O Windows não criou a área de cópia da captura.");
                }

                var info = new NativeWindows.BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<NativeWindows.BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                };
                _bitmap = NativeWindows.CreateDIBSection(Screen, ref info, 0, out _bits, 0, 0);
                if (_bitmap == 0 || _bits == 0)
                {
                    throw new LibraryException($"O Windows não conseguiu reservar a imagem de {width}x{height} da captura.");
                }

                _previous = NativeWindows.SelectObject(_memory, _bitmap);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public nint Screen { get; }

        public nint Device => _memory;

        public byte[] Read()
        {
            var pixels = new byte[_width * _height * 4];
            Marshal.Copy(_bits, pixels, 0, pixels.Length);
            return pixels;
        }

        public void Dispose()
        {
            if (_previous != 0)
            {
                NativeWindows.SelectObject(_memory, _previous);
            }

            if (_bitmap != 0)
            {
                NativeWindows.DeleteObject(_bitmap);
            }

            if (_memory != 0)
            {
                NativeWindows.DeleteDC(_memory);
            }

            _ = NativeWindows.ReleaseDC(0, Screen);
        }
    }
}
