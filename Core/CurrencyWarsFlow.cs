namespace HsrCurrencyWarsCleanWpf.Core;

public enum FlowStepKind
{
    ClickText,
    ClickRelativePoint,
    SafeInvestmentChoice,
    RepeatSafeInvestmentChoice,
    InvestmentSearch,
    PressKey,
    FastExitToSettlement
}

public sealed record RatioPoint(double X, double Y);

public sealed record RatioRegion(double X, double Y, double Width, double Height);

public sealed class FlowStep
{
    public required FlowStepKind Kind { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
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

public static class CurrencyWarsFlow
{
    public static readonly RatioRegion FullWindow = new(0.0, 0.0, 1.0, 1.0);
    public static readonly RatioRegion TopHalf = new(0.0, 0.0, 1.0, 0.5);
    public static readonly RatioRegion BottomHalf = new(0.0, 0.5, 1.0, 0.5);
    public static readonly RatioRegion LeftBottom = new(0.0, 0.5, 0.5, 0.5);
    public static readonly RatioRegion RightBottom = new(0.5, 0.5, 0.5, 0.5);
    public static readonly RatioPoint FastEscApproxPoint = new(0.035, 0.055);
    public static readonly RatioPoint FastSettlementApproxPoint = new(0.39, 0.69);

    public static readonly string[] DebuffScreenHints =
    [
        "敌人难度",
        "下一步",
        "随从强化",
        "沉重脚步",
        "变宝为废"
    ];

    public static readonly string[] SpecialInvestmentBlacklist =
    [
        "蓝海",
        "特邀专家：银狼",
        "专家研讨会",
        "特邀专家：加拉赫",
        "特邀专家：停云"
    ];

    public static readonly RatioPoint[] InvestmentFallbackPoints =
    [
        new(0.23, 0.38),
        new(0.50, 0.38),
        new(0.77, 0.38)
    ];

    public const double DebuffRecheckTimeoutSeconds = 3.0;
    public const double DebuffRecheckIntervalSeconds = 0.3;
    public const int InvestmentScanAttemptCount = 4;
    public const double InvestmentRecheckIntervalSeconds = 0.1;
    public const double FastExitProbeIntervalSeconds = 0.11;
    public const int FastExitSettlementAlternateClickCount = 8;

    public static readonly IReadOnlyList<FlowStep> Steps =
    [
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "开始「货币战争」",
            Aliases = ["开始货币战争", "开始", "货币战争"],
            TimeoutSeconds = 8,
            SearchRegion = RightBottom,
            FallbackPoint = new RatioPoint(0.82, 0.91),
            StandardDelayAfterSeconds = 0.8
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "进入标准博弈",
            Aliases = ["进入标准博弈", "开始标准博弈", "标准博弈"],
            TimeoutSeconds = 12,
            SearchRegion = RightBottom,
            FallbackPoint = new RatioPoint(0.82, 0.90),
            StandardDelayAfterSeconds = 0.7
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "开始对局",
            Aliases = ["开始对局", "对局"],
            TimeoutSeconds = 15,
            CheckDebuffAfterStep = true,
            StandardDelayAfterSeconds = 0.8
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "下一步",
            Aliases = ["下一步"],
            TimeoutSeconds = 15,
            StandardDelayAfterSeconds = 0.8
        },
        new()
        {
            Kind = FlowStepKind.ClickRelativePoint,
            Name = "点击空白继续",
            ClickPoint = new RatioPoint(0.50, 0.58),
            WaitAfterSeconds = 1.0,
            StandardDelayAfterSeconds = 0.8
        },
        new()
        {
            Kind = FlowStepKind.SafeInvestmentChoice,
            Name = "默认选择安全投资",
            WaitAfterSeconds = 3.0,
            FixedWaitAfter = true,
            StandardDelayAfterSeconds = 0.0
        },
        new()
        {
            Kind = FlowStepKind.RepeatSafeInvestmentChoice,
            Name = "动画后再次点击安全投资",
            WaitAfterSeconds = 0.0,
            StandardDelayAfterSeconds = 0.0
        },
        new()
        {
            Kind = FlowStepKind.InvestmentSearch,
            Name = "投资识别",
            StandardDelayAfterSeconds = 0.7
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "确认",
            Aliases = ["确认", "确定"],
            TimeoutSeconds = 15,
            StandardDelayAfterSeconds = 0.4
        },
        new()
        {
            Kind = FlowStepKind.FastExitToSettlement,
            Name = "左上角退出并结算",
            Aliases = ["放弃并结算", "放弃", "结算"],
            TimeoutSeconds = 15,
            StandardDelayAfterSeconds = 0.4
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "下一步",
            Aliases = ["下一步"],
            TimeoutSeconds = 15,
            StandardDelayAfterSeconds = 0.4
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "下一页",
            Aliases = ["下一页"],
            TimeoutSeconds = 15,
            StandardDelayAfterSeconds = 0.5
        },
        new()
        {
            Kind = FlowStepKind.ClickText,
            Name = "返回货币战争",
            Aliases = ["返回货币战争", "返回"],
            TimeoutSeconds = 15,
            StandardDelayAfterSeconds = 0.7
        }
    ];
}
