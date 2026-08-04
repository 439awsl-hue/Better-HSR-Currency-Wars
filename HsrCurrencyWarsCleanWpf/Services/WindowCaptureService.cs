using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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

	private const uint MonitorDefaultToNearest = 2u;

	private const uint MonitorInfofPrimary = 1u;

	public string LastCaptureBackend { get; private set; } = "尚未截图";

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
		WindowClientRect screenRegion = ResolveRegion(window.ClientRect, region);
		if (IsWindowOnPrimaryMonitor(window.Handle))
		{
			LastCaptureBackend = "桌面 BitBlt（主屏）";
			return CaptureScreenRegion(screenRegion);
		}

		WindowClientRect clientRegion = ResolveRegion(new WindowClientRect(0, 0, window.ClientRect.Width, window.ClientRect.Height), region);
		BitmapSource windowImage = null;
		Exception windowCaptureError = null;
		try
		{
			windowImage = CaptureWindowClientRegion(window.Handle, clientRegion);
			if (!IsLikelyBlank(windowImage))
			{
				LastCaptureBackend = "窗口客户区 BitBlt（副屏）";
				return windowImage;
			}
		}
		catch (Exception ex)
		{
			windowCaptureError = ex;
		}

		try
		{
			BitmapSource screenImage = CaptureScreenRegion(screenRegion);
			if (!IsLikelyBlank(screenImage) || windowImage == null)
			{
				LastCaptureBackend = "桌面 BitBlt（副屏回退）";
				return screenImage;
			}
		}
		catch when (windowImage != null)
		{
			LastCaptureBackend = "窗口客户区 BitBlt（副屏，画面可能尚未渲染）";
			return windowImage;
		}

		if (windowImage != null)
		{
			LastCaptureBackend = "窗口客户区 BitBlt（副屏，画面可能尚未渲染）";
			return windowImage;
		}

		throw new InvalidOperationException("副屏截图失败。" + (windowCaptureError == null ? string.Empty : " 窗口截图：" + windowCaptureError.Message));
	}

	public string DescribeDisplay(GameWindowInfo window)
	{
		nint monitor = MonitorFromWindow(window.Handle, MonitorDefaultToNearest);
		if (monitor == IntPtr.Zero)
		{
			return "显示器：无法读取";
		}
		MonitorInfo info = new MonitorInfo
		{
			Size = Marshal.SizeOf<MonitorInfo>()
		};
		if (!GetMonitorInfo(monitor, ref info))
		{
			return "显示器：无法读取";
		}
		bool primary = (info.Flags & MonitorInfofPrimary) != 0;
		uint dpi = 96u;
		try
		{
			uint value = GetDpiForWindow(window.Handle);
			if (value > 0)
			{
				dpi = value;
			}
		}
		catch (EntryPointNotFoundException)
		{
		}
		int scale = (int)Math.Round(dpi * 100.0 / 96.0);
		return $"显示器：{(primary ? "主屏" : "副屏")}  bounds={info.Monitor.Left},{info.Monitor.Top},{info.Monitor.Right - info.Monitor.Left}x{info.Monitor.Bottom - info.Monitor.Top}  DPI={dpi}（{scale}%）";
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
		nint sourceDc = GetDC(IntPtr.Zero);
		if (sourceDc == IntPtr.Zero)
		{
			throw new InvalidOperationException("获取屏幕 DC 失败。");
		}
		try
		{
			return CaptureFromDc(sourceDc, rect.Left, rect.Top, rect.Width, rect.Height);
		}
		finally
		{
			ReleaseDC(IntPtr.Zero, sourceDc);
		}
	}

	private static BitmapSource CaptureWindowClientRegion(nint handle, WindowClientRect rect)
	{
		nint sourceDc = GetDC(handle);
		if (sourceDc == IntPtr.Zero)
		{
			throw new InvalidOperationException("获取游戏窗口客户区 DC 失败。");
		}
		try
		{
			return CaptureFromDc(sourceDc, rect.Left, rect.Top, rect.Width, rect.Height);
		}
		finally
		{
			ReleaseDC(handle, sourceDc);
		}
	}

	private static BitmapSource CaptureFromDc(nint sourceDc, int sourceX, int sourceY, int width, int height)
	{
		nint memoryDc = CreateCompatibleDC(sourceDc);
		if (memoryDc == IntPtr.Zero)
		{
			throw new InvalidOperationException("创建截图 DC 失败。");
		}
		nint bitmap = CreateCompatibleBitmap(sourceDc, width, height);
		if (bitmap == IntPtr.Zero)
		{
			DeleteDC(memoryDc);
			throw new InvalidOperationException("创建截图位图失败。");
		}
		nint oldObject = SelectObject(memoryDc, bitmap);
		try
		{
			if (!BitBlt(memoryDc, 0, 0, width, height, sourceDc, sourceX, sourceY, Srccopy))
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
		}
	}

	private static bool IsLikelyBlank(BitmapSource source)
	{
		const int sampleWidth = 32;
		const int sampleHeight = 18;
		double scaleX = Math.Min(1.0, sampleWidth / (double)Math.Max(1, source.PixelWidth));
		double scaleY = Math.Min(1.0, sampleHeight / (double)Math.Max(1, source.PixelHeight));
		TransformedBitmap reduced = new TransformedBitmap(source, new ScaleTransform(scaleX, scaleY));
		FormatConvertedBitmap converted = new FormatConvertedBitmap(reduced, PixelFormats.Bgra32, null, 0.0);
		int width = converted.PixelWidth;
		int height = converted.PixelHeight;
		int stride = width * 4;
		byte[] pixels = new byte[stride * height];
		converted.CopyPixels(pixels, stride, 0);
		int min = 255;
		int max = 0;
		long total = 0;
		long totalSquared = 0;
		int samples = width * height;
		for (int offset = 0; offset < pixels.Length; offset += 4)
		{
			int luminance = (pixels[offset] * 11 + pixels[offset + 1] * 59 + pixels[offset + 2] * 30) / 100;
			min = Math.Min(min, luminance);
			max = Math.Max(max, luminance);
			total += luminance;
			totalSquared += luminance * luminance;
		}
		if (samples <= 0)
		{
			return true;
		}
		double average = total / (double)samples;
		double variance = totalSquared / (double)samples - average * average;
		return max - min < 5 && variance < 2.0;
	}

	private static bool IsWindowOnPrimaryMonitor(nint handle)
	{
		nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
		if (monitor == IntPtr.Zero)
		{
			return true;
		}
		MonitorInfo info = new MonitorInfo
		{
			Size = Marshal.SizeOf<MonitorInfo>()
		};
		return !GetMonitorInfo(monitor, ref info) || (info.Flags & MonitorInfofPrimary) != 0;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MonitorInfo
	{
		public int Size;

		public RectStruct Monitor;

		public RectStruct Work;

		public uint Flags;
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
	private static extern nint MonitorFromWindow(nint handle, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(nint handle);

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
