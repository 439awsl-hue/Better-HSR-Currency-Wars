using System;
using System.Collections.Generic;
using System.Linq;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class AutomationConfig
{
	public const int MaxTargetAllWords = 4;

	public const int MaxTargetAnyWords = 20;

	public const int MaxInvestmentWords = 20;

	public const int MaxInGameWords = 20;

	public const int WordHistoryLimit = 100;

	public string WindowTitle { get; set; } = "";

	public bool DebuffEnabled { get; set; } = true;

	public bool DebuffMatchAny { get; set; }

	public List<string> TargetWords { get; set; } = new List<string>();

	public bool BlockedEnabled { get; set; }

	public List<string> BlockedWords { get; set; } = new List<string>();

	public bool InvestmentEnabled { get; set; } = true;

	public List<string> InvestmentTargets { get; set; } = new List<string>();

	public bool CheckInvestmentWhenBlocked { get; set; }

	public string InGameStrategyTarget { get; set; } = "";

	public string InGameInvestmentTarget { get; set; } = "";

	public List<string> InGameStrategyTargets { get; set; } = new List<string>();

	public List<string> InGameInvestmentTargets { get; set; } = new List<string>();

	public List<string> TargetWordHistory { get; set; } = new List<string>();

	public List<string> BlockedWordHistory { get; set; } = new List<string>();

	public List<string> InvestmentWordHistory { get; set; } = new List<string>();

	public List<string> InGameStrategyHistory { get; set; } = new List<string>();

	public List<string> InGameInvestmentHistory { get; set; } = new List<string>();

	public int FuzzyScore { get; set; } = 82;

	public int ButtonFuzzyScore { get; set; } = 78;

	public int InvestmentFuzzyScore { get; set; } = 88;

	public double StartDelaySeconds { get; set; } = 1.0;

	public double DebuffCheckDelaySeconds { get; set; } = 4.0;

	public double InvestmentIntervalSeconds { get; set; } = 0.2;

	public void Normalize()
	{
		InvestmentEnabled = true;
		int targetLimit = (DebuffMatchAny ? 20 : 4);
		TargetWords = NormalizeWords(TargetWords, targetLimit);
		BlockedWords = NormalizeWords(BlockedWords, 20);
		InvestmentTargets = NormalizeWords(InvestmentTargets, 20);
		InGameStrategyTargets = NormalizeWords(InGameStrategyTargets, 20);
		InGameInvestmentTargets = NormalizeWords(InGameInvestmentTargets, 20);
		if (InGameStrategyTargets.Count == 0 && !string.IsNullOrWhiteSpace(InGameStrategyTarget))
		{
			InGameStrategyTargets = NormalizeWords(new _003C_003Ez__ReadOnlySingleElementList<string>(InGameStrategyTarget), 20);
		}
		if (InGameInvestmentTargets.Count == 0 && !string.IsNullOrWhiteSpace(InGameInvestmentTarget))
		{
			InGameInvestmentTargets = NormalizeWords(new _003C_003Ez__ReadOnlySingleElementList<string>(InGameInvestmentTarget), 20);
		}
		InGameStrategyTarget = InGameStrategyTargets.FirstOrDefault() ?? "";
		InGameInvestmentTarget = InGameInvestmentTargets.FirstOrDefault() ?? "";
		TargetWordHistory = MergeHistory(TargetWords, TargetWordHistory);
		BlockedWordHistory = MergeHistory(BlockedWords, BlockedWordHistory);
		InvestmentWordHistory = MergeHistory(InvestmentTargets, InvestmentWordHistory);
		InGameStrategyHistory = MergeHistory(InGameStrategyTargets, InGameStrategyHistory);
		InGameInvestmentHistory = MergeHistory(InGameInvestmentTargets, InGameInvestmentHistory);
	}

	private static List<string> NormalizeWords(IEnumerable<string>? words, int maxCount)
	{
		return (from word in words ?? Array.Empty<string>()
			select word.Trim() into word
			where !string.IsNullOrWhiteSpace(word)
			select word).Distinct<string>(StringComparer.Ordinal).Take(maxCount).ToList();
	}

	private static List<string> MergeHistory(params IEnumerable<string>[] wordLists)
	{
		List<string> history = new List<string>();
		for (int i = 0; i < wordLists.Length; i++)
		{
			foreach (string item in wordLists[i])
			{
				string value = item.Trim();
				if (!string.IsNullOrWhiteSpace(value))
				{
					history.Remove(value);
					history.Insert(0, value);
				}
			}
		}
		return history.Take(100).ToList();
	}
}
