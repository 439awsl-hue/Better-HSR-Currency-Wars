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

	public string HiddenReleaseNotesVersion { get; set; } = "";

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

	public bool CombinedDebuffEnabled { get; set; } = true;

	public bool CombinedDebuffMatchAny { get; set; }

	public List<string> CombinedTargetWords { get; set; } = new List<string>();

	public bool CombinedBlockedEnabled { get; set; }

	public List<string> CombinedBlockedWords { get; set; } = new List<string>();

	public bool CombinedCheckInvestmentWhenBlocked { get; set; }

	public List<string> CombinedInvestmentTargets { get; set; } = new List<string>();

	public List<string> CombinedInGameStrategyTargets { get; set; } = new List<string>();

	public List<string> CombinedInGameInvestmentTargets { get; set; } = new List<string>();

	public CombinedMainRule CombinedMainRule { get; set; } = CombinedMainRule.StopOnMatch;

	public CombinedBlockedRule CombinedBlockedRule { get; set; } = CombinedBlockedRule.RestartOnMatch;

	public CombinedOuterInvestmentRule CombinedOuterInvestmentRule { get; set; } = CombinedOuterInvestmentRule.StopOnMatch;

	public CombinedInGameInvestmentRule CombinedInGameInvestmentRule { get; set; } = CombinedInGameInvestmentRule.RequireThenContinue;

	public bool CombinedFlowRulesConfigured { get; set; }

	public List<string> TargetWordHistory { get; set; } = new List<string>();

	public List<string> BlockedWordHistory { get; set; } = new List<string>();

	public List<string> InvestmentWordHistory { get; set; } = new List<string>();

	public List<string> InGameStrategyHistory { get; set; } = new List<string>();

	public List<string> InGameInvestmentHistory { get; set; } = new List<string>();

	public List<string> CombinedTargetWordHistory { get; set; } = new List<string>();

	public List<string> CombinedBlockedWordHistory { get; set; } = new List<string>();

	public List<string> CombinedInvestmentWordHistory { get; set; } = new List<string>();

	public List<string> CombinedInGameStrategyHistory { get; set; } = new List<string>();

	public List<string> CombinedInGameInvestmentHistory { get; set; } = new List<string>();

	public int FuzzyScore { get; set; } = 85;

	public int BlockedFuzzyScore { get; set; } = 85;

	public int ButtonFuzzyScore { get; set; } = 78;

	public int InvestmentFuzzyScore { get; set; } = 88;

	public double StartDelaySeconds { get; set; } = 1.0;

	public double DebuffCheckDelaySeconds { get; set; } = 4.0;

	public double InvestmentIntervalSeconds { get; set; } = 0.2;

	public void Normalize()
	{
		InvestmentEnabled = true;
		FuzzyScore = Math.Max(FuzzyScore, 85);
		BlockedFuzzyScore = Math.Max(BlockedFuzzyScore, 85);
		if (!CombinedFlowRulesConfigured)
		{
			CombinedMainRule = CombinedDebuffEnabled ? CombinedMainRule.StopOnMatch : CombinedMainRule.Ignore;
			CombinedBlockedRule = CombinedBlockedEnabled ? CombinedBlockedRule.RestartOnMatch : CombinedBlockedRule.Ignore;
			CombinedOuterInvestmentRule = CombinedInvestmentTargets.Count > 0
				? CombinedOuterInvestmentRule.StopOnMatch
				: (CombinedInGameInvestmentTargets.Count > 0 ? CombinedOuterInvestmentRule.RequireThenContinue : CombinedOuterInvestmentRule.Ignore);
			CombinedInGameInvestmentRule = CombinedInGameInvestmentTargets.Count > 0 ? CombinedInGameInvestmentRule.RequireThenContinue : CombinedInGameInvestmentRule.Ignore;
			CombinedFlowRulesConfigured = true;
		}
		CombinedDebuffEnabled = CombinedMainRule != CombinedMainRule.Ignore;
		CombinedBlockedEnabled = CombinedBlockedRule != CombinedBlockedRule.Ignore;
		CombinedCheckInvestmentWhenBlocked = CombinedBlockedRule == CombinedBlockedRule.ContinueOnMatch;
		int targetLimit = (DebuffMatchAny ? 20 : 4);
		TargetWords = NormalizeWords(TargetWords, targetLimit);
		BlockedWords = NormalizeWords(BlockedWords, 20);
		InvestmentTargets = NormalizeWords(InvestmentTargets, 20);
		InGameStrategyTargets = NormalizeWords(InGameStrategyTargets, 20);
		InGameInvestmentTargets = NormalizeWords(InGameInvestmentTargets, 20);
		int combinedTargetLimit = (CombinedDebuffMatchAny ? 20 : 4);
		CombinedTargetWords = NormalizeWords(CombinedTargetWords, combinedTargetLimit);
		CombinedBlockedWords = NormalizeWords(CombinedBlockedWords, 20);
		CombinedInvestmentTargets = NormalizeWords(CombinedInvestmentTargets, 20);
		CombinedInGameStrategyTargets = NormalizeWords(CombinedInGameStrategyTargets, 20);
		CombinedInGameInvestmentTargets = NormalizeWords(CombinedInGameInvestmentTargets, 20);
		CombinedInvestmentTargets = NormalizeWords(CombinedInvestmentTargets.Concat(CombinedInGameInvestmentTargets), 20);
		CombinedInGameInvestmentTargets = CombinedInvestmentTargets.ToList();
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
		CombinedTargetWordHistory = MergeHistory(CombinedTargetWords, CombinedTargetWordHistory);
		CombinedBlockedWordHistory = MergeHistory(CombinedBlockedWords, CombinedBlockedWordHistory);
		CombinedInvestmentWordHistory = MergeHistory(CombinedInvestmentTargets, CombinedInGameInvestmentHistory, CombinedInvestmentWordHistory);
		CombinedInGameStrategyHistory = MergeHistory(CombinedInGameStrategyTargets, CombinedInGameStrategyHistory);
		CombinedInGameInvestmentHistory = CombinedInvestmentWordHistory.ToList();
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
