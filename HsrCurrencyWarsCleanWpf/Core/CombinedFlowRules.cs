namespace HsrCurrencyWarsCleanWpf.Core;

public enum CombinedMainRule
{
	Ignore,
	RequireThenContinue,
	StopOnMatch,
	OptionalContinue
}

public enum CombinedBlockedRule
{
	Ignore,
	RestartOnMatch,
	ContinueOnMatch
}

public enum CombinedOuterInvestmentRule
{
	Ignore,
	RequireThenContinue,
	OptionalContinue,
	StopOnMatch
}

public enum CombinedInGameInvestmentRule
{
	Ignore,
	RequireThenContinue,
	OptionalContinue
}
