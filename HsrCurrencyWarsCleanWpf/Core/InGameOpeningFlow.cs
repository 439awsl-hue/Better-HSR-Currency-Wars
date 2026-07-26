namespace HsrCurrencyWarsCleanWpf.Core;

public static class InGameOpeningFlow
{
	public static readonly RatioPoint[] PrepareSlots = new RatioPoint[4]
	{
		new RatioPoint(0.229, 0.846),
		new RatioPoint(0.294, 0.845),
		new RatioPoint(0.359, 0.846),
		new RatioPoint(0.425, 0.843)
	};

	public static readonly RatioPoint[] ForwardSlots = new RatioPoint[4]
	{
		new RatioPoint(0.388, 0.367),
		new RatioPoint(0.462, 0.368),
		new RatioPoint(0.537, 0.368),
		new RatioPoint(0.609, 0.37)
	};

	public static readonly RatioPoint BattleButton = new RatioPoint(0.952, 0.694);

	public static readonly RatioPoint ContinueFallbackPoint = new RatioPoint(0.5, 0.58);

	public static readonly RatioPoint StrategyConfirmPoint = new RatioPoint(0.5, 0.91);

	public static readonly RatioPoint GalaStarConfirmPoint = new RatioPoint(0.775, 0.523);

	public static readonly RatioPoint PartnerChoicePoint = new RatioPoint(0.545, 0.277);

	public static readonly RatioPoint PartnerConfirmPoint = new RatioPoint(0.777, 0.551);

	public static readonly RatioPoint HolyGrailConfirmPoint = new RatioPoint(0.78, 0.593);

	public static readonly RatioPoint UnderfilledDoNotRemindPoint = new RatioPoint(0.463, 0.56);

	public static readonly RatioPoint UnderfilledConfirmPoint = new RatioPoint(0.612, 0.622);

	public static readonly RatioRegion BattleButtonRegion = new RatioRegion(0.84, 0.62, 0.16, 0.22);

	public static readonly RatioRegion DialogRegion = new RatioRegion(0.2, 0.04, 0.7, 0.65);

	public static readonly RatioRegion RoleChoicePopupTitleRegion = new RatioRegion(0.34, 0.03, 0.42, 0.12);

	public static readonly RatioRegion StrategyRegion = new RatioRegion(0.0, 0.08, 1.0, 0.66);

	public static readonly RatioPoint[] GalaStarChoices = new RatioPoint[2]
	{
		new RatioPoint(0.485, 0.255),
		new RatioPoint(0.61, 0.255)
	};

	public static readonly RatioPoint[] HolyGrailChoices = new RatioPoint[2]
	{
		new RatioPoint(0.357, 0.33),
		new RatioPoint(0.734, 0.33)
	};

	public static readonly RatioPoint[] StrategyRefreshButtons = new RatioPoint[3]
	{
		new RatioPoint(0.203, 0.795),
		new RatioPoint(0.5, 0.795),
		new RatioPoint(0.724, 0.795)
	};

	public static readonly RatioPoint[] StrategyCards = new RatioPoint[3]
	{
		new RatioPoint(0.24, 0.455),
		new RatioPoint(0.5, 0.455),
		new RatioPoint(0.76, 0.455)
	};

	public static readonly string[] BattleButtonAliases = new string[2] { "出战", "跳过" };

	public static readonly string[] ContinueButtonAliases = new string[6] { "点击空白处继续", "下一步", "下一页", "继续挑战", "前往结算", "确认" };

	public static readonly string[] TargetStrategyAliases = new string[1] { "本姑娘就是罗刹" };

	public static readonly string[] ReincarnationStrategyAliases = new string[1] { "轮回不止" };

	public static readonly string[] FlyingLightStrategyAliases = new string[2] { "飞光·映月", "飞光映月" };

	public static readonly string[] SandGoldStrategyAliases = new string[1] { "砂里淘金" };

	public static readonly string[] PrismInvestmentGateAliases = new string[5] { "彩虹时代", "银·金·彩", "银金彩", "银 金 彩", "头彩" };

	public static readonly string[] ExtraStrategyRefreshInvestmentAliases = new string[3] { "银·金·彩", "银金彩", "银 金 彩" };

	public static readonly string[] LongTermGoodInvestmentGateAliases = new string[2] { "长线利好", "轮岗" };

	public static readonly string[] GalaStarAliases = new string[1] { "盛会之星" };

	public static readonly string[] PartnerChoiceAliases = new string[1] { "选择伙伴" };

	public static readonly string[] HolyGrailChoiceAliases = new string[1] { "祈愿试炼" };

	public static readonly string[] UnderfilledTeamAliases = new string[3] { "可出战角色人数未达上限", "是否确认出战", "本局不再提示" };

	public static readonly string[] StrategyConfirmAliases = new string[2] { "确认", "确定" };

	public static readonly string[] StrategyScreenAliases = new string[3] { "请选择投资策略", "刷新次数", "返回备战界面" };

	public static readonly string[] StrategyChoiceBlacklist = new string[3] { "远见", "黄金投资", "白银投资" };

	public const double DragPauseSeconds = 0.25;

	public const double AfterBattleClickSeconds = 10.0;

	public const double AfterBattleRetryWaitSeconds = 6.0;

	public const double BoardWaitTimeoutSeconds = 300.0;

	public const double StrategyScreenDelaySeconds = 10.5;

	public const double BeforeInGameDeployDelaySeconds = 5.7;

	public const double GalaStarWaitTimeoutSeconds = 1.2;

	public const double GalaStarScanIntervalSeconds = 0.3;

	public const double StrategyRefreshDelaySeconds = 1.0;

	public const int InitialStrategyScanAttemptCount = 2;

	public const int PostRefreshStrategyScanAttemptCount = 1;

	public const int ExtraRightStrategyRefreshCount = 2;

	public const double StrategyScanIntervalSeconds = 0.1;

	public const int PresetInvestmentScanAttemptCount = 3;

	public const double PresetInvestmentPostSafeChoiceDelaySeconds = 2.8;

	public const double PresetInvestmentRefreshDelaySeconds = 0.15;

	public const double PresetInvestmentRecheckIntervalSeconds = 0.08;

	public const int BattleButtonClickCount = 3;

	public const double BattleButtonClickIntervalSeconds = 0.4;

	public const int PresetInvestmentFuzzyScore = 88;

	public const int StrategyFuzzyScore = 83;
}
