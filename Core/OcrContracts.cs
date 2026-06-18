using System.Windows;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record OcrTextItem(string Text, Rect Bounds, double Confidence);

public sealed record OcrScanResult(string RawText, IReadOnlyList<OcrTextItem> Items, DateTime ScannedAt)
{
    public static OcrScanResult Empty { get; } = new("", [], DateTime.Now);
}

public interface IOcrService
{
    string Name { get; }
    Task<OcrScanResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default);
}

public sealed class PendingOcrService : IOcrService
{
    public PendingOcrService(string reason)
    {
        Name = $"OCR 未就绪：{reason}";
    }

    public string Name { get; }

    public Task<OcrScanResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OcrScanResult.Empty);
    }
}
