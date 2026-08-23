using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed class MouseClickService : IClickService
{
	private const int SwRestore = 9;

	private const int SmXVirtualScreen = 76;

	private const int SmYVirtualScreen = 77;

	private const int SmCxVirtualScreen = 78;

	private const int SmCyVirtualScreen = 79;

	private const uint InputMouse = 0u;

	private const uint MouseeventfMove = 0x0001u;

	private const uint MouseeventfLeftdown = 0x0002u;

	private const uint MouseeventfLeftup = 0x0004u;

	private const uint MouseeventfWheel = 0x0800u;

	private const uint MouseeventfVirtualdesk = 0x4000u;

	private const uint MouseeventfAbsolute = 0x8000u;

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
		SendAbsoluteMove(request.ScreenX, request.ScreenY);
		await Task.Delay(60, cancellationToken);
		SendMouseButton(MouseeventfLeftdown);
		await Task.Delay(50, cancellationToken);
		SendMouseButton(MouseeventfLeftup);
		return new ClickResult(Performed: true, $"真实点击：{request.Reason} @ {request.ScreenX},{request.ScreenY}");
	}

	public async Task<ClickResult> DragAsync(DragRequest request, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		SendAbsoluteMove(request.StartScreenX, request.StartScreenY);
		SendMouseButton(MouseeventfLeftdown);
		SendAbsoluteMove(request.EndScreenX, request.EndScreenY);
		SendMouseButton(MouseeventfLeftup);
		return new ClickResult(Performed: true, $"真实拖拽：{request.Reason} @ {request.StartScreenX},{request.StartScreenY} -> {request.EndScreenX},{request.EndScreenY}");
	}

	public async Task<ClickResult> ScrollAsync(int screenX, int screenY, int wheelDelta, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken))
	{
		cancellationToken.ThrowIfCancellationRequested();
		TryActivateWindow(windowHandle);
		await Task.Delay(120, cancellationToken);
		SendAbsoluteMove(screenX, screenY);
		await Task.Delay(80, cancellationToken);
		SendMouseWheel(wheelDelta);
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

	private static void SendAbsoluteMove(int screenX, int screenY)
	{
		(int absoluteX, int absoluteY) = ToAbsoluteCoordinates(screenX, screenY);
		SendMouseInput(absoluteX, absoluteY, 0u, MouseeventfMove | MouseeventfAbsolute | MouseeventfVirtualdesk);
	}

	private static void SendMouseButton(uint buttonFlag)
	{
		SendMouseInput(0, 0, 0u, buttonFlag);
	}

	private static void SendMouseWheel(int wheelDelta)
	{
		SendMouseInput(0, 0, unchecked((uint)wheelDelta), MouseeventfWheel);
	}

	private static void SendMouseInput(int dx, int dy, uint mouseData, uint flags)
	{
		INPUT[] inputs =
		[
			new INPUT
			{
				type = InputMouse,
				U = new InputUnion
				{
					mi = new MOUSEINPUT
					{
						dx = dx,
						dy = dy,
						mouseData = mouseData,
						dwFlags = flags,
						time = 0u,
						dwExtraInfo = UIntPtr.Zero
					}
				}
			}
		];
		uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
		if (sent != inputs.Length)
		{
			throw new InvalidOperationException("模拟鼠标输入失败。请确认当前会话允许注入输入，必要时以管理员身份运行。");
		}
	}

	private static (int absoluteX, int absoluteY) ToAbsoluteCoordinates(int screenX, int screenY)
	{
		int virtualLeft = GetSystemMetrics(SmXVirtualScreen);
		int virtualTop = GetSystemMetrics(SmYVirtualScreen);
		int virtualWidth = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
		int virtualHeight = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
		int absoluteX = (int)Math.Round((double)(screenX - virtualLeft) * 65535.0 / Math.Max(1, virtualWidth - 1));
		int absoluteY = (int)Math.Round((double)(screenY - virtualTop) * 65535.0 / Math.Max(1, virtualHeight - 1));
		return (ClampAbsolute(absoluteX), ClampAbsolute(absoluteY));
	}

	private static int ClampAbsolute(int value)
	{
		if (value < 0)
		{
			return 0;
		}
		if (value > 65535)
		{
			return 65535;
		}
		return value;
	}

	private static void TryActivateWindow(nint windowHandle)
	{
		if (windowHandle == IntPtr.Zero)
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
	private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int sizeOfInput);

	[DllImport("user32.dll")]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint handle, int command);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint handle);

	[StructLayout(LayoutKind.Sequential)]
	private struct INPUT
	{
		public uint type;

		public InputUnion U;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		// Keep union size aligned with Win32 INPUT on x64 (SendInput expects 40-byte INPUT).
		[FieldOffset(0)]
		public KEYBDINPUT ki;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nuint dwExtraInfo;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nuint dwExtraInfo;
	}
}
