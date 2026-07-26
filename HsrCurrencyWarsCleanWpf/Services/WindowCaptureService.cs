using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed class WindowCaptureService
{
	private delegate bool EnumWindowsProc(nint handle, nint lParam);

	private struct RectStruct
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private struct PointStruct(int x, int y)
	{
		public int X = x;

		public int Y = y;
	}

	private const int Srccopy = 13369376;

	public GameWindowInfo FindWindow(string titleKeyword)
	{
		string keyword = titleKeyword.Trim();
		if (string.IsNullOrWhiteSpace(keyword))
		{
			throw new InvalidOperationException("请先输入游戏窗口标题的一部分。");
		}
		List<(nint Handle, string Title)> matches = new List<(nint, string)>();
		EnumWindows(delegate(nint handle, nint _)
		{
			if (!IsWindowVisible(handle))
			{
				return true;
			}
			int windowTextLength = GetWindowTextLength(handle);
			if (windowTextLength <= 0)
			{
				return true;
			}
			string text = new string('\0', windowTextLength + 1);
			int windowText = GetWindowText(handle, text, text.Length);
			if (windowText <= 0)
			{
				return true;
			}
			text = text.Substring(0, windowText);
			if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
			{
				matches.Add((handle, text));
			}
			return true;
		}, IntPtr.Zero);
		if (matches.Count == 0)
		{
			throw new InvalidOperationException("找不到标题包含“" + titleKeyword + "”的游戏窗口。");
		}
		(nint, string) match = matches[0];
		return new GameWindowInfo(match.Item1, match.Item2, GetClientRectOnScreen(match.Item1));
	}

	public BitmapSource Capture(GameWindowInfo window, CaptureRegion region)
	{
		return CaptureScreenRegion(ResolveRegion(window.ClientRect, region));
	}

	public WindowClientRect ResolveRegion(WindowClientRect rect, CaptureRegion region)
	{
		int left = rect.Left + (int)Math.Round((double)rect.Width * region.X);
		int top = rect.Top + (int)Math.Round((double)rect.Height * region.Y);
		int width = Math.Max(1, (int)Math.Round((double)rect.Width * region.Width));
		int height = Math.Max(1, (int)Math.Round((double)rect.Height * region.Height));
		return new WindowClientRect(left, top, width, height);
	}

	private static WindowClientRect GetClientRectOnScreen(nint handle)
	{
		if (!GetClientRect(handle, out var clientRect))
		{
			throw new InvalidOperationException("读取游戏窗口客户区失败。");
		}
		PointStruct point = new PointStruct(0, 0);
		if (!ClientToScreen(handle, ref point))
		{
			throw new InvalidOperationException("转换游戏窗口客户区坐标失败。");
		}
		int width = clientRect.Right - clientRect.Left;
		int height = clientRect.Bottom - clientRect.Top;
		if (width <= 0 || height <= 0)
		{
			throw new InvalidOperationException("游戏窗口大小无效，请确认窗口没有最小化。");
		}
		return new WindowClientRect(point.X, point.Y, width, height);
	}

	private static BitmapSource CaptureScreenRegion(WindowClientRect rect)
	{
		nint screenDc = GetDC(IntPtr.Zero);
		if (screenDc == IntPtr.Zero)
		{
			throw new InvalidOperationException("获取屏幕 DC 失败。");
		}
		nint memoryDc = CreateCompatibleDC(screenDc);
		if (memoryDc == IntPtr.Zero)
		{
			ReleaseDC(IntPtr.Zero, screenDc);
			throw new InvalidOperationException("创建截图 DC 失败。");
		}
		nint bitmap = CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
		if (bitmap == IntPtr.Zero)
		{
			DeleteDC(memoryDc);
			ReleaseDC(IntPtr.Zero, screenDc);
			throw new InvalidOperationException("创建截图位图失败。");
		}
		nint oldObject = SelectObject(memoryDc, bitmap);
		try
		{
			if (!BitBlt(memoryDc, 0, 0, rect.Width, rect.Height, screenDc, rect.Left, rect.Top, 13369376))
			{
				throw new InvalidOperationException("截图失败。");
			}
			BitmapSource bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
			bitmapSource.Freeze();
			return bitmapSource;
		}
		finally
		{
			SelectObject(memoryDc, oldObject);
			DeleteObject(bitmap);
			DeleteDC(memoryDc);
			ReleaseDC(IntPtr.Zero, screenDc);
		}
	}

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
}
