using System.Runtime.InteropServices;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed record ClickRequest(string Reason, int ScreenX, int ScreenY);

public sealed record DragRequest(string Reason, int StartScreenX, int StartScreenY, int EndScreenX, int EndScreenY);

public sealed record ClickResult(bool Performed, string Message);

public interface IClickService
{
    Task<ClickResult> ClickAsync(ClickRequest request, nint windowHandle, CancellationToken cancellationToken = default);
    Task<ClickResult> DragAsync(DragRequest request, nint windowHandle, CancellationToken cancellationToken = default);
    Task<ClickResult> PressKeyAsync(string key, nint windowHandle, CancellationToken cancellationToken = default);
}

public sealed class MouseClickService : IClickService
{
    private const int SwRestore = 9;
    private const uint MouseeventfLeftdown = 0x0002;
    private const uint MouseeventfLeftup = 0x0004;
    private const byte VkEscape = 0x1B;
    private const uint KeyeventfKeyup = 0x0002;

    public async Task<ClickResult> ClickAsync(ClickRequest request, nint windowHandle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryActivateWindow(windowHandle);
        await Task.Delay(120, cancellationToken);

        if (!SetCursorPos(request.ScreenX, request.ScreenY))
        {
            throw new InvalidOperationException("移动鼠标失败。");
        }

        await Task.Delay(60, cancellationToken);
        mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(50, cancellationToken);
        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        return new ClickResult(true, $"真实点击：{request.Reason} @ {request.ScreenX},{request.ScreenY}");
    }

    public async Task<ClickResult> DragAsync(DragRequest request, nint windowHandle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryActivateWindow(windowHandle);
        await Task.Delay(120, cancellationToken);

        if (!SetCursorPos(request.StartScreenX, request.StartScreenY))
        {
            throw new InvalidOperationException("Move cursor failed.");
        }

        await Task.Delay(100, cancellationToken);
        mouse_event(MouseeventfLeftdown, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(180, cancellationToken);

        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var x = request.StartScreenX + (int)Math.Round((request.EndScreenX - request.StartScreenX) * i / (double)steps);
            var y = request.StartScreenY + (int)Math.Round((request.EndScreenY - request.StartScreenY) * i / (double)steps);
            SetCursorPos(x, y);
            await Task.Delay(28, cancellationToken);
        }

        await Task.Delay(160, cancellationToken);
        mouse_event(MouseeventfLeftup, 0, 0, 0, UIntPtr.Zero);
        return new ClickResult(true, $"Real drag: {request.Reason} @ {request.StartScreenX},{request.StartScreenY} -> {request.EndScreenX},{request.EndScreenY}");
    }

    public async Task<ClickResult> PressKeyAsync(string key, nint windowHandle, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(key, "esc", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"当前阶段只允许发送 Esc，不支持按键：{key}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        TryActivateWindow(windowHandle);
        await Task.Delay(120, cancellationToken);
        keybd_event(VkEscape, 0, 0, UIntPtr.Zero);
        await Task.Delay(50, cancellationToken);
        keybd_event(VkEscape, 0, KeyeventfKeyup, UIntPtr.Zero);
        return new ClickResult(true, "真实按键：Esc");
    }

    private static void TryActivateWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return;
        }

        try
        {
            ShowWindow(windowHandle, SwRestore);
            SetForegroundWindow(windowHandle);
        }
        catch
        {
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint handle);
}
