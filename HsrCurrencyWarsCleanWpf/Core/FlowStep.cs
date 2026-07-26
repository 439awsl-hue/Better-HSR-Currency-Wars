using System;
using System.Collections.Generic;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class FlowStep
{
	public required FlowStepKind Kind { get; init; }

	public required string Name { get; init; }

	public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

	public RatioRegion SearchRegion { get; init; } = CurrencyWarsFlow.FullWindow;

	public RatioPoint? ClickPoint { get; init; }

	public RatioPoint? FallbackPoint { get; init; }

	public string? Key { get; init; }

	public double TimeoutSeconds { get; init; } = 12.0;

	public double WaitAfterSeconds { get; init; }

	public bool FixedWaitAfter { get; init; }

	public double StandardDelayAfterSeconds { get; init; } = 1.3;

	public bool CheckDebuffAfterStep { get; init; }
}
