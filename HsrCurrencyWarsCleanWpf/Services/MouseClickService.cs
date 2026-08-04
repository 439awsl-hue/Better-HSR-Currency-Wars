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

	private const uint MouseeventfWheel = 2048u;

	private const byte VkEscape = 27;

	private const byte VkV = 86;

	private const byte VkMenu = 18;

	private const byte VkReturn = 13;

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

	public async Task<ClickResult> ScrollAsync(int screenX, int screenY, int wheelDelta, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		if (!SetCursorPos(screenX, screenY))
		{
			throw new InvalidOperationException("Move cursor failed.");
		}
		await Task.Delay(80, cancellationToken);
		mouse_event(MouseeventfWheel, 0u, 0u, unchecked((uint)wheelDelta), UIntPtr.Zero);
		return new ClickResult(Performed: true, $"真实滚轮：{wheelDelta} @ {screenX},{screenY}");
	}

	public async Task<ClickResult> PressKeyAsync(string key, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		byte virtualKey;
		string displayName;
		if (string.Equals(key, "esc", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "escape", StringComparison.OrdinalIgnoreCase))
		{
			virtualKey = VkEscape;
			displayName = "Esc";
		}
		else if (string.Equals(key, "v", StringComparison.OrdinalIgnoreCase))
		{
			virtualKey = VkV;
			displayName = "V";
		}
		else
		{
			throw new InvalidOperationException("当前只支持按键 Esc 和 V，不支持：" + key);
		}
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		keybd_event(virtualKey, 0, 0u, UIntPtr.Zero);
		await Task.Delay(50, cancellationToken);
		keybd_event(virtualKey, 0, KeyeventfKeyup, UIntPtr.Zero);
		return new ClickResult(Performed: true, "真实按键：" + displayName);
	}

	public async Task<ClickResult> PressAltEnterAsync(nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		bool enterIsDown = false;
		keybd_event(VkMenu, 0, 0u, UIntPtr.Zero);
		try
		{
			await Task.Delay(200, cancellationToken);
			keybd_event(VkReturn, 0, 0u, UIntPtr.Zero);
			enterIsDown = true;
			await Task.Delay(50, cancellationToken);
			keybd_event(VkReturn, 0, KeyeventfKeyup, UIntPtr.Zero);
			enterIsDown = false;
		}
		finally
		{
			if (enterIsDown)
			{
				keybd_event(VkReturn, 0, KeyeventfKeyup, UIntPtr.Zero);
			}
			keybd_event(VkMenu, 0, KeyeventfKeyup, UIntPtr.Zero);
		}
		return new ClickResult(Performed: true, "真实组合键：Alt（提前 0.2 秒）+ Enter");
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
