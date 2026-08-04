using System.Collections.Generic;

namespace HsrCurrencyWarsCleanWpf.Core;

public static class CurrencyWarsFlow
{
	public static readonly RatioRegion FullWindow = new RatioRegion(0.0, 0.0, 1.0, 1.0);

	public static readonly RatioRegion TopHalf = new RatioRegion(0.0, 0.0, 1.0, 0.5);

	public static readonly RatioRegion BottomHalf = new RatioRegion(0.0, 0.5, 1.0, 0.5);

	public static readonly RatioRegion LeftBottom = new RatioRegion(0.0, 0.5, 0.5, 0.5);

	public static readonly RatioRegion RightBottom = new RatioRegion(0.5, 0.5, 0.5, 0.5);

	public static readonly RatioPoint FastEscApproxPoint = new RatioPoint(0.035, 0.055);

	public static readonly RatioPoint FastSettlementApproxPoint = new RatioPoint(0.39, 0.69);

	public static readonly RatioPoint OpeningRapidAdvancePoint = new RatioPoint(1660.0 / 1920.0, 965.0 / 1080.0);

	public const double OpeningRapidAdvanceDurationSeconds = 3.5;

	public const double OpeningRapidAdvanceClickIntervalSeconds = 0.3;

	public static readonly string[] DebuffScreenHints = new string[5] { "敌人难度", "下一步", "随从强化", "沉重脚步", "变宝为废" };

	public static readonly string[] InvestmentEntryScreenAliases = new string[1] { "投资环境" };

	public static readonly string[] InvestmentChoiceScreenAliases = new string[1] { "剩余次数" };

	public static readonly string[] SettlementDialogAliases = new string[3] { "放弃并结算", "放弃", "结算" };

	public static readonly string[] SpecialInvestmentBlacklist = new string[8] { "蓝海", "蓝嗨", "蓝烸", "蓝塰", "特邀专家：银狼", "专家研讨会", "特邀专家：加拉赫", "特邀专家：停云" };

	public static readonly RatioPoint[] InvestmentFallbackPoints = new RatioPoint[3]
	{
		new RatioPoint(0.23, 0.38),
		new RatioPoint(0.5, 0.38),
		new RatioPoint(0.77, 0.38)
	};

	public static readonly RatioRegion[] InvestmentCardSearchRegions = new RatioRegion[3]
	{
		new RatioRegion(202.0 / 1920.0, 195.0 / 1080.0, 470.0 / 1920.0, 672.0 / 1080.0),
		new RatioRegion(725.0 / 1920.0, 196.0 / 1080.0, 468.0 / 1920.0, 670.0 / 1080.0),
		new RatioRegion(1247.0 / 1920.0, 198.0 / 1080.0, 465.0 / 1920.0, 668.0 / 1080.0)
	};

	public const double InvestmentCollectionMarkerThreshold = 0.9;

	public const double DebuffRecheckTimeoutSeconds = 3.0;

	public const double DebuffRecheckIntervalSeconds = 0.3;

	public const int InvestmentScanAttemptCount = 3;

	public const double InvestmentRecheckIntervalSeconds = 0.1;

	public const double FastExitProbeIntervalSeconds = 0.11;

	public const int FastExitSettlementAlternateClickCount = 7;

	public const int BottomReturnFixedClickCount = 6;

	public const double MajorPageScanIntervalSeconds = 0.6;

	public const double InvestmentPageWaitTimeoutSeconds = 20.0;

	public const double SettlementPageWaitTimeoutSeconds = 20.0;

	public static readonly IReadOnlyList<FlowStep> Steps = new global::_003C_003Ez__ReadOnlyArray<FlowStep>(new FlowStep[13]
	{
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "开始「货币战争」",
			Aliases = new global::_003C_003Ez__ReadOnlyArray<string>(new string[3] { "开始货币战争", "开始", "货币战争" }),
			TimeoutSeconds = 8.0,
			SearchRegion = RightBottom,
			FallbackPoint = new RatioPoint(0.82, 0.91),
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "进入标准博弈",
			Aliases = new global::_003C_003Ez__ReadOnlyArray<string>(new string[3] { "进入标准博弈", "开始标准博弈", "标准博弈" }),
			TimeoutSeconds = 12.0,
			SearchRegion = RightBottom,
			FallbackPoint = new RatioPoint(0.82, 0.9),
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "开始对局",
			Aliases = new global::_003C_003Ez__ReadOnlyArray<string>(new string[2] { "开始对局", "对局" }),
			TimeoutSeconds = 8.0,
			CheckDebuffAfterStep = true,
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "下一步",
			Aliases = new _003C_003Ez__ReadOnlySingleElementList<string>("下一步"),
			TimeoutSeconds = 8.0,
			FallbackPoint = new RatioPoint(0.88, 0.895),
			StandardDelayAfterSeconds = 0.8
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickRelativePoint,
			Name = "点击空白继续",
			ClickPoint = new RatioPoint(0.5, 0.58),
			WaitAfterSeconds = 1.0,
			StandardDelayAfterSeconds = 0.8
		},
		new FlowStep
		{
			Kind = FlowStepKind.SafeInvestmentChoice,
			Name = "默认选择安全投资",
			WaitAfterSeconds = 2.8,
			FixedWaitAfter = true,
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.RepeatSafeInvestmentChoice,
			Name = "动画后再次点击安全投资",
			WaitAfterSeconds = 0.0,
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.InvestmentSearch,
			Name = "投资识别",
			StandardDelayAfterSeconds = 0.4
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickRelativePoint,
			Name = "固定确认",
			ClickPoint = new RatioPoint(0.565, 0.91),
			StandardDelayAfterSeconds = 0.2
		},
		new FlowStep
		{
			Kind = FlowStepKind.FastExitToSettlement,
			Name = "左上角退出并结算",
			Aliases = new global::_003C_003Ez__ReadOnlyArray<string>(new string[3] { "放弃并结算", "放弃", "结算" }),
			TimeoutSeconds = 8.0,
			StandardDelayAfterSeconds = 0.4
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "下一步",
			Aliases = new _003C_003Ez__ReadOnlySingleElementList<string>("下一步"),
			TimeoutSeconds = 8.0,
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "下一页",
			Aliases = new _003C_003Ez__ReadOnlySingleElementList<string>("下一页"),
			TimeoutSeconds = 8.0,
			StandardDelayAfterSeconds = 0.0
		},
		new FlowStep
		{
			Kind = FlowStepKind.ClickText,
			Name = "返回货币战争",
			Aliases = new global::_003C_003Ez__ReadOnlyArray<string>(new string[2] { "返回货币战争", "返回" }),
			TimeoutSeconds = 8.0,
			StandardDelayAfterSeconds = 0.0
		}
	});
}
