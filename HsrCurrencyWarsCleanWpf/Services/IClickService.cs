using System.Threading;
using System.Threading.Tasks;

namespace HsrCurrencyWarsCleanWpf.Services;

public interface IClickService
{
	Task<ClickResult> ClickAsync(ClickRequest request, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken));

	Task<ClickResult> DragAsync(DragRequest request, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken));

	Task<ClickResult> ScrollAsync(int screenX, int screenY, int wheelDelta, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken));

	Task<ClickResult> PressKeyAsync(string key, nint windowHandle, CancellationToken cancellationToken = default(CancellationToken));

	Task<ClickResult> PressAltEnterAsync(nint windowHandle, CancellationToken cancellationToken = default(CancellationToken));
}
