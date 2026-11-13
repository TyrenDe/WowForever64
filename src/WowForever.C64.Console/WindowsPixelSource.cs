using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WowForever.C64.Core;

namespace WowForever.C64.ConsoleApp;

[SupportedOSPlatform("windows")]
public sealed class WindowsPixelSource(string processName) : IPixelSource
{
    public string Status { get; private set; } = "Waiting for WoW";

    public PixelFrame? Capture()
    {
        var dpi = SetThreadDpiAwarenessContext(new nint(-4));
        try
        {
            var window = GetForegroundWindow();
            if (window == 0 || IsIconic(window)) return Missing("WoW is not visible in the foreground");
            GetWindowThreadProcessId(window, out var processId);
            using var process = Process.GetProcessById((int)processId);
            // Process name is OS window metadata; no game process memory is opened or read.
            if (!process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                return Missing("Return to WoW to read the pixel bridge");
            var origin = new Point();
            if (!GetClientRect(window, out var rectangle) || !ClientToScreen(window, ref origin))
                return Missing("Cannot locate the WoW client area");
            var width = Math.Min(768, rectangle.Right);
            var height = Math.Min(384, rectangle.Bottom);
            if (width < 128 || height < 16) return Missing("WoW client area is too small");
            var result = CopyDesktop(origin.X, origin.Y + rectangle.Bottom - height, width, height);
            if (GetForegroundWindow() != window) return Missing("WoW foreground window changed");
            Status = "Reading WoW pixel bridge";
            return result;
        }
        catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException)
        {
            return Missing("Capture unavailable: " + ex.Message);
        }
        finally
        {
            if (dpi != 0) SetThreadDpiAwarenessContext(dpi);
        }
    }

    private PixelFrame? Missing(string message)
    {
        Status = message;
        return null;
    }

    private static PixelFrame CopyDesktop(int x, int y, int width, int height)
    {
        var desktop = GetDC(0);
        if (desktop == 0) throw new Win32Exception();
        nint memory = 0, bitmap = 0, original = 0;
        try
        {
            memory = CreateCompatibleDC(desktop);
            if (memory == 0) throw new Win32Exception();
            var info = new BitmapInfo
            {
                Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32
            };
            bitmap = CreateDIBSection(desktop, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0 || bits == 0) throw new Win32Exception();
            original = SelectObject(memory, bitmap);
            if (original == 0 || original == -1) throw new Win32Exception();
            if (!BitBlt(memory, 0, 0, width, height, desktop, x, y, 0x40CC0020))
                throw new Win32Exception();
            if (!GdiFlush()) throw new Win32Exception();
            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return new PixelFrame(width, height, pixels);
        }
        finally
        {
            if (original != 0 && original != -1 && memory != 0) SelectObject(memory, original);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, desktop);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rectangle
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant, Color;
    }

    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out Rectangle rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
}
