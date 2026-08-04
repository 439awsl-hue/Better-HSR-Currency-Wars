using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Services;

public readonly record struct AutoBattleDetectionResult(bool IsDisabled, double Similarity);

public static class AutoBattleStateDetector
{
	private const int ReferenceCropWidth = 144;

	private const int ReferenceCropHeight = 120;

	private const double MatchThreshold = 0.9;

	// March7thAssistant: assets/images/share/base/not_auto.png (16x17).
	// Keep the exact fixed template in the executable so portable packages cannot omit it.
	private const string DisabledTemplateBase64 = "iVBORw0KGgoAAAANSUhEUgAAABAAAAARCAYAAADUryzEAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAAHYYAAB2GAV2iE4EAAAHISURBVDhPrZPPK0RRFMe/77358YwxjR8z2VCG/NgoUshiZmNB/gAbNpKslCIsZKGE/GhSSokUKwvDkpCFHxs2olAjyc8SQma8mefe++68ebNi5FOve97p3O8553aOYLdZ1WhUhRGTSURejgvZbidEUcDt/TOC14/4Ckd4hIbZLEGwpVhUGO67MtOwH+hHqs3KPRrPrx+oax5D8OqRezQSBGTZjMvdMWZHosD7R4hYKhGTIYnMjYqGAVzfPGk/BNGY3T/QxC2gtLYXhd4u8nWjiJy0AspwbyM7Ywg2mVRAsFgkBHcnWKZB/yqm5jdYQIzKsnx4ct1Y2zjE2zutTEMXSLPLON8ZYaV7ajoRCiks4Cd4Z4AUa5KgKETll+i3BEHgVnLE0/6R/xOIRON9J9ONLhAOa69O39JbVcJsI1kZdhR6stloG9EFPj+/cHZxx+yFyVZYrSZmU+hurMx0YGe5D7Ojrdyroc8BJd2ZitPNIWbTxZle2kZEUdDS6IOD7gZJV988jsPjSxZDSRAAqc5XXYJFf7s++0baeuYQWD+i66GTKMBxuxzwVhajuryA9Czh4OgCW3snuHt44RFxBIddVpOZPCOybMY3M/qDuSeZZeAAAAAASUVORK5CYII=";

	private static readonly BitmapSource DisabledTemplate = LoadDisabledTemplate();

	public static AutoBattleDetectionResult Detect(BitmapSource image)
	{
		if (image.PixelWidth < DisabledTemplate.PixelWidth || image.PixelHeight < DisabledTemplate.PixelHeight)
		{
			return new AutoBattleDetectionResult(false, -1.0);
		}

		BitmapSource source = ConvertToGray8(image);
		byte[] sourcePixels = CopyPixels(source);
		byte[] templatePixels = CopyPixels(DisabledTemplate);
		double bestScore = -1.0;
		int maximumX = ReferenceCropWidth - DisabledTemplate.PixelWidth;
		int maximumY = ReferenceCropHeight - DisabledTemplate.PixelHeight;
		for (int y = 0; y <= maximumY; y++)
		{
			for (int x = 0; x <= maximumX; x++)
			{
				double score = CalculateNormalizedCorrelation(
					sourcePixels,
					source.PixelWidth,
					source.PixelHeight,
					templatePixels,
					DisabledTemplate.PixelWidth,
					DisabledTemplate.PixelHeight,
					x,
					y);
				if (score > bestScore)
				{
					bestScore = score;
				}
			}
		}

		return new AutoBattleDetectionResult(bestScore >= MatchThreshold, bestScore);
	}

	private static double CalculateNormalizedCorrelation(byte[] source, int sourceWidth, int sourceHeight, byte[] template, int templateWidth, int templateHeight, int left, int top)
	{
		double sourceSum = 0.0;
		double templateSum = 0.0;
		double sourceSquaredSum = 0.0;
		double templateSquaredSum = 0.0;
		double productSum = 0.0;
		int count = templateWidth * templateHeight;
		for (int templateY = 0; templateY < templateHeight; templateY++)
		{
			int referenceY = top + templateY;
			int sourceY = MapReferenceCoordinate(referenceY, ReferenceCropHeight, sourceHeight);
			for (int templateX = 0; templateX < templateWidth; templateX++)
			{
				int referenceX = left + templateX;
				int sourceX = MapReferenceCoordinate(referenceX, ReferenceCropWidth, sourceWidth);
				double sourceValue = source[sourceY * sourceWidth + sourceX];
				double templateValue = template[templateY * templateWidth + templateX];
				sourceSum += sourceValue;
				templateSum += templateValue;
				sourceSquaredSum += sourceValue * sourceValue;
				templateSquaredSum += templateValue * templateValue;
				productSum += sourceValue * templateValue;
			}
		}

		double sourceVariance = sourceSquaredSum - sourceSum * sourceSum / count;
		double templateVariance = templateSquaredSum - templateSum * templateSum / count;
		if (sourceVariance <= 0.001 || templateVariance <= 0.001)
		{
			return -1.0;
		}
		double covariance = productSum - sourceSum * templateSum / count;
		return covariance / Math.Sqrt(sourceVariance * templateVariance);
	}

	private static int MapReferenceCoordinate(int coordinate, int referenceSize, int actualSize)
	{
		return Math.Clamp((int)Math.Round(coordinate * (actualSize - 1.0) / (referenceSize - 1.0)), 0, actualSize - 1);
	}

	private static BitmapSource LoadDisabledTemplate()
	{
		using MemoryStream stream = new MemoryStream(Convert.FromBase64String(DisabledTemplateBase64));
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
