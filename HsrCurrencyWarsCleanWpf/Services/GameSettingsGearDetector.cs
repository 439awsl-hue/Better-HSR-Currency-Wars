using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Services;

public readonly record struct GameSettingsGearDetectionResult(bool Found, double Similarity, int CenterX, int CenterY);

public static class GameSettingsGearDetector
{
	private readonly record struct TemplateSample(int X, int Y, byte Value);

	private const double ReferenceHeight = 1080.0;

	private const double MatchThreshold = 0.65;

	private const string GearTemplateBase64 = "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";

	private static readonly BitmapSource GearTemplate = LoadTemplate();

	private static readonly IReadOnlyList<TemplateSample> Samples = BuildTemplateSamples(GearTemplate);

	public static GameSettingsGearDetectionResult Detect(BitmapSource rightQuarterScreenshot)
	{
		BitmapSource source = ConvertToGray8(rightQuarterScreenshot);
		byte[] sourcePixels = CopyPixels(source);
		double scale = Math.Max(0.5, source.PixelHeight / ReferenceHeight);
		int scaledWidth = Math.Max(1, (int)Math.Round(GearTemplate.PixelWidth * scale));
		int scaledHeight = Math.Max(1, (int)Math.Round(GearTemplate.PixelHeight * scale));
		int left = Math.Clamp((int)Math.Round(source.PixelWidth * 0.68), 0, source.PixelWidth - 1);
		int right = source.PixelWidth;
		int top = Math.Clamp((int)Math.Round(source.PixelHeight * 0.26), 0, source.PixelHeight - 1);
		int bottom = Math.Clamp((int)Math.Round(source.PixelHeight * 0.62), top + 1, source.PixelHeight);
		int maximumX = right - scaledWidth;
		int maximumY = bottom - scaledHeight;
		if (maximumX < left || maximumY < top)
		{
			return new GameSettingsGearDetectionResult(false, -1.0, 0, 0);
		}

		double bestScore = -1.0;
		int bestX = left;
		int bestY = top;
		int coarseStep = Math.Max(1, (int)Math.Round(2.0 * scale));
		for (int y = top; y <= maximumY; y += coarseStep)
		{
			for (int x = left; x <= maximumX; x += coarseStep)
			{
				double score = CalculateNormalizedCorrelation(sourcePixels, source.PixelWidth, Samples, x, y, scale);
				if (score > bestScore)
				{
					bestScore = score;
					bestX = x;
					bestY = y;
				}
			}
		}

		int refineLeft = Math.Max(left, bestX - coarseStep);
		int refineRight = Math.Min(maximumX, bestX + coarseStep);
		int refineTop = Math.Max(top, bestY - coarseStep);
		int refineBottom = Math.Min(maximumY, bestY + coarseStep);
		for (int y = refineTop; y <= refineBottom; y++)
		{
			for (int x = refineLeft; x <= refineRight; x++)
			{
				double score = CalculateNormalizedCorrelation(sourcePixels, source.PixelWidth, Samples, x, y, scale);
				if (score > bestScore)
				{
					bestScore = score;
					bestX = x;
					bestY = y;
				}
			}
		}

		return new GameSettingsGearDetectionResult(
			bestScore >= MatchThreshold,
			bestScore,
			bestX + scaledWidth / 2,
			bestY + scaledHeight / 2);
	}

	private static double CalculateNormalizedCorrelation(byte[] source, int sourceWidth, IReadOnlyList<TemplateSample> samples, int left, int top, double scale)
	{
		double sourceSum = 0.0;
		double templateSum = 0.0;
		double sourceSquaredSum = 0.0;
		double templateSquaredSum = 0.0;
		double productSum = 0.0;
		for (int index = 0; index < samples.Count; index++)
		{
			TemplateSample sample = samples[index];
			double sourceValue = source[(top + (int)Math.Round(sample.Y * scale)) * sourceWidth + left + (int)Math.Round(sample.X * scale)];
			double templateValue = sample.Value;
			sourceSum += sourceValue;
			templateSum += templateValue;
			sourceSquaredSum += sourceValue * sourceValue;
			templateSquaredSum += templateValue * templateValue;
			productSum += sourceValue * templateValue;
		}

		double count = samples.Count;
		double sourceVariance = sourceSquaredSum - sourceSum * sourceSum / count;
		double templateVariance = templateSquaredSum - templateSum * templateSum / count;
		if (sourceVariance <= 0.001 || templateVariance <= 0.001)
		{
			return -1.0;
		}
		double covariance = productSum - sourceSum * templateSum / count;
		return covariance / Math.Sqrt(sourceVariance * templateVariance);
	}

	private static IReadOnlyList<TemplateSample> BuildTemplateSamples(BitmapSource template)
	{
		byte[] pixels = CopyPixels(template);
		List<TemplateSample> samples = new List<TemplateSample>();
		for (int y = 0; y < template.PixelHeight; y += 2)
		{
			for (int x = 0; x < template.PixelWidth; x += 2)
			{
				samples.Add(new TemplateSample(x, y, pixels[y * template.PixelWidth + x]));
			}
		}
		return samples;
	}

	private static BitmapSource LoadTemplate()
	{
		using MemoryStream stream = new MemoryStream(Convert.FromBase64String(GearTemplateBase64));
		PngBitmapDecoder decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		BitmapSource template = ConvertToGray8(decoder.Frames[0]);
		template.Freeze();
		return template;
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
