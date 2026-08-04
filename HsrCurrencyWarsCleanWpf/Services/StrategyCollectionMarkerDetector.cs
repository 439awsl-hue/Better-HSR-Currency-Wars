using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HsrCurrencyWarsCleanWpf.Core;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed record StrategyCollectionMarkerMatch(int Column, double Score);

public static class StrategyCollectionMarkerDetector
{
	private readonly record struct TemplateSample(int X, int Y, byte Value);

	private const double ReferenceWidth = 1920.0;

	private const double ReferenceHeight = 1080.0;

	public static IReadOnlyList<StrategyCollectionMarkerMatch> FindMatches(BitmapSource screenshot, BitmapSource template, IReadOnlyList<RatioRegion> cardRegions, CancellationToken cancellationToken = default(CancellationToken))
	{
		BitmapSource source = ConvertToGray8(screenshot);
		BitmapSource marker = ConvertToGray8(template);
		byte[] sourcePixels = CopyPixels(source);
		byte[] markerPixels = CopyPixels(marker);
		List<TemplateSample> samples = BuildTemplateSamples(markerPixels, marker.PixelWidth, marker.PixelHeight);
		double scaleX = source.PixelWidth / ReferenceWidth;
		double scaleY = source.PixelHeight / ReferenceHeight;
		int scaledWidth = Math.Max(1, (int)Math.Round(marker.PixelWidth * scaleX));
		int scaledHeight = Math.Max(1, (int)Math.Round(marker.PixelHeight * scaleY));
		int coarseStepX = Math.Max(1, (int)Math.Round(2.0 * scaleX));
		int coarseStepY = Math.Max(1, (int)Math.Round(2.0 * scaleY));
		List<StrategyCollectionMarkerMatch> matches = new List<StrategyCollectionMarkerMatch>(cardRegions.Count);
		for (int column = 0; column < cardRegions.Count; column++)
		{
			RatioRegion region = cardRegions[column];
			int left = Math.Clamp((int)Math.Floor(source.PixelWidth * region.X), 0, source.PixelWidth - 1);
			int top = Math.Clamp((int)Math.Floor(source.PixelHeight * region.Y), 0, source.PixelHeight - 1);
			int right = Math.Clamp((int)Math.Ceiling(source.PixelWidth * (region.X + region.Width)), left + 1, source.PixelWidth);
			int bottom = Math.Clamp((int)Math.Ceiling(source.PixelHeight * (region.Y + region.Height)), top + 1, source.PixelHeight);
			double score = FindBestScore(sourcePixels, source.PixelWidth, samples, left, top, right, bottom, scaledWidth, scaledHeight, scaleX, scaleY, coarseStepX, coarseStepY, cancellationToken);
			matches.Add(new StrategyCollectionMarkerMatch(column, score));
		}
		return matches;
	}

	private static double FindBestScore(byte[] sourcePixels, int sourceWidth, IReadOnlyList<TemplateSample> samples, int left, int top, int right, int bottom, int scaledWidth, int scaledHeight, double scaleX, double scaleY, int coarseStepX, int coarseStepY, CancellationToken cancellationToken)
	{
		int maximumX = right - scaledWidth;
		int maximumY = bottom - scaledHeight;
		if (maximumX < left || maximumY < top)
		{
			return -1.0;
		}

		double bestScore = -1.0;
		int bestX = left;
		int bestY = top;
		for (int y = top; y <= maximumY; y += coarseStepY)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int x = left; x <= maximumX; x += coarseStepX)
			{
				double score = CalculateNormalizedCorrelation(sourcePixels, sourceWidth, samples, x, y, scaleX, scaleY);
				if (score > bestScore)
				{
					bestScore = score;
					bestX = x;
					bestY = y;
				}
			}
		}

		int refineLeft = Math.Max(left, bestX - coarseStepX);
		int refineTop = Math.Max(top, bestY - coarseStepY);
		int refineRight = Math.Min(maximumX, bestX + coarseStepX);
		int refineBottom = Math.Min(maximumY, bestY + coarseStepY);
		for (int y = refineTop; y <= refineBottom; y++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int x = refineLeft; x <= refineRight; x++)
			{
				bestScore = Math.Max(bestScore, CalculateNormalizedCorrelation(sourcePixels, sourceWidth, samples, x, y, scaleX, scaleY));
			}
		}
		return bestScore;
	}

	private static double CalculateNormalizedCorrelation(byte[] sourcePixels, int sourceWidth, IReadOnlyList<TemplateSample> samples, int left, int top, double scaleX, double scaleY)
	{
		double sumTemplate = 0.0;
		double sumSource = 0.0;
		double sumTemplateSquared = 0.0;
		double sumSourceSquared = 0.0;
		double sumProduct = 0.0;
		for (int index = 0; index < samples.Count; index++)
		{
			TemplateSample sample = samples[index];
			double templateValue = sample.Value;
			int sourceX = left + (int)Math.Round(sample.X * scaleX);
			int sourceY = top + (int)Math.Round(sample.Y * scaleY);
			double sourceValue = sourcePixels[sourceY * sourceWidth + sourceX];
			sumTemplate += templateValue;
			sumSource += sourceValue;
			sumTemplateSquared += templateValue * templateValue;
			sumSourceSquared += sourceValue * sourceValue;
			sumProduct += templateValue * sourceValue;
		}

		double count = samples.Count;
		double templateVariance = sumTemplateSquared - sumTemplate * sumTemplate / count;
		double sourceVariance = sumSourceSquared - sumSource * sumSource / count;
		if (templateVariance <= 0.001 || sourceVariance <= 0.001)
		{
			return -1.0;
		}
		double covariance = sumProduct - sumTemplate * sumSource / count;
		return covariance / Math.Sqrt(templateVariance * sourceVariance);
	}

	private static List<TemplateSample> BuildTemplateSamples(byte[] pixels, int width, int height)
	{
		List<TemplateSample> samples = new List<TemplateSample>();
		for (int y = 0; y < height; y += 2)
		{
			for (int x = 0; x < width; x += 2)
			{
				samples.Add(new TemplateSample(x, y, pixels[y * width + x]));
			}
		}
		return samples;
	}

	private static BitmapSource ConvertToGray8(BitmapSource image)
	{
		if (image.Format == PixelFormats.Gray8)
		{
			return image;
		}
		FormatConvertedBitmap converted = new FormatConvertedBitmap();
		converted.BeginInit();
		converted.Source = image;
		converted.DestinationFormat = PixelFormats.Gray8;
		converted.EndInit();
		converted.Freeze();
		return converted;
	}

	private static byte[] CopyPixels(BitmapSource image)
	{
		int stride = image.PixelWidth;
		byte[] pixels = new byte[stride * image.PixelHeight];
		image.CopyPixels(pixels, stride, 0);
		return pixels;
	}
}
