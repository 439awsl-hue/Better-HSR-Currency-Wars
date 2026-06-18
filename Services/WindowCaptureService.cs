using HsrCurrencyWarsCleanWpf.Core;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed record GameWindowInfo(nint Handle, string Title, WindowClientRect ClientRect);

public sealed record WindowClientRect(int Left, int Top, int Width, int Height);

public sealed record CaptureRegion(string Name, double X, double Y, double Width, double Height)
{
    public static CaptureRegion FullWindow { get; } = new("全窗口", 0.0, 0.0, 1.0, 1.0);
    public static CaptureRegion TopHalf { get; } = new("上半屏", 0.0, 0.0, 1.0, 0.5);
    public static CaptureRegion BottomHalf { get; } = new("下半屏", 0.0, 0.5, 1.0, 0.5);
    public static CaptureRegion LeftBottom { get; } = new("左下角", 0.0, 0.5, 0.5, 0.5);
    public static CaptureRegion RightBottom { get; } = new("右下角", 0.5, 0.5, 0.5, 0.5);
}

public sealed class WindowCaptureService
{
    private const int Srccopy = 0x00CC0020;

    public GameWindowInfo FindWindow(string titleKeyword)
    {
        var keyword = titleKeyword.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            throw new InvalidOperationException("请先输入游戏窗口标题的一部分。");
        }

        var matches = new List<(nint Handle, string Title)>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                return true;
            }

            var title = new string('\0', length + 1);
            var copied = GetWindowText(handle, title, title.Length);
            if (copied <= 0)
            {
                return true;
            }

            title = title[..copied];
            if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((handle, title));
            }

            return true;
        }, nint.Zero);

        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"找不到标题包含“{titleKeyword}”的游戏窗口。");
        }

        var match = matches[0];
        return new GameWindowInfo(match.Handle, match.Title, GetClientRectOnScreen(match.Handle));
    }

    public BitmapSource Capture(GameWindowInfo window, CaptureRegion region)
    {
        var rect = ResolveRegion(window.ClientRect, region);
        return CaptureScreenRegion(rect);
    }

    public WindowClientRect ResolveRegion(WindowClientRect rect, CaptureRegion region)
    {
        var left = rect.Left + (int)Math.Round(rect.Width * region.X);
        var top = rect.Top + (int)Math.Round(rect.Height * region.Y);
        var width = Math.Max(1, (int)Math.Round(rect.Width * region.Width));
        var height = Math.Max(1, (int)Math.Round(rect.Height * region.Height));
        return new WindowClientRect(left, top, width, height);
    }

    private static WindowClientRect GetClientRectOnScreen(nint handle)
    {
        if (!GetClientRect(handle, out var clientRect))
        {
            throw new InvalidOperationException("读取游戏窗口客户区失败。");
        }

        var point = new PointStruct(0, 0);
        if (!ClientToScreen(handle, ref point))
        {
            throw new InvalidOperationException("转换游戏窗口客户区坐标失败。");
        }

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("游戏窗口大小无效，请确认窗口没有最小化。");
        }

        return new WindowClientRect(point.X, point.Y, width, height);
    }

    private static BitmapSource CaptureScreenRegion(WindowClientRect rect)
    {
        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
        {
            throw new InvalidOperationException("获取屏幕 DC 失败。");
        }

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == nint.Zero)
        {
            ReleaseDC(nint.Zero, screenDc);
            throw new InvalidOperationException("创建截图 DC 失败。");
        }

        var bitmap = CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
        if (bitmap == nint.Zero)
        {
            DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, screenDc);
            throw new InvalidOperationException("创建截图位图失败。");
        }

        var oldObject = SelectObject(memoryDc, bitmap);
        try
        {
            if (!BitBlt(memoryDc, 0, 0, rect.Width, rect.Height, screenDc, rect.Left, rect.Top, Srccopy))
            {
                throw new InvalidOperationException("截图失败。");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap,
                nint.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            SelectObject(memoryDc, oldObject);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, screenDc);
        }
    }

    private delegate bool EnumWindowsProc(nint handle, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, string text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint handle, out RectStruct rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint handle, ref PointStruct point);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint handle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint handle, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint destinationDc, int x, int y, int width, int height, nint sourceDc, int sourceX, int sourceY, int rasterOperation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public PointStruct(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X;
        public int Y;
    }
}
