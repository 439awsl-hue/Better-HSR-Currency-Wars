using System;
using System.Collections.Generic;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record OcrScanResult(string RawText, IReadOnlyList<OcrTextItem> Items, DateTime ScannedAt)
{
	public static OcrScanResult Empty { get; } = new OcrScanResult("", Array.Empty<OcrTextItem>(), DateTime.Now);
}
