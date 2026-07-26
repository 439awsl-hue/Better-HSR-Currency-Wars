using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HsrCurrencyWarsCleanWpf.Core;
using HsrCurrencyWarsCleanWpf.Services;

namespace HsrCurrencyWarsCleanWpf;

public partial class MainWindow : Window, IComponentConnector
{
	private struct NativeRect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private struct PointStruct(int x, int y)
	{
		public int X = x;

		public int Y = y;
	}

	private const int HotkeyStopId = 1001;

	private const int WmHotkey = 786;

	private const uint ModNoRepeat = 16384u;

	private const uint VkF8 = 119u;

	private const int DwmwaWindowCornerPreference = 33;

	private const int DwmWindowCornerPreferenceRound = 2;

	private const int WeeklyPointsGoal = 18000;

	private static readonly RatioPoint WeeklyHomeSafePoint = new RatioPoint(0.94, 0.8);

	private static readonly RatioRegion WeeklyPointsRegion = new RatioRegion(0.018, 0.865, 0.205, 0.085);

	private readonly ConfigStore _configStore = new ConfigStore(AppContext.BaseDirectory);

	private readonly WindowCaptureService _windowCapture = new WindowCaptureService();

	private readonly IOcrService _ocrService = CreateOcrService();

	private readonly ScanEvaluator _scanEvaluator = new ScanEvaluator();

	private readonly IClickService _clickService = new MouseClickService();

	private AutomationConfig _config = new AutomationConfig();

	private GameWindowInfo? _gameWindow;

	private BitmapSource? _latestPreviewImage;

	private WindowClientRect? _latestCaptureScreenRegion;

	private OcrScanResult? _latestOcrResult;

	private CaptureRegion _latestPreviewRegion = CaptureRegion.FullWindow;

	private RatioPoint? _lastSafeInvestmentPoint;

	private bool _blockedHitThisCycle;

	private CancellationTokenSource? _automationCts;

	private bool _automationSuccessStop;

	private HwndSource? _hotkeySource;

	private readonly GameLogOverlayWindow _gameLogOverlay = new GameLogOverlayWindow();

