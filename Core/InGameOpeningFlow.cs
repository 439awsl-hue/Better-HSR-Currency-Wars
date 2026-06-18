namespace HsrCurrencyWarsCleanWpf.Core;

public static class InGameOpeningFlow
{
    public static readonly RatioPoint[] PrepareSlots =
    [
        new(0.229, 0.846),
        new(0.294, 0.845),
        new(0.359, 0.846),
        new(0.425, 0.843)
    ];

    public static readonly RatioPoint[] ForwardSlots =
    [
        new(0.388, 0.367),
        new(0.462, 0.368),
        new(0.537, 0.368),
        new(0.609, 0.370)
    ];

    public static readonly RatioPoint BattleButton = new(0.952, 0.694);
    public static readonly RatioPoint ContinueFallbackPoint = new(0.50, 0.58);
    public static readonly RatioPoint StrategyConfirmPoint = new(0.50, 0.91);
    public static readonly RatioPoint GalaStarConfirmPoint = new(0.775, 0.523);
    public static readonly RatioPoint UnderfilledDoNotRemindPoint = new(0.463, 0.560);
    public static readonly RatioPoint UnderfilledConfirmPoint = new(0.612, 0.622);
    public static readonly RatioRegion BattleButtonRegion = new(0.84, 0.62, 0.16, 0.22);
    public static readonly RatioRegion DialogRegion = new(0.20, 0.04, 0.70, 0.65);
    public static readonly RatioRegion StrategyRegion = new(0.0, 0.08, 1.0, 0.66);

    public static readonly RatioPoint[] GalaStarChoices =
    [
        new(0.485, 0.255),
        new(0.610, 0.255)
    ];

    public static readonly RatioPoint[] StrategyRefreshButtons =
    [
        new(0.203, 0.795),
        new(0.500, 0.795),
        new(0.724, 0.795)
    ];

    public static readonly RatioPoint[] StrategyCards =
    [
        new(0.240, 0.455),
        new(0.500, 0.455),
        new(0.760, 0.455)
    ];

    public static readonly string[] BattleButtonAliases =
    [
        "出战",
        "跳过"
    ];

    public static readonly string[] ContinueButtonAliases =
    [
        "点击空白处继续",
        "下一步",
        "下一页",
        "继续挑战",
        "前往结算",
        "确认"
    ];

    public static readonly string[] TargetStrategyAliases =
    [
        "本姑娘就是罗刹"
    ];

    public static readonly string[] ReincarnationStrategyAliases =
    [
        "轮回不止"
    ];

    public static readonly string[] SandGoldStrategyAliases =
    [
        "砂里淘金"
    ];

    public static readonly string[] PrismInvestmentGateAliases =
    [
        "彩虹时代",
        "银·金·彩",
        "银金彩",
        "头彩"
    ];

    public static readonly string[] LongTermGoodInvestmentGateAliases =
    [
        "长线利好",
        "轮岗"
    ];

    public static readonly string[] GalaStarAliases =
    [
        "盛会之星",
        "请选择1名角色成为巨星",
        "请选择强化角色",
        "确认选择"
    ];

    public static readonly string[] UnderfilledTeamAliases =
    [
        "可出战角色人数未达上限",
        "是否确认出战",
        "本局不再提示"
    ];

    public static readonly string[] StrategyConfirmAliases =
    [
        "确认",
        "确定"
    ];

    public static readonly string[] StrategyScreenAliases =
    [
        "请选择投资策略",
        "刷新次数",
        "返回备战界面"
    ];

    public static readonly string[] StrategyChoiceBlacklist =
    [
        "远见",
        "黄金投资",
        "白银投资"
    ];

    public const double DragPauseSeconds = 0.25;
    public const double AfterBattleClickSeconds = 10.0;
    public const double AfterBattleRetryWaitSeconds = 6.0;
    public const double BoardWaitTimeoutSeconds = 300.0;
    public const double StrategyScreenDelaySeconds = 9.0;
    public const double BeforeInGameDeployDelaySeconds = 5.7;
    public const double StrategyRefreshDelaySeconds = 0.5;
    public const int InitialStrategyScanAttemptCount = 2;
    public const int PostRefreshStrategyScanAttemptCount = 1;
    public const int ExtraRightStrategyRefreshCount = 2;
    public const double StrategyScanIntervalSeconds = 0.2;
    public const int PresetInvestmentScanAttemptCount = 3;
    public const int BattleButtonClickCount = 3;
    public const double BattleButtonClickIntervalSeconds = 0.4;
    public const int PresetInvestmentFuzzyScore = 88;
    public const int StrategyFuzzyScore = 83;
}
