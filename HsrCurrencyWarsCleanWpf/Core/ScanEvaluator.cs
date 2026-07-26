using System;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class ScanEvaluator
{
	public BasicScanEvaluation Evaluate(AutomationConfig config, string ocrText)
	{
		config.Normalize();
		MatchResult targetMatch = (config.DebuffEnabled ? TextMatcher.MatchTargets(config.TargetWords, ocrText, config.FuzzyScore) : new MatchResult(Array.Empty<string>(), Array.Empty<string>()));
		MatchResult blockedMatch = (config.BlockedEnabled ? TextMatcher.MatchTargets(config.BlockedWords, ocrText, config.FuzzyScore) : new MatchResult(Array.Empty<string>(), Array.Empty<string>()));
		MatchResult investmentMatch = (config.InvestmentEnabled ? TextMatcher.MatchTargets(config.InvestmentTargets, ocrText, config.InvestmentFuzzyScore) : new MatchResult(Array.Empty<string>(), Array.Empty<string>()));
		bool blockedHit = config.BlockedEnabled && blockedMatch.HitWords.Count > 0;
		bool targetSatisfied = IsTargetSatisfied(config, targetMatch);
		bool debuffSuccess = config.DebuffEnabled && !blockedHit && targetSatisfied;
		string modeText = (config.DebuffMatchAny ? "任意命中" : "全部命中");
		string reason = CreateDecisionReason(config, blockedMatch, targetMatch, blockedHit, targetSatisfied, debuffSuccess);
		return new BasicScanEvaluation(targetMatch, blockedMatch, investmentMatch, debuffSuccess, blockedHit, targetSatisfied, modeText, reason);
	}

	private static bool IsTargetSatisfied(AutomationConfig config, MatchResult targetMatch)
	{
		if (!config.DebuffEnabled)
		{
			return false;
		}
		if (config.DebuffMatchAny)
		{
			return targetMatch.HitWords.Count > 0;
		}
		return targetMatch.MissingWords.Count == 0;
	}

	private static string CreateDecisionReason(AutomationConfig config, MatchResult blockedMatch, MatchResult targetMatch, bool blockedHit, bool targetSatisfied, bool debuffSuccess)
	{
		if (!config.DebuffEnabled)
		{
			return "继续刷新：主词条检测未开启。";
		}
		if (blockedHit)
		{
			string next = (config.CheckInvestmentWhenBlocked ? "本轮仍会继续检查投资识别" : "本轮跳过投资识别");
			return $"继续刷新：命中不想要词条：{string.Join("、", blockedMatch.HitWords)}。{next}。";
		}
		if (debuffSuccess)
		{
			return "主词条条件已满足，且没有命中不想要词条。";
		}
		if (!targetSatisfied && targetMatch.MissingWords.Count > 0)
		{
			return "继续刷新：主词条缺少：" + string.Join("、", targetMatch.MissingWords) + "。";
		}
		return "继续刷新：未命中主词条。";
	}
}
