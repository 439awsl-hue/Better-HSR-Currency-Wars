using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HsrCurrencyWarsCleanWpf.Core;

public static class TextMatcher
{
	private static readonly string[] PlaneStrengtheningPrefixes = new string[3] { "第一位面", "第二位面", "第三位面" };

	public static string Normalize(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "";
		}
		StringBuilder builder = new StringBuilder(text.Length);
		foreach (Rune rune in text.EnumerateRunes())
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
		string normalizedText = Normalize(text);
		string normalizedTarget = Normalize(target);
		if (normalizedText.Length == 0 || normalizedTarget.Length == 0)
		{
			return false;
		}
		if (normalizedText.Contains(normalizedTarget, StringComparison.Ordinal))
		{
			return true;
		}
		return TextWindows(normalizedText, normalizedTarget.Length).Any((string part) => Similarity(normalizedTarget, part) >= score);
	}

	public static MatchResult MatchTargets(IEnumerable<string> targetWords, string ocrText, int fuzzyScore)
	{
		List<string> hitWords = new List<string>();
		List<string> missingWords = new List<string>();
		foreach (string word in targetWords)
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

	public static MatchResult MatchDebuffTargets(IEnumerable<string> targetWords, string ocrText, int fuzzyScore)
	{
		List<string> hitWords = new List<string>();
		List<string> missingWords = new List<string>();
		foreach (string word in targetWords)
		{
			if (FuzzyContainsDebuffTarget(ocrText, word, fuzzyScore))
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

	private static bool FuzzyContainsDebuffTarget(string text, string target, int score)
	{
		string normalizedTarget = Normalize(target);
		string expectedPrefix = PlaneStrengtheningPrefixes.FirstOrDefault((string prefix) => normalizedTarget == prefix + "强化");
		if (expectedPrefix == null)
		{
			return FuzzyContains(text, target, score);
		}
		string normalizedText = Normalize(text);
		if (!normalizedText.Contains(expectedPrefix, StringComparison.Ordinal))
		{
			return false;
		}
		return FuzzyContains(text, target, score);
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
		int minSize = Math.Max(1, targetLength - 1);
		int maxSize = Math.Min(text.Length, targetLength + 2);
		for (int size = minSize; size <= maxSize; size++)
		{
			for (int start = 0; start <= text.Length - size; start++)
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
		int distance = LevenshteinDistance(left, right);
		int maxLength = Math.Max(left.Length, right.Length);
		return (int)Math.Round((1.0 - (double)distance / (double)maxLength) * 100.0);
	}

	private static int LevenshteinDistance(string left, string right)
	{
		int[] previous = new int[right.Length + 1];
		int[] current = new int[right.Length + 1];
		for (int column = 0; column <= right.Length; column++)
		{
			previous[column] = column;
		}
		for (int row = 1; row <= left.Length; row++)
		{
			current[0] = row;
			for (int i = 1; i <= right.Length; i++)
			{
				int substitutionCost = ((left[row - 1] != right[i - 1]) ? 1 : 0);
				current[i] = Math.Min(Math.Min(current[i - 1] + 1, previous[i] + 1), previous[i - 1] + substitutionCost);
			}
			int[] array = current;
			current = previous;
			previous = array;
		}
		return previous[right.Length];
	}
}