	private readonly DispatcherTimer _gameLogOverlayTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(180L)
	};

	public MainWindow()
	{
		InitializeComponent();
		LoadConfigToUi();
		InitializeFlowList();
		AppendStartupNotice();
		AppendLog("程序已启动。");
		AppendLog("配置文件：" + _configStore.ConfigPath);
		AppendLog("第 6 阶段已启用：完整自动刷新闭环，返回货币战争后继续循环。");
		AppendLog("OCR 状态：" + _ocrService.Name);
		AppendLog("点击/按键语义对齐旧 Python 稳定版：流程中直接执行输入。");
		SetStatus("状态：未运行");
		HighlightNav(HomeNav);
		_gameLogOverlayTimer.Tick += GameLogOverlayTimer_Tick;
		_gameLogOverlayTimer.Start();
		base.SourceInitialized += MainWindow_SourceInitialized;
		base.Loaded += MainWindow_Loaded;
		base.Closed += MainWindow_Closed;
	}

	private void MainWindow_SourceInitialized(object? sender, EventArgs e)
	{
		ApplySystemWindowEffects();
	}

	private void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		RegisterHotkeys();
		CheckForUpdatesAsync(showNoUpdateMessage: false);
	}

	private void MainWindow_Closed(object? sender, EventArgs e)
	{
		_automationCts?.Cancel();
		_gameLogOverlayTimer.Stop();
		_gameLogOverlay.Close();
		UnregisterHotkeys();
		if (_ocrService is IDisposable disposable)
		{
			disposable.Dispose();
		}
	}

	private void ReloadConfig_Click(object sender, RoutedEventArgs e)
	{
		LoadConfigToUi();
		AppendLog("已重新加载配置。");
	}

	private void SaveConfig_Click(object sender, RoutedEventArgs e)
	{
		ReadUiToConfig();
		_configStore.Save(_config);
		LoadConfigToUi();
		AppendLog("配置已保存。");
		AppendLog($"配置词条：主词条 {_config.TargetWords.Count} 个，不想要 {_config.BlockedWords.Count} 个，投资 {_config.InvestmentTargets.Count} 个。");
	}

	private void FindWindow_Click(object sender, RoutedEventArgs e)
	{
		TryFindWindow();
	}

	private void GameLogOverlayCheckBox_Changed(object sender, RoutedEventArgs e)
	{
		if (GameLogOverlayCheckBox.IsChecked != true)
		{
			_gameLogOverlay.HideOverlay();
		}
	}

	private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
	{
		await CheckForUpdatesAsync(showNoUpdateMessage: true);
	}

	private void ShowDonation_Click(object sender, RoutedEventArgs e)
	{
		DonationOverlay.Visibility = Visibility.Visible;
	}

	private void CloseDonation_Click(object sender, RoutedEventArgs e)
	{
		DonationOverlay.Visibility = Visibility.Collapsed;
	}

	private void DonationOverlay_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource == sender)
		{
			DonationOverlay.Visibility = Visibility.Collapsed;
		}
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			ToggleWindowState();
			return;
		}
		try
		{
			DragMove();
		}
		catch
		{
		}
	}

	private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void ToggleMaximizeWindow_Click(object sender, RoutedEventArgs e)
	{
		ToggleWindowState();
	}

	private void CloseWindow_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ToggleWindowState()
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void ApplySystemWindowEffects()
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			int cornerPreference = 2;
			DwmSetWindowAttribute(handle, 33, ref cornerPreference, Marshal.SizeOf<int>());
		}
		catch
		{
		}
	}

	private void Nav_Click(object sender, RoutedEventArgs e)
	{
		if (sender is Button { Tag: string pageName })
		{
			HomePage.Visibility = ((!(pageName == "HomePage")) ? Visibility.Collapsed : Visibility.Visible);
			OutGamePage.Visibility = ((!(pageName == "OutGamePage")) ? Visibility.Collapsed : Visibility.Visible);
			InGamePage.Visibility = ((!(pageName == "InGamePage")) ? Visibility.Collapsed : Visibility.Visible);
			AutoBattlePage.Visibility = ((!(pageName == "AutoBattlePage")) ? Visibility.Collapsed : Visibility.Visible);
			LogPage.Visibility = ((!(pageName == "LogPage")) ? Visibility.Collapsed : Visibility.Visible);
			HelpPage.Visibility = ((!(pageName == "HelpPage")) ? Visibility.Collapsed : Visibility.Visible);
			HighlightNav((Button)sender);
		}
	}

	private void ShowLogPage()
	{
		HomePage.Visibility = Visibility.Collapsed;
		OutGamePage.Visibility = Visibility.Collapsed;
		InGamePage.Visibility = Visibility.Collapsed;
		AutoBattlePage.Visibility = Visibility.Collapsed;
		LogPage.Visibility = Visibility.Visible;
		HelpPage.Visibility = Visibility.Collapsed;
		HighlightNav(LogNav);
	}

	private void HighlightNav(Button active)
	{
		Button[] array = new Button[6] { HomeNav, OutGameNav, InGameNav, AutoBattleNav, LogNav, HelpNav };
		foreach (Button obj in array)
		{
			obj.Background = ((obj == active) ? ((Brush)FindResource("NavActiveBrush")) : Brushes.Transparent);
			obj.Foreground = ((obj == active) ? ((Brush)FindResource("NavTextBrush")) : ((Brush)FindResource("MutedBrush")));
		}
	}

	private void CaptureFullWindow_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.FullWindow);
	}

	private void CaptureTopHalf_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.TopHalf);
	}

	private void CaptureBottomHalf_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.BottomHalf);
	}

	private void CaptureLeftBottom_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.LeftBottom);
	}

	private void CaptureRightBottom_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.RightBottom);
	}

	private async void RunOcr_Click(object sender, RoutedEventArgs e)
	{
		await RunOcrOnLatestPreviewAsync();
	}

	private async void TestDebuffOcr_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.BottomHalf);
		await RunOcrOnLatestPreviewAsync();
	}

	private async void TestFullWindowOcr_Click(object sender, RoutedEventArgs e)
	{
		CapturePreview(CaptureRegion.FullWindow);
		await RunOcrOnLatestPreviewAsync();
	}

	private async void ClickWindowCenter_Click(object sender, RoutedEventArgs e)
	{
		await ClickWindowCenterAsync();
	}

	private async void ClickOcrText_Click(object sender, RoutedEventArgs e)
	{
		await ClickOcrTextAsync();
	}

	private async void StartAutomation_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		await StartAutomationAsync();
	}

	private async void StartLuochaPreset_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		await StartIndependentStrategyPresetAsync("本姑娘就是罗刹", InGameOpeningFlow.TargetStrategyAliases, InGameOpeningFlow.PrismInvestmentGateAliases);
	}

	private async void StartReincarnationPreset_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		await StartIndependentStrategyPresetAsync("轮回不止", InGameOpeningFlow.ReincarnationStrategyAliases, InGameOpeningFlow.PrismInvestmentGateAliases);
	}

	private async void StartFlyingLightPreset_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		await StartIndependentStrategyPresetAsync("飞光·映月", InGameOpeningFlow.FlyingLightStrategyAliases, InGameOpeningFlow.PrismInvestmentGateAliases);
	}

	private async void StartSandGoldPreset_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		await StartIndependentStrategyPresetAsync("砂里淘金", InGameOpeningFlow.SandGoldStrategyAliases, InGameOpeningFlow.LongTermGoodInvestmentGateAliases);
	}

	private async void StartCustomInGame_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		List<string> strategies = InGameStrategyListBox.Items.OfType<string>().ToList();
		List<string> investments = InGameInvestmentListBox.Items.OfType<string>().ToList();
		if (strategies.Count == 0 || investments.Count == 0)
		{
			MessageBox.Show(this, "请至少添加 1 个局内投资目标和 1 个局内策略目标。", "局内自定义", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		RefreshWordHistoryControls();
		await StartIndependentStrategyPresetAsync(string.Join("、", strategies), strategies, investments);
	}

	private async void StartWeeklyPoints_Click(object sender, RoutedEventArgs e)
	{
		ShowLogPage();
		await StartWeeklyPointsAsync();
	}

	private void StopAutomation_Click(object sender, RoutedEventArgs e)
	{
		StopAutomation();
	}

	private void TargetWordInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddTargetWord_Click(sender, e);
			e.Handled = true;
		}
	}

	private void BlockedWordInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddBlockedWord_Click(sender, e);
			e.Handled = true;
		}
	}

	private void InvestmentWordInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddInvestmentWord_Click(sender, e);
			e.Handled = true;
		}
	}

	private void AddTargetWord_Click(object sender, RoutedEventArgs e)
	{
		AddWord(TargetWordInputBox, TargetWordsListBox, GetTargetWordLimit(), "主词条");
	}

	private void DeleteSelectedTargetWord_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(TargetWordsListBox, "主词条");
	}

	private void ClearTargetWords_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(TargetWordsListBox, "主词条");
	}

	private void AddBlockedWord_Click(object sender, RoutedEventArgs e)
	{
		AddWord(BlockedWordInputBox, BlockedWordsListBox, 20, "不想要词条");
	}

	private void DeleteSelectedBlockedWord_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(BlockedWordsListBox, "不想要词条");
	}

	private void ClearBlockedWords_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(BlockedWordsListBox, "不想要词条");
	}

	private void AddInvestmentWord_Click(object sender, RoutedEventArgs e)
	{
		AddWord(InvestmentWordInputBox, InvestmentWordsListBox, 20, "投资词条");
	}

	private void DeleteSelectedInvestmentWord_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(InvestmentWordsListBox, "投资词条");
	}

	private void ClearInvestmentWords_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(InvestmentWordsListBox, "投资词条");
	}

	private void AddInGameInvestment_Click(object sender, RoutedEventArgs e)
	{
		AddWord(InGameInvestmentInputBox, InGameInvestmentListBox, 20, "局内投资");
	}

	private void DeleteSelectedInGameInvestment_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(InGameInvestmentListBox, "局内投资");
	}

	private void ClearInGameInvestment_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(InGameInvestmentListBox, "局内投资");
	}

	private void AddInGameStrategy_Click(object sender, RoutedEventArgs e)
	{
		AddWord(InGameStrategyInputBox, InGameStrategyListBox, 20, "局内策略");
	}

	private void DeleteSelectedInGameStrategy_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(InGameStrategyListBox, "局内策略");
	}

	private void ClearInGameStrategy_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(InGameStrategyListBox, "局内策略");
	}

	private void DebuffMatchAny_Changed(object sender, RoutedEventArgs e)
	{
		if (base.IsLoaded)
		{
			SaveConfigFromUi("主词条命中模式已更新。");
		}
	}

	private void CheckInvestmentWhenBlocked_Changed(object sender, RoutedEventArgs e)
	{
		if (base.IsLoaded)
		{
			SaveConfigFromUi("命中不想要后投资检查开关已更新。");
		}
	}

	private void LoadConfigToUi()
	{
		_config = _configStore.Load();
		WindowTitleBox.Text = _config.WindowTitle;
		DebuffEnabledBox.IsChecked = _config.DebuffEnabled;
		DebuffMatchAnyBox.IsChecked = _config.DebuffMatchAny;
		SetListBoxItems(TargetWordsListBox, _config.TargetWords);
		BlockedEnabledBox.IsChecked = _config.BlockedEnabled;
		SetListBoxItems(BlockedWordsListBox, _config.BlockedWords);
		InvestmentEnabledBox.IsChecked = true;
		CheckInvestmentWhenBlockedBox.IsChecked = _config.CheckInvestmentWhenBlocked;
		SetListBoxItems(InvestmentWordsListBox, _config.InvestmentTargets);
		SetListBoxItems(InGameStrategyListBox, _config.InGameStrategyTargets);
		SetListBoxItems(InGameInvestmentListBox, _config.InGameInvestmentTargets);
		RefreshWordHistoryControls();
		UpdateWordCounts();
		ApplyEvaluationSummary(null);
	}

	private void ReadUiToConfig()
	{
		_config.WindowTitle = WindowTitleBox.Text.Trim();
		_config.DebuffEnabled = DebuffEnabledBox.IsChecked == true;
		_config.DebuffMatchAny = DebuffMatchAnyBox.IsChecked == true;
		_config.TargetWords = ReadWords(TargetWordsListBox);
		_config.BlockedEnabled = BlockedEnabledBox.IsChecked == true;
		_config.BlockedWords = ReadWords(BlockedWordsListBox);
		_config.InvestmentEnabled = true;
		_config.InvestmentTargets = ReadWords(InvestmentWordsListBox);
		_config.CheckInvestmentWhenBlocked = CheckInvestmentWhenBlockedBox.IsChecked == true;
		_config.InGameStrategyTargets = ReadWords(InGameStrategyListBox);
		_config.InGameInvestmentTargets = ReadWords(InGameInvestmentListBox);
		_config.InGameStrategyTarget = "";
		_config.InGameInvestmentTarget = "";
		_config.Normalize();
		UpdateWordCounts();
		SetListBoxItems(TargetWordsListBox, _config.TargetWords);
		SetListBoxItems(BlockedWordsListBox, _config.BlockedWords);
		SetListBoxItems(InvestmentWordsListBox, _config.InvestmentTargets);
		SetListBoxItems(InGameStrategyListBox, _config.InGameStrategyTargets);
		SetListBoxItems(InGameInvestmentListBox, _config.InGameInvestmentTargets);
		RefreshWordHistoryControls();
	}

	private void UpdateWordCounts()
	{
		int targetLimit = (_config.DebuffMatchAny ? 20 : 4);
		string mode = (_config.DebuffMatchAny ? "任意" : "全部");
		TargetWordsCountText.Text = $"{mode} {_config.TargetWords.Count} / 上限 {targetLimit}";
		BlockedWordsCountText.Text = $"不想要 {_config.BlockedWords.Count} / 上限 {20}";
		InvestmentWordsCountText.Text = $"投资 {_config.InvestmentTargets.Count} / 上限 {20}";
		InGameInvestmentCountText.Text = $"{_config.InGameInvestmentTargets.Count} / {20}";
		InGameStrategyCountText.Text = $"{_config.InGameStrategyTargets.Count} / {20}";
	}

	private static List<string> ReadWords(ListBox listBox)
	{
		return (from word in listBox.Items.OfType<string>()
			select word.Trim() into word
			where !string.IsNullOrWhiteSpace(word)
			select word).ToList();
	}

	private static void SetListBoxItems(ListBox listBox, IEnumerable<string> words)
	{
		listBox.Items.Clear();
		foreach (string word in words.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			listBox.Items.Add(word);
		}
	}

	private static void SetComboBoxItems(ComboBox comboBox, IEnumerable<string> words)
	{
		string currentText = comboBox.Text;
		comboBox.Items.Clear();
		foreach (string word in words.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct<string>(StringComparer.OrdinalIgnoreCase))
		{
			comboBox.Items.Add(word);
		}
		comboBox.Text = currentText;
	}

	private void RefreshWordHistoryControls()
	{
		SetComboBoxItems(TargetWordInputBox, _config.TargetWordHistory);
		SetComboBoxItems(BlockedWordInputBox, _config.BlockedWordHistory);
		SetComboBoxItems(InvestmentWordInputBox, _config.InvestmentWordHistory);
		SetComboBoxItems(InGameStrategyInputBox, _config.InGameStrategyHistory);
		SetComboBoxItems(InGameInvestmentInputBox, _config.InGameInvestmentHistory);
	}

	private int GetTargetWordLimit()
	{
		if (DebuffMatchAnyBox.IsChecked != true)
		{
			return 4;
		}
		return 20;
	}

	private void AddWord(ComboBox inputBox, ListBox listBox, int limit, string label)
	{
		string word = inputBox.Text.Trim();
		if (!string.IsNullOrWhiteSpace(word))
		{
			if (listBox.Items.OfType<string>().Any((string existing) => string.Equals(existing, word, StringComparison.OrdinalIgnoreCase)))
			{
				inputBox.Text = "";
				AppendLog(label + "已存在：" + word);
			}
			else if (listBox.Items.Count >= limit)
			{
				MessageBox.Show(this, $"{label}最多保存 {limit} 个。", "超过上限", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			else
			{
				listBox.Items.Add(word);
				inputBox.Text = "";
				SaveConfigFromUi(label + "已添加：" + word);
			}
		}
	}

	private void DeleteSelectedWords(ListBox listBox, string label)
	{
		if (listBox.SelectedItems.Count == 0)
		{
			return;
		}
		List<string> selected = listBox.SelectedItems.OfType<string>().ToList();
		foreach (string item in selected)
		{
			listBox.Items.Remove(item);
		}
		SaveConfigFromUi(label + "已删除：" + string.Join("、", selected));
	}

	private void ClearWords(ListBox listBox, string label)
	{
		if (listBox.Items.Count != 0)
		{
			listBox.Items.Clear();
			SaveConfigFromUi(label + "已清空。");
		}
	}

	private void SaveConfigFromUi(string message)
	{
		ReadUiToConfig();
		_configStore.Save(_config);
		UpdateWordCounts();
		RefreshWordHistoryControls();
		AppendLog(message);
		AppendLog($"配置词条：主词条 {_config.TargetWords.Count} 个，不想要 {_config.BlockedWords.Count} 个，投资 {_config.InvestmentTargets.Count} 个。");
	}

	private void InitializeFlowList()
	{
		FlowStepsListBox.Items.Clear();
		int index = 1;
		foreach (FlowStep step in CurrencyWarsFlow.Steps)
		{
			FlowStepsListBox.Items.Add($"{index}. {step.Name}");
			index++;
		}
	}

	private bool TryFindWindow()
	{
		try
		{
			ReadUiToConfig();
			_configStore.Save(_config);
			_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
			WindowClientRect rect = _gameWindow.ClientRect;
			WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={rect.Width}x{rect.Height}  left={rect.Left}, top={rect.Top}";
			SetStatus("状态：已找到窗口");
			AppendLog($"找到窗口：{_gameWindow.Title}，client={rect.Width}x{rect.Height}，left={rect.Left}, top={rect.Top}");
			return true;
		}
		catch (Exception ex)
		{
			_gameWindow = null;
			WindowInfoText.Text = "窗口：未检测";
			SetStatus("状态：找窗口失败");
			AppendLog("找窗口失败：" + ex.Message);
			MessageBox.Show(this, ex.Message, "找窗口失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return false;
		}
	}

	private async Task RefreshGameWindowForIndependentLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
			WindowClientRect rect = _gameWindow.ClientRect;
			WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={rect.Width}x{rect.Height}  left={rect.Left}, top={rect.Top}";
			AppendLog($"独立局内预设：已刷新窗口位置，client={rect.Width}x{rect.Height}，left={rect.Left}, top={rect.Top}。");
			await DelayWithCancellationAsync(0.3, cancellationToken);
		}
		catch (Exception ex)
		{
			AppendLog("独立局内预设：刷新窗口位置失败，继续使用旧窗口位置：" + ex.Message);
		}
	}

	private void RefreshGameWindowForIndependentStep()
	{
		_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
		WindowClientRect rect = _gameWindow.ClientRect;
		WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={rect.Width}x{rect.Height}  left={rect.Left}, top={rect.Top}";
	}

	private void CapturePreview(CaptureRegion region)
	{
		try
		{
			if ((object)_gameWindow != null || TryFindWindow())
			{
				_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
				WindowClientRect resolved = _windowCapture.ResolveRegion(_gameWindow.ClientRect, region);
				BitmapSource image = (_latestPreviewImage = _windowCapture.Capture(_gameWindow, region));
				_latestCaptureScreenRegion = resolved;
				_latestOcrResult = null;
				_latestPreviewRegion = region;
				PreviewImage.Source = image;
				PreviewPlaceholder.Visibility = Visibility.Collapsed;
				CaptureInfoText.Text = $"截图：{region.Name}  {resolved.Width}x{resolved.Height}  left={resolved.Left}, top={resolved.Top}";
				SetStatus("状态：截图完成：" + region.Name);
				AppendLog($"截图完成：{region.Name}，{resolved.Width}x{resolved.Height}，left={resolved.Left}, top={resolved.Top}");
			}
		}
		catch (Exception ex)
		{
			AppendLog("截图失败：" + region.Name + "，" + ex.Message);
			SetStatus("状态：截图失败");
			MessageBox.Show(this, ex.Message, "截图失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task RunOcrOnLatestPreviewAsync()
	{
		try
		{
			if (_latestPreviewImage == null)
			{
				CapturePreview(CaptureRegion.FullWindow);
			}
			if (_latestPreviewImage != null)
			{
				OcrInfoText.Text = "OCR：正在识别 " + _latestPreviewRegion.Name + "...";
				SetStatus("状态：OCR 识别中：" + _latestPreviewRegion.Name);
				AppendLog("OCR 开始：" + _latestPreviewRegion.Name);
				OcrScanResult result = (_latestOcrResult = await _ocrService.RecognizeAsync(_latestPreviewImage));
				ReadUiToConfig();
				BasicScanEvaluation evaluation = _scanEvaluator.Evaluate(_config, result.RawText);
				OcrInfoText.Text = $"OCR：{_latestPreviewRegion.Name}，文本块 {result.Items.Count}，字符 {result.RawText.Length}";
				OcrRawTextBox.Text = FormatOcrResult(_latestPreviewRegion.Name, result);
				EvaluationTextBox.Text = FormatEvaluation(evaluation);
				ApplyEvaluationSummary(evaluation);
				SetStatus("状态：OCR 完成");
				AppendLog($"OCR 完成：{_latestPreviewRegion.Name}，文本块 {result.Items.Count}，字符 {result.RawText.Length}");
				AppendLog($"命中评估：主词条{(evaluation.DebuffSuccess ? "成功" : "未成功")}，命中 {evaluation.TargetMatch.HitWords.Count}，不想要命中 {evaluation.BlockedMatch.HitWords.Count}，投资命中 {evaluation.InvestmentMatch.HitWords.Count}");
			}
		}
		catch (Exception ex)
		{
			OcrInfoText.Text = "OCR：失败";
			SetStatus("状态：OCR 失败");
			AppendLog("OCR 失败：" + ex.Message);
			MessageBox.Show(this, ex.Message, "OCR 失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task ClickWindowCenterAsync()
	{
		try
		{
			if (((object)_gameWindow != null || TryFindWindow()) && (object)_gameWindow != null)
			{
				WindowClientRect rect = _gameWindow.ClientRect;
				ClickRequest request = new ClickRequest("窗口中心", rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
				await ExecuteClickAsync(request);
			}
		}
		catch (Exception ex)
		{
			AppendLog("点击窗口中心失败：" + ex.Message);
			MessageBox.Show(this, ex.Message, "点击失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task ClickOcrTextAsync()
	{
		_ = 1;
		try
		{
			if (_latestPreviewImage == null)
			{
				CapturePreview(CaptureRegion.FullWindow);
			}
			if ((object)_latestOcrResult == null)
			{
				await RunOcrOnLatestPreviewAsync();
			}
			if ((object)_latestOcrResult != null && (object)_latestCaptureScreenRegion != null)
			{
				List<string> aliases = (from value in ClickTextBox.Text.Split(new char[8] { '/', '／', ',', '，', ';', '；', '|', '｜' }, StringSplitOptions.RemoveEmptyEntries)
					select value.Trim() into value
					where !string.IsNullOrWhiteSpace(value)
					select value).ToList();
				OcrClickCandidate candidate = OcrClickResolver.FindBest(_latestOcrResult, aliases, _config.ButtonFuzzyScore);
				if ((object)candidate == null)
				{
					string text = string.Join(" / ", aliases);
					AppendLog("OCR 点击未命中：" + text);
					MessageBox.Show(this, "当前 OCR 结果里没有找到：" + text, "OCR 点击未命中", MessageBoxButton.OK, MessageBoxImage.Asterisk);
					return;
				}
				Rect bounds = candidate.Item.Bounds;
				ClickRequest request = new ClickRequest($"OCR 文字：{candidate.Item.Text}（匹配 {candidate.Alias}）", _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0));
				await ExecuteClickAsync(request);
			}
		}
		catch (Exception ex)
		{
			AppendLog("OCR 文字点击失败：" + ex.Message);
			MessageBox.Show(this, ex.Message, "点击失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private async Task ExecuteClickAsync(ClickRequest request)
	{
		if ((object)_gameWindow != null || TryFindWindow())
		{
			AppendLog((await _clickService.ClickAsync(request, _gameWindow?.Handle ?? IntPtr.Zero)).Message);
		}
	}

	private async Task ExecuteDragRatioAsync(RatioPoint start, RatioPoint end, string reason, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if ((object)_gameWindow == null && !TryFindWindow())
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
		WindowClientRect rect = _gameWindow.ClientRect;
		DragRequest request = new DragRequest(reason, rect.Left + (int)Math.Round((double)rect.Width * start.X), rect.Top + (int)Math.Round((double)rect.Height * start.Y), rect.Left + (int)Math.Round((double)rect.Width * end.X), rect.Top + (int)Math.Round((double)rect.Height * end.Y));
		AppendLog((await _clickService.DragAsync(request, _gameWindow.Handle, cancellationToken)).Message);
	}

	private void SetAutomationButtonsEnabled(bool enabled)
	{
		StartAutoButton.IsEnabled = enabled;
		StartLuochaPresetButton.IsEnabled = enabled;
		StartReincarnationPresetButton.IsEnabled = enabled;
		StartFlyingLightPresetButton.IsEnabled = enabled;
		StartSandGoldPresetButton.IsEnabled = enabled;
		StartCustomInGameButton.IsEnabled = enabled;
		StartWeeklyPointsButton.IsEnabled = enabled;
		StopAutoButton.IsEnabled = !enabled;
	}

	private async Task StartWeeklyPointsAsync()
	{
		if (_automationCts != null)
		{
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		if ((object)_gameWindow == null && !TryFindWindow())
		{
			return;
		}
		_automationCts = new CancellationTokenSource();
		_automationSuccessStop = false;
		_lastSafeInvestmentPoint = null;
		SetAutomationButtonsEnabled(enabled: false);
		SetStatus("状态：自动刷周常积分运行中");
		try
		{
			await RunWeeklyPointsLoopAsync(_automationCts.Token);
		}
		catch (OperationCanceledException)
		{
			AppendLog(_automationSuccessStop ? "自动刷周常积分：积分已满，成功停止。" : "自动刷周常积分：已手动停止。");
			SetStatus(_automationSuccessStop ? "状态：周常积分已满" : "状态：已停止");
		}
		catch (Exception ex2)
		{
			AppendLog("自动刷周常积分失败：" + ex2.Message);
			SetStatus("状态：自动刷周常积分失败");
			MessageBox.Show(this, ex2.Message, "自动刷周常积分失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_automationCts?.Dispose();
			_automationCts = null;
			SetAutomationButtonsEnabled(enabled: true);
			if (!_automationSuccessStop && StatusText.Text == "状态：自动刷周常积分运行中")
			{
				SetStatus("状态：已停止");
			}
		}
	}

	private async Task RunWeeklyPointsLoopAsync(CancellationToken cancellationToken)
	{
		AppendLog($"自动刷周常积分：启动缓冲 1 秒，当前积分 >= {18000} 时停止。");
		await DelayWithCancellationAsync(1.0, cancellationToken);
		int round = 1;
		while (!cancellationToken.IsCancellationRequested)
		{
			AppendLog($"自动刷周常积分：第 {round} 轮开始。");
			await RefreshGameWindowForIndependentLoopAsync(cancellationToken);
			await ClickWeeklyHomeSafeAreaAsync(cancellationToken);
			int? points = await TryReadWeeklyPointsAsync(cancellationToken);
			if (points.HasValue)
			{
				AppendLog($"自动刷周常积分：当前积分 {points.Value}/{18000}。");
				if (points.Value >= 18000)
				{
					_automationSuccessStop = true;
					SetStatus("状态：周常积分已满");
					AppendLog("自动刷周常积分：积分已满，停止。");
					break;
				}
			}
			else
			{
				AppendLog("自动刷周常积分：首页积分 OCR 未解析，继续执行一轮。");
			}
			await RunWeeklyPointsRoundAsync(cancellationToken);
			AppendLog($"自动刷周常积分：第 {round} 轮完成，返回首页继续检查积分。");
			round++;
		}
	}

	private async Task ClickWeeklyHomeSafeAreaAsync(CancellationToken cancellationToken)
	{
		AppendLog("自动刷周常积分：首页安全点击 3 次后识别积分。");
		for (int i = 0; i < 3; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(WeeklyHomeSafePoint, $"自动刷周常积分：首页安全点击 {i + 1}/3", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
		}
	}

	private async Task<int?> TryReadWeeklyPointsAsync(CancellationToken cancellationToken)
	{
		OcrScanResult scan = await CaptureAndOcrAsync(WeeklyPointsRegion, cancellationToken);
		AppendLog("自动刷周常积分：首页积分 OCR：" + ShortText(scan.RawText));
		Match match = Regex.Match(Regex.Replace(scan.RawText, "\\s+", ""), "(?<current>\\d{1,6})/(?<total>\\d{1,6})");
		if (match.Success && int.TryParse(match.Groups["current"].Value, out var current))
		{
			return current;
		}
		return null;
	}

	private async Task RunWeeklyPointsRoundAsync(CancellationToken cancellationToken)
	{
		await RunIndependentOuterFlowBeforeInvestmentAsync(cancellationToken);
		await DelayWithCancellationAsync(0.4, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickRatioPointAsync(new RatioPoint(0.565, 0.91), "自动刷周常积分：固定确认", cancellationToken);
		await DelayWithCancellationAsync(0.2, cancellationToken);
		await DelayWithCancellationAsync(5.7, cancellationToken);
		await DeployOpeningCharactersAsync(cancellationToken);
		await TryHandleGalaStarChoiceAsync(cancellationToken);
		await RunOpeningBattlesUntilTwoContinueClicksAsync(cancellationToken);
		await RunWeeklyStrategyChoiceAsync(cancellationToken);
		await RunIndependentReturnToCurrencyWarsAsync(cancellationToken);
	}

	private async Task RunWeeklyStrategyChoiceAsync(CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(10.5, cancellationToken);
		AppendLog("自动刷周常积分：策略页复用局内识别默认黑名单逻辑。");
		if (!(await IsStrategySelectionScreenAsync(cancellationToken)))
		{
			AppendLog("自动刷周常积分：当前不是策略选择界面，跳过策略确认。");
			return;
		}
		await ClickRandomStrategyCardAsync(cancellationToken);
		await ClickStrategyConfirmAsync(cancellationToken);
	}

	private async Task StartIndependentStrategyPresetAsync(string strategyName, IReadOnlyList<string> strategyAliases, IReadOnlyList<string> investmentGateAliases)
	{
		if (_automationCts != null)
		{
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		if ((object)_gameWindow == null && !TryFindWindow())
		{
			return;
		}
		_automationCts = new CancellationTokenSource();
		_automationSuccessStop = false;
		_lastSafeInvestmentPoint = null;
		SetAutomationButtonsEnabled(enabled: false);
		SetStatus("状态：独立局内预设运行中：" + strategyName);
		try
		{
			await RunIndependentStrategyPresetLoopAsync(strategyName, strategyAliases, investmentGateAliases, _automationCts.Token);
		}
		catch (OperationCanceledException)
		{
			AppendLog(_automationSuccessStop ? "独立局内预设：成功停止。" : "独立局内预设：已手动停止。");
			SetStatus(_automationSuccessStop ? "状态：局内策略命中停止" : "状态：已停止");
			if (_automationSuccessStop)
			{
				MessageBox.Show(this, "成功刷出目标策略：" + strategyName + "！", "成功", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception ex2)
		{
			AppendLog("独立局内预设失败：" + ex2.Message);
			SetStatus("状态：独立局内预设失败");
			MessageBox.Show(this, ex2.Message, "独立局内预设失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_automationCts?.Dispose();
			_automationCts = null;
			SetAutomationButtonsEnabled(enabled: true);
		}
	}

	private async Task RunIndependentStrategyPresetLoopAsync(string strategyName, IReadOnlyList<string> strategyAliases, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
	{
		AppendLog("独立局内预设：启动缓冲 1 秒，固定使用统一速度。");
		await DelayWithCancellationAsync(1.0, cancellationToken);
		int round = 1;
		while (!cancellationToken.IsCancellationRequested)
		{
			AppendLog($"独立局内预设：第 {round} 轮开始，投资门槛：{string.Join("、", investmentGateAliases)}。");
			await RefreshGameWindowForIndependentLoopAsync(cancellationToken);
			await RunIndependentOuterFlowBeforeInvestmentAsync(cancellationToken);
			AppendLog("独立局内预设：检查固定投资门槛。");
			string? investmentGateHit = await ExecuteIndependentInvestmentGateAsync(investmentGateAliases, cancellationToken);
			bool gateHit = !string.IsNullOrWhiteSpace(investmentGateHit);
			bool allowExtraRightStrategyRefresh = IsExtraStrategyRefreshInvestment(investmentGateHit);
			await DelayWithCancellationAsync(0.4, cancellationToken);
			RefreshGameWindowForIndependentStep();
			await ClickRatioPointAsync(new RatioPoint(0.565, 0.91), "独立局内预设：固定确认", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
			if (gateHit)
			{
				AppendLog("独立局内预设：投资门槛命中，等待局内棋盘稳定后进入 1-1 / 1-2。");
				await DelayWithCancellationAsync(5.7, cancellationToken);
				await DeployOpeningCharactersAsync(cancellationToken);
				await TryHandleGalaStarChoiceAsync(cancellationToken);
				await RunOpeningBattlesUntilTwoContinueClicksAsync(cancellationToken);
				await RunStrategyRecognitionAsync(strategyName, strategyAliases, allowExtraRightStrategyRefresh, cancellationToken);
				if (_automationSuccessStop)
				{
					break;
				}
				AppendLog("独立局内预设：本轮策略未命中，退出结算并返回货币战争。");
			}
			else
			{
				AppendLog("独立局内预设：投资门槛未命中，不进入局内，直接退出本轮。");
			}
			await RunIndependentReturnToCurrencyWarsAsync(cancellationToken);
			AppendLog($"独立局内预设：第 {round} 轮完成，继续下一轮。");
			round++;
		}
	}

	private async Task RunIndependentOuterFlowBeforeInvestmentAsync(CancellationToken cancellationToken)
	{
		await IndependentClickTextStepAsync("开始「货币战争」", new global::_003C_003Ez__ReadOnlyArray<string>(new string[3] { "开始货币战争", "开始", "货币战争" }), CurrencyWarsFlow.RightBottom, new RatioPoint(0.82, 0.91), 8.0, 0.8, cancellationToken);
		await IndependentClickTextStepAsync("进入标准博弈", new global::_003C_003Ez__ReadOnlyArray<string>(new string[3] { "进入标准博弈", "开始标准博弈", "标准博弈" }), CurrencyWarsFlow.RightBottom, new RatioPoint(0.82, 0.9), 12.0, 0.7, cancellationToken);
		await IndependentClickTextStepAsync("开始对局", new global::_003C_003Ez__ReadOnlyArray<string>(new string[2] { "开始对局", "对局" }), CurrencyWarsFlow.FullWindow, new RatioPoint(0.88, 0.895), 8.0, 0.6, cancellationToken);
		if (_config.DebuffEnabled)
		{
			await WaitForIndependentDebuffResultAsync(cancellationToken);
		}
		await DelayWithCancellationAsync(0.6, cancellationToken);
		await IndependentClickTextStepAsync("下一步", new _003C_003Ez__ReadOnlySingleElementList<string>("下一步"), CurrencyWarsFlow.FullWindow, new RatioPoint(0.88, 0.895), 8.0, 0.6, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickRatioPointAsync(new RatioPoint(0.5, 0.58), "独立局内预设：点击空白继续", cancellationToken);
		await DelayWithCancellationAsync(1.8, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickSafeInvestmentAsync(rememberChoice: true, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
		await DelayWithCancellationAsync(InGameOpeningFlow.PresetInvestmentPostSafeChoiceDelaySeconds, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickRatioPointAsync(_lastSafeInvestmentPoint ?? new RatioPoint(0.5, 0.38), "独立局内预设：动画后再次点击安全投资", cancellationToken);
	}

	private async Task RunIndependentReturnToCurrencyWarsAsync(CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		await ExecuteFastExitToSettlementAsync(cancellationToken);
		await DelayWithCancellationAsync(0.4, cancellationToken);
		await IndependentClickBottomReturnButtonAsync("下一步", new _003C_003Ez__ReadOnlySingleElementList<string>("下一步"), 8.0, 0.4, cancellationToken);
		await IndependentClickBottomReturnButtonAsync("下一页", new _003C_003Ez__ReadOnlySingleElementList<string>("下一页"), 8.0, 0.5, cancellationToken);
		await IndependentClickBottomReturnButtonAsync("返回货币战争", new global::_003C_003Ez__ReadOnlyArray<string>(new string[2] { "返回货币战争", "返回" }), 8.0, 0.7, cancellationToken);
	}

	private async Task<string?> ExecuteIndependentInvestmentGateAsync(IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		string hitWord = await TryClickIndependentInvestmentTargetAsync("首次投资识别", investmentGateAliases, cancellationToken);
		if (hitWord != null)
		{
			AppendLog("独立局内预设：投资门槛命中：" + hitWord + "。");
			return hitWord;
		}
		AppendLog("独立局内预设：首次投资未命中，检查剩余次数刷新。");
		OcrClickCandidate remaining = OcrClickResolver.FindBest(await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken), new _003C_003Ez__ReadOnlySingleElementList<string>("剩余次数"), _config.ButtonFuzzyScore);
		if ((object)remaining != null && (object)_latestCaptureScreenRegion != null)
		{
			Rect bounds = remaining.Item.Bounds;
			await ExecuteClickAsync(new ClickRequest("独立局内预设：投资刷新：" + remaining.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
			await DelayWithCancellationAsync(0.15, cancellationToken);
			hitWord = await TryClickIndependentInvestmentTargetAsync("刷新后投资识别", investmentGateAliases, cancellationToken);
			if (hitWord != null)
			{
				AppendLog("独立局内预设：投资门槛命中：" + hitWord + "。");
				return hitWord;
			}
		}
		await ClickSafeInvestmentAsync(rememberChoice: false, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
		await ClickBlueOceanFollowupGuardAsync(cancellationToken);
		return null;
	}

	private async Task<string?> TryClickIndependentInvestmentTargetAsync(string scope, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
	{
		AppendLog($"独立局内预设：{scope}开始，固定扫描 {InGameOpeningFlow.PresetInvestmentScanAttemptCount} 次。");
		for (int attempt = 1; attempt <= InGameOpeningFlow.PresetInvestmentScanAttemptCount; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AppendLog($"独立局内预设：{scope}第 {attempt} 次扫描上半屏。");
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
			AppendLog($"独立局内预设：{scope}第 {attempt} 次 OCR 原文：{ShortText(scan.RawText)}");
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, investmentGateAliases, 88);
			if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
			{
				Rect bounds = candidate.Item.Bounds;
				await ExecuteClickAsync(new ClickRequest("独立局内预设：投资门槛：" + candidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
				return candidate.Alias;
			}
			if (attempt < InGameOpeningFlow.PresetInvestmentScanAttemptCount)
			{
				await DelayWithCancellationAsync(0.08, cancellationToken);
			}
		}
		AppendLog("独立局内预设：" + scope + "结束，未命中固定投资门槛。");
		return null;
	}

	private async Task IndependentClickTextStepAsync(string name, IReadOnlyList<string> aliases, RatioRegion searchRegion, RatioPoint? fallbackPoint, double timeoutSeconds, double standardDelaySeconds, CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		string lastText = "";
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(searchRegion, cancellationToken);
			lastText = scan.RawText;
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, aliases, _config.ButtonFuzzyScore);
			if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
			{
				Rect bounds = candidate.Item.Bounds;
				int clickX = _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0);
				int clickY = _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0);
				await ExecuteClickAsync(new ClickRequest("独立局内预设：" + name + "：" + candidate.Item.Text, clickX, clickY));
				await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
				return;
			}
			await DelayWithCancellationAsync(0.6, cancellationToken);
		}
		if ((object)fallbackPoint != null)
		{
			AppendLog("独立局内预设：" + name + " OCR 未命中，使用兜底坐标。最后 OCR：" + ShortText(lastText));
			await ClickRatioPointAsync(fallbackPoint, "独立局内预设：" + name + " 兜底", cancellationToken);
			await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
			return;
		}
		throw new InvalidOperationException("独立局内预设超时：没有找到按钮文字“" + name + "”。最后 OCR：" + ShortText(lastText));
	}

	private async Task IndependentClickBottomReturnButtonAsync(string name, IReadOnlyList<string> aliases, double timeoutSeconds, double standardDelaySeconds, CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		string lastText = "";
		RatioPoint point = new RatioPoint(0.5, 0.829);
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
			lastText = scan.RawText;
			if ((object)OcrClickResolver.FindBest(scan, aliases, _config.ButtonFuzzyScore) != null)
			{
				await ClickRatioPointAsync(point, "独立局内预设：" + name + "固定坐标", cancellationToken);
				await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
				return;
			}
			await DelayWithCancellationAsync(0.15, cancellationToken);
		}
		AppendLog("独立局内预设：" + name + " OCR 未命中，使用底部固定坐标。最后 OCR：" + ShortText(lastText));
		await ClickRatioPointAsync(point, "独立局内预设：" + name + "固定坐标兜底", cancellationToken);
		await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
	}

	private async Task DeployOpeningCharactersAsync(CancellationToken cancellationToken)
	{
		AppendLog("局内识别：固定拖拽底部前 4 个备战席到前台前 4 格。");
		int count = Math.Min(InGameOpeningFlow.PrepareSlots.Length, InGameOpeningFlow.ForwardSlots.Length);
		for (int i = 0; i < count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ExecuteDragRatioAsync(InGameOpeningFlow.PrepareSlots[i], InGameOpeningFlow.ForwardSlots[i], $"局内识别：备战席 {i + 1} -> 前台 {i + 1}", cancellationToken);
			await DelayWithCancellationAsync(0.25, cancellationToken);
		}
	}

	private async Task TryHandleGalaStarChoiceAsync(CancellationToken cancellationToken)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(1.2);
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (await TryHandleRoleChoicePopupAsync(await CaptureAndOcrAsync(InGameOpeningFlow.DialogRegion, cancellationToken), cancellationToken))
			{
				return;
			}
			await DelayWithCancellationAsync(0.3, cancellationToken);
		}
		AppendLog("局内识别：未检测到盛会之星、选择伙伴或圣杯选择弹窗。");
	}

	private async Task<bool> TryHandleRoleChoicePopupAsync(OcrScanResult scan, CancellationToken cancellationToken)
	{
		if (IsRoleChoicePopupTitle(scan, InGameOpeningFlow.PartnerChoiceAliases))
		{
			AppendLog("局内识别：检测到选择伙伴弹窗，选择中央伙伴。");
			await ClickRatioPointAsync(InGameOpeningFlow.PartnerChoicePoint, "局内识别：选择伙伴候选", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
			await ClickRatioPointAsync(InGameOpeningFlow.PartnerConfirmPoint, "局内识别：选择伙伴确认选择", cancellationToken);
			await DelayWithCancellationAsync(0.5, cancellationToken);
			return true;
		}
		if (IsRoleChoicePopupTitle(scan, InGameOpeningFlow.HolyGrailChoiceAliases))
		{
			RatioPoint[] choices = InGameOpeningFlow.HolyGrailChoices;
			int index = Random.Shared.Next(choices.Length);
			AppendLog($"局内识别：检测到圣杯选择（祈愿试炼）弹窗，随机选择候选 {index + 1}。");
			await ClickRatioPointAsync(choices[index], $"局内识别：圣杯选择候选 {index + 1}", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
			await ClickRatioPointAsync(InGameOpeningFlow.HolyGrailConfirmPoint, "局内识别：圣杯选择确认", cancellationToken);
			await DelayWithCancellationAsync(0.5, cancellationToken);
			return true;
		}
		if (IsRoleChoicePopupTitle(scan, InGameOpeningFlow.GalaStarAliases))
		{
			RatioPoint[] choices = InGameOpeningFlow.GalaStarChoices;
			int index = Random.Shared.Next(choices.Length);
			AppendLog($"局内识别：检测到盛会之星弹窗，随机选择候选角色 {index + 1}。");
			await ClickRatioPointAsync(choices[index], $"局内识别：盛会之星候选 {index + 1}", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
			await ClickRatioPointAsync(InGameOpeningFlow.GalaStarConfirmPoint, "局内识别：盛会之星确认选择", cancellationToken);
			await DelayWithCancellationAsync(0.5, cancellationToken);
			return true;
		}
		return false;
	}

	private bool IsRoleChoicePopupTitle(OcrScanResult scan, IReadOnlyList<string> aliases)
	{
		if ((object)_gameWindow == null || (object)_latestCaptureScreenRegion == null)
		{
			return false;
		}
		OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, aliases, _config.ButtonFuzzyScore);
		if ((object)candidate == null)
		{
			return false;
		}
		WindowClientRect client = _gameWindow.ClientRect;
		Rect bounds = candidate.Item.Bounds;
		double centerX = (double)_latestCaptureScreenRegion.Left + bounds.X + bounds.Width / 2.0;
		double num = (double)_latestCaptureScreenRegion.Top + bounds.Y + bounds.Height / 2.0;
		double ratioX = (centerX - (double)client.Left) / (double)client.Width;
		double ratioY = (num - (double)client.Top) / (double)client.Height;
		RatioRegion titleRegion = InGameOpeningFlow.RoleChoicePopupTitleRegion;
		if (ratioX >= titleRegion.X && ratioX <= titleRegion.X + titleRegion.Width && ratioY >= titleRegion.Y)
		{
			return ratioY <= titleRegion.Y + titleRegion.Height;
		}
		return false;
	}

	private async Task<bool> ClickInGameBattleButtonAsync(int battleStartCount, CancellationToken cancellationToken)
	{
		int nextCount = battleStartCount + 1;
		AppendLog($"局内识别：准备点击第 {nextCount} 次出战，优先 OCR 查找按钮。");
		OcrClickCandidate candidate = OcrClickResolver.FindBest(await CaptureAndOcrAsync(InGameOpeningFlow.BattleButtonRegion, cancellationToken), InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore);
		if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
		{
			Rect bounds = candidate.Item.Bounds;
			ClickRequest request = new ClickRequest($"局内识别：第 {nextCount} 次{candidate.Item.Text}", _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0));
			await ExecuteRepeatedClickAsync(request, 3, 0.4, cancellationToken);
			await TryHandleUnderfilledTeamConfirmAsync(cancellationToken);
			return true;
		}
		AppendLog($"局内识别：第 {nextCount} 次 OCR 未找到出战按钮，使用固定坐标兜底。");
		await ClickRatioPointRepeatedAsync(InGameOpeningFlow.BattleButton, $"局内识别：第 {nextCount} 次出战兜底", 3, 0.4, cancellationToken);
		await TryHandleUnderfilledTeamConfirmAsync(cancellationToken);
		return true;
	}

	private async Task ExecuteRepeatedClickAsync(ClickRequest request, int count, double intervalSeconds, CancellationToken cancellationToken)
	{
		for (int i = 0; i < count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			string reason = ((count == 1) ? request.Reason : $"{request.Reason} 连点 {i + 1}/{count}");
			await ExecuteClickAsync(request with
			{
				Reason = reason
			});
			if (i < count - 1)
			{
				await DelayWithCancellationAsync(intervalSeconds, cancellationToken);
			}
		}
	}

	private async Task ClickRatioPointRepeatedAsync(RatioPoint point, string reason, int count, double intervalSeconds, CancellationToken cancellationToken)
	{
		if ((object)_gameWindow == null)
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		WindowClientRect rect = _gameWindow.ClientRect;
		ClickRequest request = new ClickRequest(reason, rect.Left + (int)Math.Round((double)rect.Width * point.X), rect.Top + (int)Math.Round((double)rect.Height * point.Y));
		await ExecuteRepeatedClickAsync(request, count, intervalSeconds, cancellationToken);
	}

	private async Task TryHandleUnderfilledTeamConfirmAsync(CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(0.5, cancellationToken);
		if ((object)OcrClickResolver.FindBest(await CaptureAndOcrAsync(InGameOpeningFlow.DialogRegion, cancellationToken), InGameOpeningFlow.UnderfilledTeamAliases, _config.ButtonFuzzyScore) == null)
		{
			AppendLog("局内识别：未检测到人数不齐确认弹窗。");
			return;
		}
		AppendLog("局内识别：检测到人数不齐确认弹窗，勾选本局不再提示并确认。");
		await ClickRatioPointAsync(InGameOpeningFlow.UnderfilledDoNotRemindPoint, "局内识别：本局不再提示", cancellationToken);
		await DelayWithCancellationAsync(0.2, cancellationToken);
		await ClickRatioPointAsync(InGameOpeningFlow.UnderfilledConfirmPoint, "局内识别：人数不齐确认", cancellationToken);
		await DelayWithCancellationAsync(0.5, cancellationToken);
	}

	private async Task RunOpeningBattlesUntilTwoContinueClicksAsync(CancellationToken cancellationToken)
	{
		int battleStartCount = 0;
		int continueChallengeCount = 0;
		DateTime deadline = DateTime.UtcNow.AddSeconds(300.0);
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
			if (await TryHandleRoleChoicePopupAsync(scan, cancellationToken))
			{
				continue;
			}
			OcrClickCandidate continueCandidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.ContinueButtonAliases, _config.ButtonFuzzyScore);
			if ((object)continueCandidate != null && (object)_latestCaptureScreenRegion != null)
			{
				Rect bounds = continueCandidate.Item.Bounds;
				await ExecuteClickAsync(new ClickRequest("局内识别：" + continueCandidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
				if (TextMatcher.Normalize(continueCandidate.Alias) == TextMatcher.Normalize("继续挑战"))
				{
					continueChallengeCount++;
					AppendLog($"局内识别：已点击继续挑战 {continueChallengeCount}/2 次。");
					if (continueChallengeCount >= 2)
					{
						AppendLog("局内识别：已点击 2 次继续挑战，停止局内开局流程，避免第三次出战。");
						return;
					}
				}
				await DelayWithCancellationAsync(1.0, cancellationToken);
			}
			else if ((object)OcrClickResolver.FindBest(scan, InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore) != null)
			{
				if (await ClickInGameBattleButtonAsync(battleStartCount, cancellationToken))
				{
					battleStartCount++;
					AppendLog($"局内识别：已点击出战 {battleStartCount} 次。");
					await DelayWithCancellationAsync(10.0, cancellationToken);
				}
			}
			else
			{
				await ClickRatioPointAsync(InGameOpeningFlow.ContinueFallbackPoint, "局内识别：点击空白继续兜底", cancellationToken);
				await DelayWithCancellationAsync(1.0, cancellationToken);
			}
		}
		AppendLog("局内识别：开局两把等待超时，按当前状态结束。");
	}

	private async Task RunStrategyRecognitionAsync(string strategyName, IReadOnlyList<string> strategyAliases, bool allowExtraRightRefresh, CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(10.5, cancellationToken);
		AppendLog("局内识别：开始策略识别，目标：" + strategyName + "。");
		if (!(await IsStrategySelectionScreenAsync(cancellationToken)))
		{
			AppendLog("局内识别：当前不是策略选择界面，本轮不做策略命中判断。");
			return;
		}
		if (await TryClickTargetStrategyAsync("首次策略识别", strategyAliases, 2, cancellationToken))
		{
			StopAutomationForSuccess("状态：局内策略命中停止");
			return;
		}
		AppendLog("局内识别：首次策略未命中，点击 3 个刷新按钮。");
		RatioPoint[] strategyRefreshButtons = InGameOpeningFlow.StrategyRefreshButtons;
		foreach (RatioPoint point in strategyRefreshButtons)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(point, "局内识别：刷新策略", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
		}
		if (await TryClickTargetStrategyAsync("左中右刷新后策略识别", strategyAliases, 1, cancellationToken))
		{
			StopAutomationForSuccess("状态：局内策略命中停止");
			return;
		}
		if (!allowExtraRightRefresh)
		{
			AppendLog("局内识别：本轮投资不是“银·金·彩”，跳过右侧额外刷新。");
		}
		RatioPoint rightRefreshPoint = InGameOpeningFlow.StrategyRefreshButtons[^1];
		for (int i = 0; allowExtraRightRefresh && i < InGameOpeningFlow.ExtraRightStrategyRefreshCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(rightRefreshPoint, $"局内识别：额外刷新右侧策略 {i + 1}/{InGameOpeningFlow.ExtraRightStrategyRefreshCount}", cancellationToken);
			await DelayWithCancellationAsync(InGameOpeningFlow.StrategyRefreshDelaySeconds, cancellationToken);
			if (await TryClickTargetStrategyAsync($"右侧第 {i + 2} 次刷新后策略识别", strategyAliases, InGameOpeningFlow.InitialStrategyScanAttemptCount, cancellationToken))
			{
				StopAutomationForSuccess("状态：局内策略命中停止");
				return;
			}
		}
		AppendLog("局内识别：刷新后仍未命中目标策略，随机选择 1 张策略后点击确认。");
		await ClickRandomStrategyCardAsync(cancellationToken);
		await ClickStrategyConfirmAsync(cancellationToken);
	}

	private async Task<bool> IsStrategySelectionScreenAsync(CancellationToken cancellationToken)
	{
		OcrClickCandidate candidate = OcrClickResolver.FindBest(await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken), InGameOpeningFlow.StrategyScreenAliases, 83);
		if ((object)candidate == null)
		{
			return false;
		}
		AppendLog($"局内识别：确认策略选择界面：{candidate.Item.Text}（匹配 {candidate.Alias}）。");
		return true;
	}

	private async Task<bool> TryClickTargetStrategyAsync(string scope, IReadOnlyList<string> strategyAliases, int scanAttemptCount, CancellationToken cancellationToken)
	{
		AppendLog("局内识别：" + scope + "开始。");
		for (int attempt = 1; attempt <= scanAttemptCount; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AppendLog($"局内识别：{scope}第 {attempt} 次扫描。");
			OcrClickCandidate candidate = OcrClickResolver.FindBest(await CaptureAndOcrAsync(InGameOpeningFlow.StrategyRegion, cancellationToken), strategyAliases, 83);
			if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
			{
				Rect bounds = candidate.Item.Bounds;
				await ExecuteClickAsync(new ClickRequest("局内策略：" + candidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
				AppendLog($"局内识别：{scope}命中目标策略：{candidate.Alias}。");
				return true;
			}
			if (attempt < scanAttemptCount)
			{
				await DelayWithCancellationAsync(0.1, cancellationToken);
			}
		}
		AppendLog("局内识别：" + scope + "未命中目标策略。");
		return false;
	}

	private static bool IsExtraStrategyRefreshInvestment(string? hitWord)
	{
		if (string.IsNullOrWhiteSpace(hitWord))
		{
			return false;
		}
		string normalizedHit = TextMatcher.Normalize(hitWord);
		return InGameOpeningFlow.ExtraStrategyRefreshInvestmentAliases.Any(alias => TextMatcher.Normalize(alias) == normalizedHit);
	}

	private async Task ClickRandomStrategyCardAsync(CancellationToken cancellationToken)
	{
		RatioPoint[] points = InGameOpeningFlow.StrategyCards;
		HashSet<int> blockedColumns = FindBlacklistedStrategyColumns(await CaptureAndOcrAsync(InGameOpeningFlow.StrategyRegion, cancellationToken));
		List<int> candidates = (from item in Enumerable.Range(0, points.Length)
			where !blockedColumns.Contains(item)
			select item).ToList();
		if (candidates.Count == 0)
		{
			candidates = Enumerable.Range(0, points.Length).ToList();
		}
		if (blockedColumns.Count > 0)
		{
			AppendLog("局内识别：随机选策略时避开黑名单列 " + string.Join("、", blockedColumns.Select((int num) => num + 1)) + "。");
		}
		int index = candidates[Random.Shared.Next(candidates.Count)];
		await ClickRatioPointAsync(points[index], $"局内识别：随机选择策略 {index + 1}", cancellationToken);
		await DelayWithCancellationAsync(0.1, cancellationToken);
	}

	private HashSet<int> FindBlacklistedStrategyColumns(OcrScanResult scan)
	{
		HashSet<int> blockedColumns = new HashSet<int>();
		foreach (OcrTextItem item in scan.Items)
		{
			if (InGameOpeningFlow.StrategyChoiceBlacklist.Any((string word) => TextMatcher.FuzzyContains(item.Text, word, 83)))
			{
				blockedColumns.Add(GetStrategyColumn(item));
			}
		}
		return blockedColumns;
	}

	private int GetStrategyColumn(OcrTextItem item)
	{
		double num = item.Bounds.X + item.Bounds.Width / 2.0;
		int width = Math.Max(1, _latestCaptureScreenRegion?.Width ?? 1);
		double relativeX = num / (double)width;
		if (relativeX < 1.0 / 3.0)
		{
			return 0;
		}
		if (relativeX < 2.0 / 3.0)
		{
			return 1;
		}
		return 2;
	}

	private async Task ClickStrategyConfirmAsync(CancellationToken cancellationToken)
	{
		await ClickRatioPointAsync(InGameOpeningFlow.StrategyConfirmPoint, "局内识别：策略固定确认", cancellationToken);
	}

	private async Task StartAutomationAsync()
	{
		if (_automationCts != null)
		{
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		if (_config.DebuffEnabled && _config.TargetWords.Count == 0)
		{
			MessageBox.Show(this, "主词条检测开启时，请先添加至少一个目标词条。", "缺少目标词条", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		_automationCts = new CancellationTokenSource();
		_automationSuccessStop = false;
		SetAutomationButtonsEnabled(enabled: false);
		_lastSafeInvestmentPoint = null;
		_blockedHitThisCycle = false;
		SetStatus("状态：自动刷新运行中");
		AutomationRuntime runtime = new AutomationRuntime(_config, ExecuteFlowStepAsync, DelayWithCancellationAsync, VariableDelayWithCancellationAsync, delegate(string message)
		{
			AppendLog(message);
			return Task.CompletedTask;
		});
		try
		{
			await runtime.RunAsync(_automationCts.Token);
		}
		catch (OperationCanceledException)
		{
			AppendLog(_automationSuccessStop ? "自动流程：成功停止。" : "自动流程：已手动停止。");
			SetStatus(_automationSuccessStop ? "状态：成功停止" : "状态：已停止");
			if (_automationSuccessStop)
			{
				MessageBox.Show(this, "成功刷出目标词条或投资词条！", "成功", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception ex2)
		{
			AppendLog("自动流程失败：" + ex2.Message);
			SetStatus("状态：自动流程失败");
			MessageBox.Show(this, ex2.Message, "自动流程失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_automationCts?.Dispose();
			_automationCts = null;
			SetAutomationButtonsEnabled(enabled: true);
			if (!_automationSuccessStop && StatusText.Text == "状态：自动刷新运行中")
			{
				SetStatus("状态：已停止");
			}
		}
	}

	private void StopAutomation()
	{
		_automationCts?.Cancel();
	}

	private void StopAutomationForSuccess(string status)
	{
		_automationSuccessStop = true;
		SetStatus(status);
		StopAutomation();
	}

	private void ResolveInvestmentHit(string hitWord)
	{
		AppendLog("自动流程：投资词条命中：" + hitWord + "，停止。");
		DecisionReasonText.Text = "当前决策：投资词条命中：" + hitWord + "，停止。";
		StopAutomationForSuccess("状态：投资识别成功停止");
	}

	private async Task ExecuteFlowStepAsync(FlowStep step, CancellationToken cancellationToken)
	{
		if (step == CurrencyWarsFlow.Steps[0])
		{
			_blockedHitThisCycle = false;
		}
		if ((object)_gameWindow == null && !TryFindWindow())
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
		switch (step.Kind)
		{
		case FlowStepKind.ClickText:
			await ExecuteClickTextStepAsync(step, cancellationToken);
			if (step.CheckDebuffAfterStep && _config.DebuffEnabled)
			{
				await DelayWithCancellationAsync(0.6, cancellationToken);
				await WaitForDebuffResultAsync(cancellationToken);
			}
			break;
		case FlowStepKind.ClickRelativePoint:
			await ClickRatioPointAsync(step.ClickPoint ?? new RatioPoint(0.5, 0.5), step.Name, cancellationToken);
			break;
		case FlowStepKind.SafeInvestmentChoice:
			await ClickSafeInvestmentAsync(rememberChoice: true, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
			break;
		case FlowStepKind.RepeatSafeInvestmentChoice:
			await ClickRatioPointAsync(_lastSafeInvestmentPoint ?? new RatioPoint(0.5, 0.38), step.Name, cancellationToken);
			break;
		case FlowStepKind.InvestmentSearch:
			await ExecuteInvestmentSearchAsync(cancellationToken);
			break;
		case FlowStepKind.PressKey:
			await ExecutePressKeyStepAsync(step, cancellationToken);
			break;
		case FlowStepKind.FastExitToSettlement:
			await ExecuteFastExitToSettlementAsync(cancellationToken);
			break;
		}
	}

	private async Task ExecutePressKeyStepAsync(FlowStep step, CancellationToken cancellationToken)
	{
		string key = step.Key ?? "";
		AppendLog((await _clickService.PressKeyAsync(key, _gameWindow?.Handle ?? IntPtr.Zero, cancellationToken)).Message);
	}

	private async Task ExecuteFastExitToSettlementAsync(CancellationToken cancellationToken)
	{
		if ((object)_gameWindow == null)
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		await ClickEscAndSettlementPointsAsync(cancellationToken);
	}

	private async Task ClickEscAndSettlementPointsAsync(CancellationToken cancellationToken)
	{
		AppendLog($"自动流程：直接交替点击左上角退出和放弃并结算，各 {7} 次。");
		for (int i = 0; i < 7; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(CurrencyWarsFlow.FastEscApproxPoint, $"左上角退出区域 第 {i + 1} 次", cancellationToken);
			await DelayWithCancellationAsync(0.11, cancellationToken);
			await ClickRatioPointAsync(CurrencyWarsFlow.FastSettlementApproxPoint, $"放弃并结算区域 第 {i + 1} 次", cancellationToken);
			await DelayWithCancellationAsync(0.11, cancellationToken);
		}
	}

	private async Task ExecuteClickTextStepAsync(FlowStep step, CancellationToken cancellationToken)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(step.TimeoutSeconds);
		string lastText = "";
		bool useBottomReturnPoint = IsBottomReturnFlowStep(step);
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(step.SearchRegion, cancellationToken);
			lastText = scan.RawText;
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, step.Aliases, _config.ButtonFuzzyScore);
			if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
			{
				Rect bounds = candidate.Item.Bounds;
				int clickX = _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0);
				int clickY = _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0);
				if (useBottomReturnPoint && (object)_gameWindow != null)
				{
					WindowClientRect rect = _gameWindow.ClientRect;
					clickX = rect.Left + (int)Math.Round((double)rect.Width * 0.5);
					clickY = rect.Top + (int)Math.Round((double)rect.Height * 0.829);
				}
				ClickRequest request = new ClickRequest(step.Name + "：" + candidate.Item.Text, clickX, clickY);
				await ExecuteClickAsync(request);
				SetStatus("状态：已点击 " + step.Name);
				return;
			}
			await DelayWithCancellationAsync(useBottomReturnPoint ? 0.15 : 0.6, cancellationToken);
		}
		if ((object)step.FallbackPoint != null)
		{
			AppendLog("自动流程：" + step.Name + " OCR 未命中，使用兜底坐标。最后 OCR：" + ShortText(lastText));
			await ClickRatioPointAsync(step.FallbackPoint, step.Name + " 兜底", cancellationToken);
			return;
		}
		if (useBottomReturnPoint)
		{
			AppendLog("自动流程：" + step.Name + " OCR 未命中，使用底部固定坐标。最后 OCR：" + ShortText(lastText));
			await ClickRatioPointAsync(new RatioPoint(0.5, 0.829), step.Name + " 固定坐标兜底", cancellationToken);
			return;
		}
		throw new InvalidOperationException("超时：没有找到按钮文字“" + step.Name + "”。最后 OCR：" + ShortText(lastText));
	}

	private static bool IsBottomReturnFlowStep(FlowStep step)
	{
		bool flag = (object)step.FallbackPoint == null && step.SearchRegion == CurrencyWarsFlow.FullWindow;
		if (flag)
		{
			bool flag2;
			switch (step.Name)
			{
			case "下一步":
			case "下一页":
			case "返回货币战争":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		return flag;
	}

	private async Task WaitForDebuffResultAsync(CancellationToken cancellationToken)
	{
		BasicScanEvaluation evaluation = await WaitForDebuffEvaluationAsync("自动流程", cancellationToken);
		if ((object)evaluation != null && evaluation.DebuffSuccess)
		{
			AppendLog("自动流程：" + evaluation.DecisionReason + " 停止。");
			StopAutomationForSuccess("状态：主词条成功停止");
		}
	}

	private async Task WaitForIndependentDebuffResultAsync(CancellationToken cancellationToken)
	{
		BasicScanEvaluation evaluation = await WaitForDebuffEvaluationAsync("独立局内预设", cancellationToken);
		if ((object)evaluation != null && evaluation.DebuffSuccess)
		{
			AppendLog("独立局内预设：" + evaluation.DecisionReason + "，继续进入投资流程。");
		}
	}

	private async Task<BasicScanEvaluation?> WaitForDebuffEvaluationAsync(string scope, CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(_config.DebuffCheckDelaySeconds, cancellationToken);
		DateTime deadline = DateTime.UtcNow.AddSeconds(3.0);
		BasicScanEvaluation latestEvaluation = null;
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken);
			AppendLog(scope + "：主词条 OCR 原文：" + ShortText(scan.RawText));
			BasicScanEvaluation evaluation = _scanEvaluator.Evaluate(_config, scan.RawText);
			latestEvaluation = evaluation;
			EvaluationTextBox.Text = FormatEvaluation(evaluation);
			ApplyEvaluationSummary(evaluation);
			_blockedHitThisCycle = evaluation.BlockedHit;
			if (evaluation.DebuffSuccess)
			{
				return evaluation;
			}
			if (IsDebuffScreenReady(scan.RawText))
			{
				AppendLog(scope + "：词条页已识别，未命中，继续后续段落。");
				return evaluation;
			}
			await DelayWithCancellationAsync(0.3, cancellationToken);
		}
		AppendLog(scope + "：词条页等待超时，按当前结果继续后续段落。");
		return latestEvaluation;
	}

	private async Task ExecuteInvestmentSearchAsync(CancellationToken cancellationToken)
	{
		if (_blockedHitThisCycle && !_config.CheckInvestmentWhenBlocked)
		{
			AppendLog("自动流程：本轮命中不想要词条，跳过投资识别。");
			DecisionReasonText.Text = "当前决策：本轮命中不想要词条，跳过投资识别。";
			return;
		}
		if (_blockedHitThisCycle && _config.CheckInvestmentWhenBlocked)
		{
			AppendLog("自动流程：本轮命中不想要词条，但开关允许继续检查投资识别。");
			DecisionReasonText.Text = "当前决策：本轮命中不想要词条，继续检查投资识别。";
		}
		if (!_config.InvestmentEnabled || _config.InvestmentTargets.Count == 0)
		{
			return;
		}
		string hitWord = await TryClickInvestmentTargetAsync("首次投资识别", cancellationToken, logRawText: true);
		if (hitWord != null)
		{
			ResolveInvestmentHit(hitWord);
			return;
		}
		AppendLog("自动流程：首次投资识别未命中，准备检查剩余次数刷新。");
		OcrClickCandidate remaining = OcrClickResolver.FindBest(await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken), new _003C_003Ez__ReadOnlySingleElementList<string>("剩余次数"), _config.ButtonFuzzyScore);
		if ((object)remaining != null && (object)_latestCaptureScreenRegion != null)
		{
			Rect bounds = remaining.Item.Bounds;
			await ExecuteClickAsync(new ClickRequest("投资刷新：" + remaining.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
			await DelayWithCancellationAsync(_config.InvestmentIntervalSeconds, cancellationToken);
			hitWord = await TryClickInvestmentTargetAsync("刷新后投资识别", cancellationToken, logRawText: true);
			if (hitWord != null)
			{
				ResolveInvestmentHit(hitWord);
				return;
			}
		}
		await ClickSafeInvestmentAsync(rememberChoice: false, useConfiguredInvestmentTargetsForBlacklist: true, cancellationToken);
		await ClickBlueOceanFollowupGuardAsync(cancellationToken);
	}

	private async Task<string?> TryClickInvestmentTargetAsync(string scope, CancellationToken cancellationToken, bool logRawText = false)
	{
		AppendLog($"自动流程：{scope}开始，固定扫描 {CurrencyWarsFlow.InvestmentScanAttemptCount} 次。");
		for (int attempt = 1; attempt <= CurrencyWarsFlow.InvestmentScanAttemptCount; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			SetStatus($"状态：{scope} 第 {attempt} 次");
			AppendLog($"自动流程：{scope} 第 {attempt} 次扫描上半屏。");
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
			if (logRawText)
			{
				AppendLog($"自动流程：{scope} 第 {attempt} 次 OCR 原文：{ShortText(scan.RawText)}");
			}
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, _config.InvestmentTargets, _config.InvestmentFuzzyScore);
			if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
			{
				Rect bounds = candidate.Item.Bounds;
				await ExecuteClickAsync(new ClickRequest("投资词条：" + candidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
				return candidate.Alias;
			}
			if (attempt < CurrencyWarsFlow.InvestmentScanAttemptCount)
			{
				await DelayWithCancellationAsync(0.1, cancellationToken);
			}
		}
		AppendLog("自动流程：" + scope + "结束，未命中投资词条。");
		return null;
	}

	private async Task ClickSafeInvestmentAsync(bool rememberChoice, bool useConfiguredInvestmentTargetsForBlacklist, CancellationToken cancellationToken)
	{
		RatioPoint point = ChooseSafeInvestmentPoint(await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken), useConfiguredInvestmentTargetsForBlacklist);
		if (rememberChoice)
		{
			_lastSafeInvestmentPoint = point;
		}
		await ClickRatioPointAsync(point, "默认安全投资", cancellationToken);
	}

	private async Task ClickBlueOceanFollowupGuardAsync(CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(0.3, cancellationToken);
		for (int i = 0; i < 4; i++)
		{
			await ClickRatioPointAsync(new RatioPoint(0.52, 0.49), $"蓝海二次投资中间选项兜底 {i + 1}/4", cancellationToken);
			await DelayWithCancellationAsync(0.1, cancellationToken);
			await ClickRatioPointAsync(new RatioPoint(0.565, 0.91), $"蓝海二次投资确认兜底 {i + 1}/4", cancellationToken);
			await DelayWithCancellationAsync(0.1, cancellationToken);
		}
	}

	private RatioPoint ChooseSafeInvestmentPoint(OcrScanResult scan, bool useConfiguredInvestmentTargetsForBlacklist)
	{
		List<string> activeTargets = (useConfiguredInvestmentTargetsForBlacklist ? _config.InvestmentTargets : new List<string>());
		HashSet<int> blockedColumns = FindBlacklistedInvestmentColumns(scan, activeTargets);
		int chosenIndex = new int[3] { 1, 0, 2 }.FirstOrDefault((int index) => !blockedColumns.Contains(index));
		if (blockedColumns.Contains(chosenIndex))
		{
			chosenIndex = 1;
		}
		if (blockedColumns.Count > 0)
		{
			AppendLog("默认投资选择：避开特殊投资列 " + string.Join("、", blockedColumns.Select((int index) => index + 1)));
		}
		return CurrencyWarsFlow.InvestmentFallbackPoints[chosenIndex];
	}

	private HashSet<int> FindBlacklistedInvestmentColumns(OcrScanResult scan, IReadOnlyList<string> activeTargets)
	{
		HashSet<string> normalizedTargets = activeTargets.Select(TextMatcher.Normalize).ToHashSet<string>(StringComparer.Ordinal);
		List<string> blacklist = CurrencyWarsFlow.SpecialInvestmentBlacklist.Where((string word) => !normalizedTargets.Contains(TextMatcher.Normalize(word))).ToList();
		HashSet<int> blockedColumns = new HashSet<int>();
		int score = Math.Max(76, _config.InvestmentFuzzyScore - 10);
		foreach (OcrTextItem item in scan.Items)
		{
			if (blacklist.Any((string word) => TextMatcher.FuzzyContains(item.Text, word, score)))
			{
				blockedColumns.Add(GetInvestmentColumn(item));
			}
		}
		return blockedColumns;
	}

	private int GetInvestmentColumn(OcrTextItem item)
	{
		double num = item.Bounds.X + item.Bounds.Width / 2.0;
		int width = Math.Max(1, _latestCaptureScreenRegion?.Width ?? 1);
		double relativeX = num / (double)width;
		if (relativeX < 1.0 / 3.0)
		{
			return 0;
		}
		if (relativeX < 2.0 / 3.0)
		{
			return 1;
		}
		return 2;
	}

	private async Task ClickRatioPointAsync(RatioPoint point, string reason, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if ((object)_gameWindow == null)
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		WindowClientRect rect = _gameWindow.ClientRect;
		ClickRequest request = new ClickRequest(reason, rect.Left + (int)Math.Round((double)rect.Width * point.X), rect.Top + (int)Math.Round((double)rect.Height * point.Y));
		await ExecuteClickAsync(request);
	}

	private async Task<OcrScanResult> CaptureAndOcrAsync(RatioRegion region, CancellationToken cancellationToken)
	{
		if ((object)_gameWindow == null)
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		CaptureRegion captureRegion = new CaptureRegion("自动流程区域", region.X, region.Y, region.Width, region.Height);
		WindowClientRect resolved = _windowCapture.ResolveRegion(_gameWindow.ClientRect, captureRegion);
		BitmapSource image = (_latestPreviewImage = _windowCapture.Capture(_gameWindow, captureRegion));
		_latestCaptureScreenRegion = resolved;
		_latestPreviewRegion = captureRegion;
		PreviewImage.Source = image;
		PreviewPlaceholder.Visibility = Visibility.Collapsed;
		CaptureInfoText.Text = $"截图：自动流程区域  {resolved.Width}x{resolved.Height}  left={resolved.Left}, top={resolved.Top}";
		OcrScanResult scan = (_latestOcrResult = await _ocrService.RecognizeAsync(image, cancellationToken));
		OcrRawTextBox.Text = FormatOcrResult(captureRegion.Name, scan);
		OcrInfoText.Text = $"OCR：自动流程区域，文本块 {scan.Items.Count}，字符 {scan.RawText.Length}";
		return scan;
	}

	private static bool IsDebuffScreenReady(string ocrText)
	{
		string normalized = TextMatcher.Normalize(ocrText);
		return CurrencyWarsFlow.DebuffScreenHints.Any((string hint) => normalized.Contains(TextMatcher.Normalize(hint), StringComparison.Ordinal));
	}

	private static async Task DelayWithCancellationAsync(double seconds, CancellationToken cancellationToken)
	{
		if (!(seconds <= 0.0))
		{
			await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
		}
	}

	private Task VariableDelayWithCancellationAsync(double seconds, CancellationToken cancellationToken)
	{
		return DelayWithCancellationAsync(Math.Max(0.0, seconds), cancellationToken);
	}

	private static string ShortText(string text)
	{
		text = text.ReplaceLineEndings(" ").Trim();
		if (text.Length > 80)
		{
			return text.Substring(0, 80) + "...";
		}
		return text;
	}

	private static string FormatOcrResult(string scope, OcrScanResult result)
	{
		string header = $"范围：{scope}{Environment.NewLine}时间：{result.ScannedAt:HH:mm:ss}{Environment.NewLine}文本块：{result.Items.Count}{Environment.NewLine}{Environment.NewLine}";
		if (string.IsNullOrWhiteSpace(result.RawText))
		{
			return header + "没有识别到文本。";
		}
		return header + result.RawText;
	}

	private static string FormatEvaluation(BasicScanEvaluation evaluation)
	{
		string newLine = Environment.NewLine;
		InlineArray9<string> buffer = default(InlineArray9<string>);
		buffer[0] = "主词条检测：" + (evaluation.DebuffSuccess ? "成功" : "未成功");
		buffer[1] = "命中模式：" + evaluation.DebuffModeText;
		buffer[2] = "主词条已命中：" + JoinWords(evaluation.TargetMatch.HitWords);
		buffer[3] = "主词条未命中：" + JoinWords(evaluation.TargetMatch.MissingWords);
		buffer[4] = "不想要词条命中：" + JoinWords(evaluation.BlockedMatch.HitWords);
		buffer[5] = "投资词条命中：" + JoinWords(evaluation.InvestmentMatch.HitWords);
		buffer[6] = "当前决策：" + evaluation.DecisionReason;
		buffer[7] = "";
		buffer[8] = "说明：手动 OCR 会显示当前冲突处理模式下的解释；自动流程会按同一套模式决定停止或继续。";
		return string.Join(newLine, (ReadOnlySpan<string?>)buffer);
	}

	private void ApplyEvaluationSummary(BasicScanEvaluation? evaluation)
	{
		if ((object)evaluation == null)
		{
			HitWordsText.Text = "已命中：无";
			BlockedHitWordsText.Text = "不想要命中：无";
			MissingWordsText.Text = "未命中：无";
			DecisionReasonText.Text = "当前决策：未评估";
		}
		else
		{
			HitWordsText.Text = "已命中：" + JoinWords(evaluation.TargetMatch.HitWords);
			BlockedHitWordsText.Text = "不想要命中：" + JoinWords(evaluation.BlockedMatch.HitWords);
			MissingWordsText.Text = "未命中：" + JoinWords(evaluation.TargetMatch.MissingWords);
			DecisionReasonText.Text = "当前决策：" + evaluation.DecisionReason;
		}
	}

	private static string JoinWords(IReadOnlyList<string> words)
	{
		if (words.Count != 0)
		{
			return string.Join("、", words);
		}
		return "无";
	}

	private static IOcrService CreateOcrService()
	{
		string bridgeExe = Path.Combine(AppContext.BaseDirectory, "OCRRuntime", "rapidocr_bridge.exe");
		if (File.Exists(bridgeExe))
		{
			return new ExternalRapidOcrService(bridgeExe);
		}
		string bridgeScript = Path.Combine(AppContext.BaseDirectory, "Tools", "rapidocr_bridge.py");
		if (!File.Exists(bridgeScript))
		{
			return new PendingOcrService("找不到桥接脚本：" + bridgeScript);
		}
		string pythonExe = FindPythonExe();
		if (pythonExe == null)
		{
			return new PendingOcrService("找不到可用 Python。");
		}
		return new ExternalRapidOcrService(pythonExe, bridgeScript);
	}

	private static string? FindPythonExe()
	{
		string[] array = new string[4]
		{
			Path.Combine(AppContext.BaseDirectory, "OCRRuntime", "python.exe"),
			Path.Combine(AppContext.BaseDirectory, "ocr_runtime", "python.exe"),
			Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
			"C:\\Users\\SHINELON\\Documents\\Codex\\2026-06-07\\windows-python-ocr-debuff-1-python\\outputs\\debuff_ocr_tool\\.venv\\Scripts\\python.exe"
		};
		foreach (string candidate in array)
		{
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		return "python";
	}

	private void AppendLog(string message)
	{
		string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
		LogBox.AppendText(line + Environment.NewLine);
		LogBox.ScrollToEnd();
		_gameLogOverlay.AppendLog(line);
	}

	private void AppendStartupNotice()
	{
		LogBox.AppendText("      ========================================================\r\n      ||                                                  ||\r\n      ||       本工具为开源免费项目，没有任何收费，如果您是付费获得此款产品请立即退款        ||\r\n      ||                                                  ||\r\n      ========================================================\r\n");
		LogBox.ScrollToEnd();
	}

	private void GameLogOverlayTimer_Tick(object? sender, EventArgs e)
	{
		if (GameLogOverlayCheckBox.IsChecked != true)
		{
			_gameLogOverlay.HideOverlay();
			return;
		}
		if ((object)_gameWindow == null)
		{
			TryRefreshGameWindowSilently();
		}
		if ((object)_gameWindow == null || !IsGameWindowForeground(_gameWindow.Handle))
		{
			_gameLogOverlay.HideOverlay();
			return;
		}
		if (!TryGetClientRectOnScreen(_gameWindow.Handle, out WindowClientRect clientRect))
		{
			_gameLogOverlay.HideOverlay();
			return;
		}
		_gameWindow = _gameWindow with
		{
			ClientRect = clientRect
		};
		_gameLogOverlay.UpdateGeometry(clientRect);
		_gameLogOverlay.ShowOverlay();
	}

	private void TryRefreshGameWindowSilently()
	{
		if (string.IsNullOrWhiteSpace(_config.WindowTitle))
		{
			return;
		}
		try
		{
			_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
		}
		catch
		{
			_gameWindow = null;
		}
	}

	private static bool IsGameWindowForeground(nint hwnd)
	{
		if (hwnd == IntPtr.Zero || GetForegroundWindow() != hwnd)
		{
			return false;
		}
		if (IsWindowVisible(hwnd))
		{
			return !IsIconic(hwnd);
		}
		return false;
	}

	private static bool TryGetClientRectOnScreen(nint hwnd, out WindowClientRect clientRect)
	{
		clientRect = new WindowClientRect(0, 0, 0, 0);
		if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rect))
		{
			return false;
		}
		PointStruct topLeft = new PointStruct(0, 0);
		if (!ClientToScreen(hwnd, ref topLeft))
		{
			return false;
		}
		int width = rect.Right - rect.Left;
		int height = rect.Bottom - rect.Top;
		if (width <= 0 || height <= 0)
		{
			return false;
		}
		clientRect = new WindowClientRect(topLeft.X, topLeft.Y, width, height);
		return true;
	}

	private void SetStatus(string status)
	{
		StatusText.Text = status;
	}

	private async Task CheckForUpdatesAsync(bool showNoUpdateMessage)
	{
		AppendLog("更新检查：当前版本 " + UpdateChecker.CurrentVersion + "。");
		UpdateCheckResult result = await UpdateChecker.CheckLatestAsync();
		AppendLog("更新检查：" + result.Message);
		if (!result.IsConfigured)
		{
			if (showNoUpdateMessage)
			{
				MessageBox.Show(this, result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			return;
		}
		if (!result.HasUpdate || (object)result.Update == null)
		{
			if (showNoUpdateMessage)
			{
				MessageBox.Show(this, result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			return;
		}
		UpdateInfo update = result.Update;
		string notes = (string.IsNullOrWhiteSpace(update.Notes) ? "这个版本没有填写更新说明。" : update.Notes.Trim());
		if (notes.Length > 700)
		{
			notes = notes.Substring(0, 700) + Environment.NewLine + "...";
		}
		if (MessageBox.Show(this, $"发现新版本：{update.Version}{Environment.NewLine}当前版本：{UpdateChecker.CurrentVersion}{Environment.NewLine}{Environment.NewLine}{notes}{Environment.NewLine}{Environment.NewLine}" + "是否打开下载页面？", "发现新版本", MessageBoxButton.YesNo, MessageBoxImage.Asterisk) == MessageBoxResult.Yes)
		{
			string url = ((!string.IsNullOrWhiteSpace(update.DownloadUrl)) ? update.DownloadUrl : update.ReleasePageUrl);
			OpenUrl(url);
		}
	}

	private void OpenUrl(string url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			MessageBox.Show(this, "这个 Release 没有可打开的下载地址。", "检查更新", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "打开下载页面失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void RegisterHotkeys()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		_hotkeySource = HwndSource.FromHwnd(handle);
		_hotkeySource?.AddHook(HotkeyHook);
		RegisterHotKey(handle, 1001, 16384u, 119u);
		AppendLog("热键已注册：F8 停止。");
	}

	private void UnregisterHotkeys()
	{
		UnregisterHotKey(new WindowInteropHelper(this).Handle, 1001);
		_hotkeySource?.RemoveHook(HotkeyHook);
		_hotkeySource = null;
	}

	private nint HotkeyHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		if (msg != 786)
		{
			return IntPtr.Zero;
		}
		handled = true;
		if (((IntPtr)wParam).ToInt32() == 1001)
		{
			StopAutomation();
		}
		return IntPtr.Zero;
	}

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint windowHandle, int id);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hwnd);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hwnd);

	[DllImport("user32.dll")]
	private static extern bool GetClientRect(nint hwnd, out NativeRect rect);

	[DllImport("user32.dll")]
	private static extern bool ClientToScreen(nint hwnd, ref PointStruct point);

	[DllImport("dwmapi.dll")]
	private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
