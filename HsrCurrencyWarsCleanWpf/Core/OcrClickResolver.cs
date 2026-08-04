using System.Collections.Generic;
using System.Linq;

namespace HsrCurrencyWarsCleanWpf.Core;

public static class OcrClickResolver
{
	public static OcrClickCandidate? FindBest(OcrScanResult scan, IEnumerable<string> aliases, int fuzzyScore)
	{
		List<string> normalizedAliases = (from text in aliases
			select text.Trim() into text
			where !string.IsNullOrWhiteSpace(text)
			orderby TextMatcher.Normalize(text).Length descending
			select text).ToList();
		if (normalizedAliases.Count == 0)
		{
			return null;
		}
		foreach (string alias in normalizedAliases)
		{
			foreach (OcrTextItem item in scan.Items)
			{
				if (TextMatcher.FuzzyContains(item.Text, alias, fuzzyScore))
				{
					return new OcrClickCandidate(item, alias);
				}
			}
		}
		return null;
	}

	public static OcrClickCandidate? FindByPriority(OcrScanResult scan, IEnumerable<string> aliases, int fuzzyScore)
	{
		List<string> prioritizedAliases = (from text in aliases
			select text.Trim() into text
			where !string.IsNullOrWhiteSpace(text)
			select text).Distinct<string>(System.StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string alias in prioritizedAliases)
		{
			foreach (OcrTextItem item in scan.Items)
			{
				if (TextMatcher.FuzzyContains(item.Text, alias, fuzzyScore))
				{
					return new OcrClickCandidate(item, alias);
				}
			}
		}
		return null;
	}
}
