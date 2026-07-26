using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed class MouseClickService : IClickService
{
	private const int SwRestore = 9;

	private const uint MouseeventfLeftdown = 2u;

	private const uint MouseeventfLeftup = 4u;

	private const byte VkEscape = 27;

	private const uint KeyeventfKeyup = 2u;

	public async Task<ClickResult> ClickAsync(ClickRequest request, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		if (!SetCursorPos(request.ScreenX, request.ScreenY))
		{
			throw new InvalidOperationException("移动鼠标失败。");
		}
		await Task.Delay(60, cancellationToken);
		mouse_event(2u, 0u, 0u, 0u, UIntPtr.Zero);
		await Task.Delay(50, cancellationToken);
		mouse_event(4u, 0u, 0u, 0u, UIntPtr.Zero);
		return new ClickResult(Performed: true, $"真实点击：{request.Reason} @ {request.ScreenX},{request.ScreenY}");
	}

	public async Task<ClickResult> DragAsync(DragRequest request, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		if (!SetCursorPos(request.StartScreenX, request.StartScreenY))
		{
			throw new InvalidOperationException("Move cursor failed.");
		}
		await Task.Delay(100, cancellationToken);
		mouse_event(2u, 0u, 0u, 0u, UIntPtr.Zero);
		await Task.Delay(180, cancellationToken);
		for (int i = 1; i <= 12; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int x = request.StartScreenX + (int)Math.Round((double)((request.EndScreenX - request.StartScreenX) * i) / 12.0);
			int y = request.StartScreenY + (int)Math.Round((double)((request.EndScreenY - request.StartScreenY) * i) / 12.0);
			SetCursorPos(x, y);
			await Task.Delay(28, cancellationToken);
		}
		await Task.Delay(160, cancellationToken);
		mouse_event(4u, 0u, 0u, 0u, UIntPtr.Zero);
		return new ClickResult(Performed: true, $"Real drag: {request.Reason} @ {request.StartScreenX},{request.StartScreenY} -> {request.EndScreenX},{request.EndScreenY}");
	}

	public async Task<ClickResult> PressKeyAsync(string key, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (!string.Equals(key, "esc", StringComparison.OrdinalIgnoreCase) && !string.Equals(key, "escape", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("当前阶段只允许发送 Esc，不支持按键：" + key);
		}
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		keybd_event(27, 0, 0u, UIntPtr.Zero);
		await Task.Delay(50, cancellationToken);
		keybd_event(27, 0, 2u, UIntPtr.Zero);
		return new ClickResult(Performed: true, "真实按键：Esc");
	}

	private static void TryActivateWindow(nint windowHandle)
	{
		if (windowHandle == IntPtr.Zero)
		{
			return;
		}
		try
		{
			ShowWindow(windowHandle, 9);
			SetForegroundWindow(windowHandle);
		}
		catch
		{
		}
	}

	[DllImport("user32.dll")]
	private static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint handle, int command);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint handle);
}
