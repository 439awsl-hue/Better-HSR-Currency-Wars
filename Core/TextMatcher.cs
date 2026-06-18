using System.Globalization;
using System.Text;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record MatchResult(IReadOnlyList<string> HitWords, IReadOnlyList<string> MissingWords);

public static class TextMatcher
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(rune.ToString().ToLower(CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static bool FuzzyContains(string text, string target, int score)
    {
        var normalizedText = Normalize(text);
        var normalizedTarget = Normalize(target);
        if (normalizedText.Length == 0 || normalizedTarget.Length == 0)
        {
            return false;
        }

        if (normalizedText.Contains(normalizedTarget, StringComparison.Ordinal))
        {
            return true;
        }

        return TextWindows(normalizedText, normalizedTarget.Length)
            .Any(part => Similarity(normalizedTarget, part) >= score);
    }

    public static MatchResult MatchTargets(IEnumerable<string> targetWords, string ocrText, int fuzzyScore)
    {
        var hitWords = new List<string>();
        var missingWords = new List<string>();

        foreach (var word in targetWords)
        {
            if (FuzzyContains(ocrText, word, fuzzyScore))
            {
                hitWords.Add(word);
            }
            else
            {
                missingWords.Add(word);
            }
        }

        return new MatchResult(hitWords, missingWords);
    }

    private static IEnumerable<string> TextWindows(string text, int targetLength)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        if (text.Length <= targetLength)
        {
            yield return text;
            yield break;
        }

        var minSize = Math.Max(1, targetLength - 1);
        var maxSize = Math.Min(text.Length, targetLength + 2);
        for (var size = minSize; size <= maxSize; size++)
        {
            for (var start = 0; start <= text.Length - size; start++)
            {
                yield return text.Substring(start, size);
            }
        }
    }

    private static int Similarity(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0)
        {
            return 100;
        }

        var distance = LevenshteinDistance(left, right);
        var maxLength = Math.Max(left.Length, right.Length);
        return (int)Math.Round((1.0 - (double)distance / maxLength) * 100);
    }

    private static int LevenshteinDistance(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }
}
