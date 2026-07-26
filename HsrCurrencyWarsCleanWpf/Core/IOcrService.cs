using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Core;

public interface IOcrService
{
	string Name { get; }

	Task<OcrScanResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default(CancellationToken));
}
