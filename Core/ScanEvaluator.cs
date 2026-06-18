namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record BasicScanEvaluation(
    MatchResult TargetMatch,
    MatchResult BlockedMatch,
    MatchResult InvestmentMatch,
    bool DebuffSuccess,
    bool BlockedHit,
    bool TargetSatisfied,
    string DebuffModeText,
    string DecisionReason);

public sealed class ScanEvaluator
{
    public BasicScanEvaluation Evaluate(AutomationConfig config, string ocrText)
    {
        config.Normalize();

        var targetMatch = config.DebuffEnabled
            ? TextMatcher.MatchTargets(config.TargetWords, ocrText, config.FuzzyScore)
            : new MatchResult([], []);

        var blockedMatch = config.BlockedEnabled
            ? TextMatcher.MatchTargets(config.BlockedWords, ocrText, config.FuzzyScore)
            : new MatchResult([], []);

        var investmentMatch = config.InvestmentEnabled
            ? TextMatcher.MatchTargets(config.InvestmentTargets, ocrText, config.InvestmentFuzzyScore)
            : new MatchResult([], []);

        var blockedHit = config.BlockedEnabled && blockedMatch.HitWords.Count > 0;
        var targetSatisfied = IsTargetSatisfied(config, targetMatch);
        var debuffSuccess = config.DebuffEnabled && !blockedHit && targetSatisfied;
        var modeText = config.DebuffMatchAny ? "任意命中" : "全部命中";
        var reason = CreateDecisionReason(config, blockedMatch, targetMatch, blockedHit, targetSatisfied, debuffSuccess);
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

    private static string CreateDecisionReason(
        AutomationConfig config,
        MatchResult blockedMatch,
        MatchResult targetMatch,
        bool blockedHit,
        bool targetSatisfied,
        bool debuffSuccess)
    {
        if (!config.DebuffEnabled)
        {
            return "继续刷新：主词条检测未开启。";
        }

        if (blockedHit)
        {
            var next = config.CheckInvestmentWhenBlocked ? "本轮仍会继续检查投资识别" : "本轮跳过投资识别";
            return $"继续刷新：命中不想要词条：{string.Join("、", blockedMatch.HitWords)}。{next}。";
        }

        if (debuffSuccess)
        {
            return "主词条条件已满足，且没有命中不想要词条。";
        }

        if (!targetSatisfied && targetMatch.MissingWords.Count > 0)
        {
            return $"继续刷新：主词条缺少：{string.Join("、", targetMatch.MissingWords)}。";
        }

        return "继续刷新：未命中主词条。";
    }
}
