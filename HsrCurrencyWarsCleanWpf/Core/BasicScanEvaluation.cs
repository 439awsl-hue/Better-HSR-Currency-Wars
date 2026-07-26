namespace HsrCurrencyWarsCleanWpf.Core;

public sealed record BasicScanEvaluation(MatchResult TargetMatch, MatchResult BlockedMatch, MatchResult InvestmentMatch, bool DebuffSuccess, bool BlockedHit, bool TargetSatisfied, string DebuffModeText, string DecisionReason);
