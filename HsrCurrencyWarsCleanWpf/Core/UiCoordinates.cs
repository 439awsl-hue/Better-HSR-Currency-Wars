using System;
using HsrCurrencyWarsCleanWpf.Services;

namespace HsrCurrencyWarsCleanWpf.Core;

/// <summary>
/// UI 坐标以 1920x1080 为基准维护；客户区为 1080p 的 1x/1.5x/2x 等倍数时按倍率映射。
/// </summary>
public static class UiCoordinates
{
	public const int RefWidth = 1920;

	public const int RefHeight = 1080;

	public static RatioPoint From1080(int x, int y)
	{
		return new RatioPoint(x / (double)RefWidth, y / (double)RefHeight);
	}

	public static RatioRegion From1080(int x, int y, int width, int height)
	{
		return new RatioRegion(
			x / (double)RefWidth,
			y / (double)RefHeight,
			width / (double)RefWidth,
			height / (double)RefHeight);
	}

	public static double GetScale(int clientWidth, int clientHeight)
	{
		if (clientHeight <= 0)
		{
			return 1.0;
		}
		return clientHeight / (double)RefHeight;
	}

	public static bool TryGetExact1080pScale(int clientWidth, int clientHeight, out double scale)
	{
		scale = 0.0;
		if (clientWidth <= 0 || clientHeight <= 0)
		{
			return false;
		}

		double scaleY = clientHeight / (double)RefHeight;
		double expectedWidth = RefWidth * scaleY;
		if (Math.Abs(clientWidth - expectedWidth) > 1.5)
		{
			return false;
		}

		// 允许常见半整数倍：1 / 1.5 / 2 / 2.5 / 3 ...
		double halfSteps = scaleY * 2.0;
		if (Math.Abs(halfSteps - Math.Round(halfSteps)) > 0.001)
		{
			return false;
		}

		scale = Math.Round(scaleY * 2.0) / 2.0;
		return scale > 0.0;
	}

	public static string DescribeScale(int clientWidth, int clientHeight)
	{
		if (TryGetExact1080pScale(clientWidth, clientHeight, out double scale))
		{
			return $"{scale:0.##}x（{clientWidth}x{clientHeight}，1080p 整数倍）";
		}

		double approx = GetScale(clientWidth, clientHeight);
		return $"约 {approx:0.##}x（{clientWidth}x{clientHeight}，非标准 1080p 倍数，仍按比例映射）";
	}

	public static (int X, int Y) ToScreen(RatioPoint point, WindowClientRect clientRect)
	{
		if (TryGetExact1080pScale(clientRect.Width, clientRect.Height, out double scale))
		{
			int x1080 = (int)Math.Round(point.X * RefWidth);
			int y1080 = (int)Math.Round(point.Y * RefHeight);
			return (
				clientRect.Left + (int)Math.Round(x1080 * scale),
				clientRect.Top + (int)Math.Round(y1080 * scale));
		}

		return (
			clientRect.Left + (int)Math.Round(clientRect.Width * point.X),
			clientRect.Top + (int)Math.Round(clientRect.Height * point.Y));
	}

	public static WindowClientRect ToScreenRegion(RatioRegion region, WindowClientRect clientRect)
	{
		if (TryGetExact1080pScale(clientRect.Width, clientRect.Height, out double scale))
		{
			int x1080 = (int)Math.Round(region.X * RefWidth);
			int y1080 = (int)Math.Round(region.Y * RefHeight);
			int w1080 = Math.Max(1, (int)Math.Round(region.Width * RefWidth));
			int h1080 = Math.Max(1, (int)Math.Round(region.Height * RefHeight));
			return new WindowClientRect(
				clientRect.Left + (int)Math.Round(x1080 * scale),
				clientRect.Top + (int)Math.Round(y1080 * scale),
				Math.Max(1, (int)Math.Round(w1080 * scale)),
				Math.Max(1, (int)Math.Round(h1080 * scale)));
		}

		int left = clientRect.Left + (int)Math.Round(clientRect.Width * region.X);
		int top = clientRect.Top + (int)Math.Round(clientRect.Height * region.Y);
		int width = Math.Max(1, (int)Math.Round(clientRect.Width * region.Width));
		int height = Math.Max(1, (int)Math.Round(clientRect.Height * region.Height));
		return new WindowClientRect(left, top, width, height);
	}
}
