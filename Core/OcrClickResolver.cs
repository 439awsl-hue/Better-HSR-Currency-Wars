namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record OcrClickCandidate(OcrTextItem Item, string Alias);

public static class OcrClickResolver
{
    public static OcrClickCandidate? FindBest(OcrScanResult scan, IEnumerable<string> aliases, int fuzzyScore)
    {
        var normalizedAliases = aliases
            .Select(alias => alias.Trim())
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .OrderByDescending(alias => TextMatcher.Normalize(alias).Length)
            .ToList();

        if (normalizedAliases.Count == 0)
        {
            return null;
        }

        foreach (var alias in normalizedAliases)
        {
            foreach (var item in scan.Items)
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
