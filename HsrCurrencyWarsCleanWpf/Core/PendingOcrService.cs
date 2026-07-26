using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class PendingOcrService : IOcrService
{
	public string Name { get; }

	public PendingOcrService(string reason)
	{
		Name = "OCR 未就绪：" + reason;
	}

	public Task<OcrScanResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default(CancellationToken))
	{
		return Task.FromResult(OcrScanResult.Empty);
	}
}
