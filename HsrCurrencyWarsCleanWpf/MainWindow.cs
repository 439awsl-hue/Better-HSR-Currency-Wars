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

	private const int MaxLogLines = 500;

	private static readonly TimeSpan OcrMaintenanceRestartInterval = TimeSpan.FromMinutes(90.0);

	private static readonly RatioPoint WeeklyHomeSafePoint = new RatioPoint(0.94, 0.8);

	private static readonly RatioRegion WeeklyPointsRegion = new RatioRegion(0.018, 0.865, 0.205, 0.085);

	private static readonly RatioRegion GameMenuRightQuarterRegion = new RatioRegion(0.75, 0.0, 0.25, 1.0);

	private static readonly RatioRegion ResolutionListRegion = new RatioRegion(0.0, 0.1, 1.0, 0.55);

	private static readonly string[] GameMenuStableAnchors = new string[13]
	{
		"好友", "委托", "合成", "成就", "短信", "无名勋礼", "跃迁", "角色", "指南", "联机玩法",
		"导航", "教学目录", "游戏工具"
	};

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

	private bool _outerOpeningRapidAdvanceCompleted;

	private bool _outerBottomReturnRapidSequenceCompleted;

	private string? _combinedSuccessMessage;

	private readonly MediaPlayer _successAudioPlayer = new MediaPlayer();

	private bool _successAudioInitialized;

	private bool _successAudioReady;

	private bool _successAudioPlayPending;

	private HwndSource? _hotkeySource;

	private readonly GameLogOverlayWindow _gameLogOverlay = new GameLogOverlayWindow();

	private readonly DispatcherTimer _gameLogOverlayTimer = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(180L)
	};

	private int _logLineCount;

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
		InitializeSuccessAudio();
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
		base.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ShowStartupNotices));
	}

	private void ShowStartupNotices()
	{
		if (!string.Equals(_config.HiddenReleaseNotesVersion, ReleaseNotes.CurrentVersion, StringComparison.Ordinal))
		{
			ReleaseNotesWindow releaseNotesWindow = new ReleaseNotesWindow
			{
				Owner = this
			};
			releaseNotesWindow.ShowDialog();
			if (releaseNotesWindow.DoNotShowAgain)
			{
				_config.HiddenReleaseNotesVersion = ReleaseNotes.CurrentVersion;
				_configStore.Save(_config);
				AppendLog("更新提示：本版本不再显示。");
			}
		}
		CheckForUpdatesAsync(showNoUpdateMessage: false);
	}

	private void MainWindow_Closed(object? sender, EventArgs e)
	{
		_automationCts?.Cancel();
		_gameLogOverlayTimer.Stop();
		_gameLogOverlay.Close();
		UnregisterHotkeys();
		_successAudioPlayer.Close();
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
		if (_automationCts != null)
		{
			AppendLog("自动流程运行中，暂不允许修改配置。");
			return;
		}
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
			ReservedPage.Visibility = ((!(pageName == "ReservedPage")) ? Visibility.Collapsed : Visibility.Visible);
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
		ReservedPage.Visibility = Visibility.Collapsed;
		LogPage.Visibility = Visibility.Visible;
		HelpPage.Visibility = Visibility.Collapsed;
		HighlightNav(LogNav);
	}

	private void HighlightNav(Button active)
	{
		Button[] array = new Button[7] { HomeNav, OutGameNav, InGameNav, AutoBattleNav, ReservedNav, LogNav, HelpNav };
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

	private async void OptimizeGameWindow_Click(object sender, RoutedEventArgs e)
	{
		if (_automationCts != null)
		{
			MessageBox.Show(this, "请先停止当前自动流程，再进行游戏窗口优化。", "游戏窗口优化", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}
		OptimizeGameWindowButton.IsEnabled = false;
		try
		{
			if (!TryFindWindow() || _gameWindow == null)
			{
				return;
			}
			WindowClientRect rect = _gameWindow.ClientRect;
			AppendLog($"游戏窗口优化：检测到客户区 {rect.Width}x{rect.Height}，目标为 1920x1080。");
			if (rect.Width == 1920 && rect.Height == 1080)
			{
				SetStatus("状态：游戏窗口已是 1920x1080");
				AppendLog("游戏窗口优化：当前分辨率已经符合要求，不执行 Esc。 ");
				MessageBox.Show(this, "游戏窗口客户区已经是 1920x1080，无需优化。", "游戏窗口优化", MessageBoxButton.OK, MessageBoxImage.Information);
				return;
			}
			SetStatus("状态：正在打开游戏菜单");
			for (int attempt = 1; attempt <= 5; attempt++)
			{
				_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
				AppendLog($"游戏窗口优化：第 {attempt}/5 次按 Esc，等待 2 秒后识别右侧四分之一区域。");
				AppendLog((await _clickService.PressKeyAsync("Esc", _gameWindow.Handle, CancellationToken.None)).Message);
				await DelayWithCancellationAsync(2.0, CancellationToken.None);
				_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
				OcrScanResult scan = await CaptureAndOcrAsync(GameMenuRightQuarterRegion, CancellationToken.None);
				List<string> hits = FindGameMenuAnchors(scan.RawText);
				AppendLog($"游戏窗口优化：第 {attempt}/5 次菜单检测，稳定文字命中 {hits.Count} 个：{(hits.Count == 0 ? "无" : string.Join("、", hits))}。OCR：{ShortText(scan.RawText)}");
				if (hits.Count >= 3)
				{
					SetStatus("状态：已打开游戏菜单，正在识别设置齿轮");
					AppendLog("游戏窗口优化：已确认手机菜单出现，开始在右侧四分之一区域识别设置齿轮。");
					if (!await TryClickGameSettingsGearAsync(CancellationToken.None))
					{
						SetStatus("状态：未识别到设置齿轮");
						MessageBox.Show(this, "手机菜单已经打开，但没有可靠识别到设置齿轮，因此没有点击。\n请查看日志中的齿轮相似度。", "游戏窗口优化", MessageBoxButton.OK, MessageBoxImage.Warning);
						return;
					}
					if (!await TryOpenResolutionListAsync(CancellationToken.None))
					{
						SetStatus("状态：未找到分辨率设置");
						MessageBox.Show(this, "已经点击设置齿轮，但在上半屏没有识别到“分辨率”这一行。", "游戏窗口优化", MessageBoxButton.OK, MessageBoxImage.Warning);
						return;
					}
					if (!await TrySelectWindowed1920x1080Async(CancellationToken.None))
					{
						SetStatus("状态：未找到 1920x1080 窗口化选项");
						MessageBox.Show(this, "分辨率列表已经展开，但逐次向下滚动后仍未识别到不带“全屏幕”的 1920x1080。\n工具已经停止，不会盲目点击其他分辨率。", "游戏窗口优化", MessageBoxButton.OK, MessageBoxImage.Warning);
						return;
					}
					await DelayWithCancellationAsync(3.0, CancellationToken.None);
					_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
					WindowClientRect optimizedRect = _gameWindow.ClientRect;
					WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={optimizedRect.Width}x{optimizedRect.Height}  left={optimizedRect.Left}, top={optimizedRect.Top}  {_windowCapture.DescribeDisplay(_gameWindow)}";
					bool optimized = optimizedRect.Width == 1920 && optimizedRect.Height == 1080;
					SetStatus(optimized ? "状态：游戏窗口优化完成" : "状态：已选择分辨率，等待游戏应用");
					AppendLog($"游戏窗口优化：选择完成后客户区为 {optimizedRect.Width}x{optimizedRect.Height}。");
					MessageBox.Show(this, optimized
						? "已选择 1920x1080 窗口化，游戏客户区检测通过。"
						: $"已经点击 1920x1080 窗口化选项，但当前客户区仍为 {optimizedRect.Width}x{optimizedRect.Height}。请检查游戏是否弹出了额外确认。", "游戏窗口优化", MessageBoxButton.OK, optimized ? MessageBoxImage.Information : MessageBoxImage.Warning);
					return;
				}
			}
			SetStatus("状态：未识别到游戏菜单");
			AppendLog("游戏窗口优化：连续按 Esc 5 次后仍未确认手机菜单，已停止，不会继续操作。");
			MessageBox.Show(this, "已尝试按 Esc 5 次，但右侧区域仍未识别到足够的菜单文字。\n请确认游戏没有最小化，并查看运行日志中的 OCR 原文。", "游戏窗口优化", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		catch (Exception ex)
		{
			SetStatus("状态：游戏窗口优化失败");
			AppendLog("游戏窗口优化失败：" + ex.Message);
			MessageBox.Show(this, ex.Message, "游戏窗口优化失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			OptimizeGameWindowButton.IsEnabled = true;
		}
	}

	private static List<string> FindGameMenuAnchors(string ocrText)
	{
		string normalized = TextMatcher.Normalize(ocrText);
		return GameMenuStableAnchors
			.Where(anchor => normalized.Contains(TextMatcher.Normalize(anchor), StringComparison.Ordinal))
			.Distinct(StringComparer.Ordinal)
			.ToList();
	}

	private async Task<bool> TryClickGameSettingsGearAsync(CancellationToken cancellationToken)
	{
		if (_gameWindow == null)
		{
			return false;
		}
		CaptureRegion region = new CaptureRegion("游戏菜单右侧四分之一区域", GameMenuRightQuarterRegion.X, GameMenuRightQuarterRegion.Y, GameMenuRightQuarterRegion.Width, GameMenuRightQuarterRegion.Height);
		WindowClientRect resolved = _windowCapture.ResolveRegion(_gameWindow.ClientRect, region);
		BitmapSource screenshot = _windowCapture.Capture(_gameWindow, region);
		GameSettingsGearDetectionResult detection = GameSettingsGearDetector.Detect(screenshot);
		AppendLog($"游戏窗口优化：设置齿轮识图最高相似度 {detection.Similarity:0.000}，阈值 0.650。");
		if (!detection.Found)
		{
			return false;
		}
		int screenX = resolved.Left + (int)Math.Round(detection.CenterX * resolved.Width / (double)screenshot.PixelWidth);
		int screenY = resolved.Top + (int)Math.Round(detection.CenterY * resolved.Height / (double)screenshot.PixelHeight);
		AppendLog((await _clickService.ClickAsync(new ClickRequest("游戏窗口优化：识图点击设置齿轮", screenX, screenY), _gameWindow.Handle, cancellationToken)).Message);
		await DelayWithCancellationAsync(1.8, cancellationToken);
		return true;
	}

	private async Task<bool> TryOpenResolutionListAsync(CancellationToken cancellationToken)
	{
		for (int attempt = 1; attempt <= 5; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
			OcrScanResult scan = await CaptureAndOcrAsync(ResolutionListRegion, cancellationToken);
			OcrClickCandidate? candidate = OcrClickResolver.FindBest(scan, new string[1] { "分辨率" }, 86);
			AppendLog($"游戏窗口优化：设置页第 {attempt}/5 次上半屏检测：{(candidate == null ? "未找到分辨率" : "找到 " + candidate.Item.Text)}。OCR：{ShortText(scan.RawText)}");
			if (candidate != null && _latestCaptureScreenRegion != null)
			{
				Rect bounds = candidate.Item.Bounds;
				int screenX = _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0);
				int screenY = _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0);
				AppendLog((await _clickService.ClickAsync(new ClickRequest("游戏窗口优化：展开分辨率列表", screenX, screenY), _gameWindow.Handle, cancellationToken)).Message);
				await DelayWithCancellationAsync(1.5, cancellationToken);
				return true;
			}
			await DelayWithCancellationAsync(1.0, cancellationToken);
		}
		return false;
	}

	private async Task<bool> TrySelectWindowed1920x1080Async(CancellationToken cancellationToken)
	{
		const int maximumScrollCount = 20;
		for (int scanIndex = 0; scanIndex <= maximumScrollCount; scanIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
			OcrScanResult scan = await CaptureAndOcrAsync(ResolutionListRegion, cancellationToken);
			OcrTextItem? windowedTarget = scan.Items.FirstOrDefault(IsWindowed1920x1080Item);
			OcrTextItem? fullscreenTarget = scan.Items.FirstOrDefault(IsFullscreen1920x1080Item);
			OcrTextItem? target = windowedTarget ?? fullscreenTarget;
			bool convertFromFullscreen = windowedTarget == null && fullscreenTarget != null;
			AppendLog($"游戏窗口优化：分辨率列表第 {scanIndex + 1} 次中上半屏检测：{(target == null ? "未找到 1920x1080" : "找到 " + target.Text + (convertFromFullscreen ? "，选择后将用 Alt+Enter 转为窗口化" : ""))}。OCR：{ShortText(scan.RawText)}");
			if (target != null && _latestCaptureScreenRegion != null)
			{
				Rect bounds = target.Bounds;
				int screenX = _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0);
				int screenY = _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0);
				string clickReason = convertFromFullscreen ? "游戏窗口优化：选择 1920x1080 全屏幕" : "游戏窗口优化：选择 1920x1080 窗口化";
				AppendLog((await _clickService.ClickAsync(new ClickRequest(clickReason, screenX, screenY), _gameWindow.Handle, cancellationToken)).Message);
				if (convertFromFullscreen)
				{
					await DelayWithCancellationAsync(1.0, cancellationToken);
					AppendLog((await _clickService.PressAltEnterAsync(_gameWindow.Handle, cancellationToken)).Message);
				}
				return true;
			}
			if (scanIndex == maximumScrollCount)
			{
				break;
			}
			WindowClientRect rect = _gameWindow.ClientRect;
			int scrollX = rect.Left + (int)Math.Round(rect.Width * 0.84);
			int scrollY = rect.Top + (int)Math.Round(rect.Height * 0.43);
			AppendLog($"游戏窗口优化：未找到目标，执行第 {scanIndex + 1}/{maximumScrollCount} 次向下滚动，随后停下重新检测。");
			AppendLog((await _clickService.ScrollAsync(scrollX, scrollY, -600, _gameWindow.Handle, cancellationToken)).Message);
			await DelayWithCancellationAsync(1.5, cancellationToken);
		}
		return false;
	}

	private static bool IsWindowed1920x1080Item(OcrTextItem item)
	{
		string normalized = Regex.Replace(item.Text.ToUpperInvariant().Replace('×', 'X'), "\\s+", "");
		if (normalized.Contains("全屏", StringComparison.Ordinal))
		{
			return false;
		}
		return IsExact1920x1080Text(normalized);
	}

	private static bool IsFullscreen1920x1080Item(OcrTextItem item)
	{
		string normalized = Regex.Replace(item.Text.ToUpperInvariant().Replace('×', 'X'), "\\s+", "");
		return normalized.Contains("全屏", StringComparison.Ordinal) && IsExact1920x1080Text(normalized);
	}

	private static bool IsExact1920x1080Text(string normalized)
	{
		MatchCollection numbers = Regex.Matches(normalized, "\\d+");
		return numbers.Count == 2
			&& numbers[0].Value == "1920"
			&& numbers[1].Value == "1080"
			&& Regex.IsMatch(normalized, "1920[X*]1080", RegexOptions.CultureInvariant);
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
		if (strategies.Count == 0)
		{
			MessageBox.Show(this, "请至少添加 1 个局内策略目标。局内投资目标可以留空，留空时会自动选择安全投资并直接进入局内。", "局内自定义", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		RefreshWordHistoryControls();
		await StartIndependentStrategyPresetAsync(string.Join("、", strategies), strategies, investments);
	}

	private async void StartCombined_Click(object sender, RoutedEventArgs e)
	{
		CombinedOuterInvestmentRule outerInvestmentRule = GetComboRule(CombinedOuterInvestmentRuleBox, CombinedOuterInvestmentRule.StopOnMatch);
		if ((outerInvestmentRule == CombinedOuterInvestmentRule.RequireThenContinue || outerInvestmentRule == CombinedOuterInvestmentRule.StopOnMatch) && CombinedOuterInvestmentListBox.Items.Count == 0)
		{
			MessageBox.Show(this, "当前局外投资规则需要识别目标，请至少添加 1 个局外投资词条，或者选择“忽略局外投资”。", "局外+局内", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		ShowLogPage();
		await StartCombinedModeAsync();
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

	private void CombinedTargetInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddCombinedTarget_Click(sender, e);
			e.Handled = true;
		}
	}

	private void CombinedBlockedInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddCombinedBlocked_Click(sender, e);
			e.Handled = true;
		}
	}

	private void CombinedOuterInvestmentInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddCombinedOuterInvestment_Click(sender, e);
			e.Handled = true;
		}
	}

	private void CombinedInvestmentInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddCombinedInvestment_Click(sender, e);
			e.Handled = true;
		}
	}

	private void CombinedStrategyInputBox_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			AddCombinedStrategy_Click(sender, e);
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

	private void MoveInvestmentWordUp_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(InvestmentWordsListBox, -1, "投资词条");
	}

	private void MoveInvestmentWordDown_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(InvestmentWordsListBox, 1, "投资词条");
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

	private void MoveInGameInvestmentUp_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(InGameInvestmentListBox, -1, "局内投资");
	}

	private void MoveInGameInvestmentDown_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(InGameInvestmentListBox, 1, "局内投资");
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

	private void MoveInGameStrategyUp_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(InGameStrategyListBox, -1, "局内策略");
	}

	private void MoveInGameStrategyDown_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(InGameStrategyListBox, 1, "局内策略");
	}

	private void AddCombinedTarget_Click(object sender, RoutedEventArgs e)
	{
		AddWord(CombinedTargetInputBox, CombinedTargetListBox, CombinedDebuffMatchAnyBox.IsChecked == true ? 20 : 4, "组合主词条");
	}

	private void DeleteCombinedTarget_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(CombinedTargetListBox, "组合主词条");
	}

	private void ClearCombinedTarget_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(CombinedTargetListBox, "组合主词条");
	}

	private void AddCombinedBlocked_Click(object sender, RoutedEventArgs e)
	{
		AddWord(CombinedBlockedInputBox, CombinedBlockedListBox, 20, "组合不想要词条");
	}

	private void DeleteCombinedBlocked_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(CombinedBlockedListBox, "组合不想要词条");
	}

	private void ClearCombinedBlocked_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(CombinedBlockedListBox, "组合不想要词条");
	}

	private void AddCombinedOuterInvestment_Click(object sender, RoutedEventArgs e)
	{
		AddWord(CombinedOuterInvestmentInputBox, CombinedOuterInvestmentListBox, 20, "组合局外投资");
	}

	private void DeleteCombinedOuterInvestment_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(CombinedOuterInvestmentListBox, "组合局外投资");
	}

	private void ClearCombinedOuterInvestment_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(CombinedOuterInvestmentListBox, "组合局外投资");
	}

	private void MoveCombinedOuterInvestmentUp_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(CombinedOuterInvestmentListBox, -1, "组合局外投资");
	}

	private void MoveCombinedOuterInvestmentDown_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(CombinedOuterInvestmentListBox, 1, "组合局外投资");
	}

	private void AddCombinedInvestment_Click(object sender, RoutedEventArgs e)
	{
		AddWord(CombinedInvestmentInputBox, CombinedInvestmentListBox, 20, "组合局内投资");
	}

	private void DeleteCombinedInvestment_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(CombinedInvestmentListBox, "组合局内投资");
	}

	private void ClearCombinedInvestment_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(CombinedInvestmentListBox, "组合局内投资");
	}

	private void AddCombinedStrategy_Click(object sender, RoutedEventArgs e)
	{
		AddWord(CombinedStrategyInputBox, CombinedStrategyListBox, 20, "组合局内策略");
	}

	private void DeleteCombinedStrategy_Click(object sender, RoutedEventArgs e)
	{
		DeleteSelectedWords(CombinedStrategyListBox, "组合局内策略");
	}

	private void ClearCombinedStrategy_Click(object sender, RoutedEventArgs e)
	{
		ClearWords(CombinedStrategyListBox, "组合局内策略");
	}

	private void MoveCombinedStrategyUp_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(CombinedStrategyListBox, -1, "组合局内策略");
	}

	private void MoveCombinedStrategyDown_Click(object sender, RoutedEventArgs e)
	{
		MoveSelectedWord(CombinedStrategyListBox, 1, "组合局内策略");
	}

	private void CombinedConfig_Changed(object sender, RoutedEventArgs e)
	{
		if (base.IsLoaded)
		{
			SaveConfigFromUi("局外+局内配置已更新。");
		}
	}

	private void CombinedFlowRule_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (base.IsLoaded)
		{
			SaveConfigFromUi("局外+局内流程条件已更新。");
		}
	}

	private void SaveCombinedConfig_Click(object sender, RoutedEventArgs e)
	{
		ReadUiToConfig();
		_configStore.Save(_config);
		LoadConfigToUi();
		AppendLog("局外+局内配置已保存。");
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
		CombinedDebuffEnabledBox.IsChecked = _config.CombinedDebuffEnabled;
		CombinedDebuffMatchAnyBox.IsChecked = _config.CombinedDebuffMatchAny;
		SetListBoxItems(CombinedTargetListBox, _config.CombinedTargetWords);
		CombinedBlockedEnabledBox.IsChecked = _config.CombinedBlockedEnabled;
		CombinedCheckInvestmentWhenBlockedBox.IsChecked = _config.CombinedCheckInvestmentWhenBlocked;
		SelectComboRule(CombinedMainRuleBox, _config.CombinedMainRule);
		SelectComboRule(CombinedBlockedRuleBox, _config.CombinedBlockedRule);
		SelectComboRule(CombinedOuterInvestmentRuleBox, _config.CombinedOuterInvestmentRule);
		SelectComboRule(CombinedInGameInvestmentRuleBox, _config.CombinedInGameInvestmentRule);
		SetListBoxItems(CombinedBlockedListBox, _config.CombinedBlockedWords);
		SetListBoxItems(CombinedOuterInvestmentListBox, _config.CombinedInvestmentTargets);
		SetListBoxItems(CombinedStrategyListBox, _config.CombinedInGameStrategyTargets);
		SetListBoxItems(CombinedInvestmentListBox, _config.CombinedInvestmentTargets);
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
		_config.CombinedMainRule = GetComboRule(CombinedMainRuleBox, CombinedMainRule.StopOnMatch);
		_config.CombinedBlockedRule = GetComboRule(CombinedBlockedRuleBox, CombinedBlockedRule.RestartOnMatch);
		_config.CombinedOuterInvestmentRule = GetComboRule(CombinedOuterInvestmentRuleBox, CombinedOuterInvestmentRule.StopOnMatch);
		_config.CombinedInGameInvestmentRule = _config.CombinedOuterInvestmentRule switch
		{
			CombinedOuterInvestmentRule.Ignore => CombinedInGameInvestmentRule.Ignore,
			CombinedOuterInvestmentRule.RequireThenContinue => CombinedInGameInvestmentRule.RequireThenContinue,
			_ => CombinedInGameInvestmentRule.OptionalContinue
		};
		_config.CombinedFlowRulesConfigured = true;
		_config.CombinedDebuffEnabled = _config.CombinedMainRule != CombinedMainRule.Ignore;
		_config.CombinedDebuffMatchAny = CombinedDebuffMatchAnyBox.IsChecked == true;
		_config.CombinedTargetWords = ReadWords(CombinedTargetListBox);
		_config.CombinedBlockedEnabled = _config.CombinedBlockedRule != CombinedBlockedRule.Ignore;
		_config.CombinedCheckInvestmentWhenBlocked = _config.CombinedBlockedRule == CombinedBlockedRule.ContinueOnMatch;
		_config.CombinedBlockedWords = ReadWords(CombinedBlockedListBox);
		_config.CombinedInvestmentTargets = ReadWords(CombinedOuterInvestmentListBox);
		_config.CombinedInGameStrategyTargets = ReadWords(CombinedStrategyListBox);
		_config.CombinedInGameInvestmentTargets = _config.CombinedInvestmentTargets.ToList();
		_config.InGameStrategyTarget = "";
		_config.InGameInvestmentTarget = "";
		_config.Normalize();
		UpdateWordCounts();
		SetListBoxItems(TargetWordsListBox, _config.TargetWords);
		SetListBoxItems(BlockedWordsListBox, _config.BlockedWords);
		SetListBoxItems(InvestmentWordsListBox, _config.InvestmentTargets);
		SetListBoxItems(InGameStrategyListBox, _config.InGameStrategyTargets);
		SetListBoxItems(InGameInvestmentListBox, _config.InGameInvestmentTargets);
		SetListBoxItems(CombinedTargetListBox, _config.CombinedTargetWords);
		SetListBoxItems(CombinedBlockedListBox, _config.CombinedBlockedWords);
		SetListBoxItems(CombinedOuterInvestmentListBox, _config.CombinedInvestmentTargets);
		SetListBoxItems(CombinedStrategyListBox, _config.CombinedInGameStrategyTargets);
		SetListBoxItems(CombinedInvestmentListBox, _config.CombinedInGameInvestmentTargets);
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

	private static T GetComboRule<T>(ComboBox comboBox, T fallback) where T : struct, Enum
	{
		if (comboBox.SelectedItem is ComboBoxItem item && item.Tag is string value && Enum.TryParse(value, out T parsed))
		{
			return parsed;
		}
		return fallback;
	}

	private static void SelectComboRule<T>(ComboBox comboBox, T value) where T : struct, Enum
	{
		string expected = value.ToString();
		foreach (ComboBoxItem item in comboBox.Items.OfType<ComboBoxItem>())
		{
			if (string.Equals(item.Tag as string, expected, StringComparison.Ordinal))
			{
				comboBox.SelectedItem = item;
				return;
			}
		}
		comboBox.SelectedIndex = 0;
	}

	private static string GetCombinedRuleText(Enum rule)
	{
		return rule switch
		{
			CombinedMainRule.Ignore => "忽略",
			CombinedMainRule.RequireThenContinue => "必须命中后继续",
			CombinedMainRule.StopOnMatch => "命中立即停止",
			CombinedMainRule.OptionalContinue => "尝试即可",
			CombinedBlockedRule.Ignore => "忽略",
			CombinedBlockedRule.RestartOnMatch => "命中就重开",
			CombinedBlockedRule.ContinueOnMatch => "命中仍继续",
			CombinedOuterInvestmentRule.Ignore => "忽略",
			CombinedOuterInvestmentRule.RequireThenContinue => "必须命中后继续",
			CombinedOuterInvestmentRule.OptionalContinue => "尝试即可",
			CombinedOuterInvestmentRule.StopOnMatch => "命中立即停止",
			CombinedInGameInvestmentRule.Ignore => "忽略",
			CombinedInGameInvestmentRule.RequireThenContinue => "必须命中后继续",
			CombinedInGameInvestmentRule.OptionalContinue => "尝试即可",
			_ => rule.ToString()
		};
	}

	private void RefreshWordHistoryControls()
	{
		SetComboBoxItems(TargetWordInputBox, _config.TargetWordHistory);
		SetComboBoxItems(BlockedWordInputBox, _config.BlockedWordHistory);
		SetComboBoxItems(InvestmentWordInputBox, _config.InvestmentWordHistory);
		SetComboBoxItems(InGameStrategyInputBox, _config.InGameStrategyHistory);
		SetComboBoxItems(InGameInvestmentInputBox, _config.InGameInvestmentHistory);
		SetComboBoxItems(CombinedTargetInputBox, _config.CombinedTargetWordHistory);
		SetComboBoxItems(CombinedBlockedInputBox, _config.CombinedBlockedWordHistory);
		SetComboBoxItems(CombinedOuterInvestmentInputBox, _config.CombinedInvestmentWordHistory);
		SetComboBoxItems(CombinedStrategyInputBox, _config.CombinedInGameStrategyHistory);
		SetComboBoxItems(CombinedInvestmentInputBox, _config.CombinedInGameInvestmentHistory);
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

	private void MoveSelectedWord(ListBox listBox, int direction, string label)
	{
		int selectedIndex = listBox.SelectedIndex;
		if (selectedIndex < 0)
		{
			return;
		}
		int targetIndex = selectedIndex + direction;
		if (targetIndex < 0 || targetIndex >= listBox.Items.Count)
		{
			return;
		}
		string selectedWord = (string)listBox.Items[selectedIndex];
		listBox.Items.RemoveAt(selectedIndex);
		listBox.Items.Insert(targetIndex, selectedWord);
		SaveConfigFromUi($"{label}优先级已调整：{selectedWord}");
		listBox.SelectedItem = selectedWord;
		listBox.ScrollIntoView(selectedWord);
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
			string display = _windowCapture.DescribeDisplay(_gameWindow);
			WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={rect.Width}x{rect.Height}  left={rect.Left}, top={rect.Top}  {display}";
			SetStatus("状态：已找到窗口");
			AppendLog($"找到窗口：{_gameWindow.Title}，client={rect.Width}x{rect.Height}，left={rect.Left}, top={rect.Top}；{display}");
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
			string display = _windowCapture.DescribeDisplay(_gameWindow);
			WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={rect.Width}x{rect.Height}  left={rect.Left}, top={rect.Top}  {display}";
			AppendLog($"独立局内预设：已刷新窗口位置，client={rect.Width}x{rect.Height}，left={rect.Left}, top={rect.Top}；{display}。");
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
		WindowInfoText.Text = $"窗口：{_gameWindow.Title}  client={rect.Width}x{rect.Height}  left={rect.Left}, top={rect.Top}  {_windowCapture.DescribeDisplay(_gameWindow)}";
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
				CaptureInfoText.Text = $"截图：{region.Name}  {resolved.Width}x{resolved.Height}  left={resolved.Left}, top={resolved.Top}  后端={_windowCapture.LastCaptureBackend}";
				SetStatus("状态：截图完成：" + region.Name);
				AppendLog($"截图完成：{region.Name}，{resolved.Width}x{resolved.Height}，left={resolved.Left}, top={resolved.Top}，后端={_windowCapture.LastCaptureBackend}");
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
		StartCombinedButton.IsEnabled = enabled;
		OutGamePage.IsEnabled = enabled;
		InGamePage.IsEnabled = enabled;
		ReservedPage.IsEnabled = enabled;
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
		await WaitForOpeningBoardReadyAsync("自动刷周常积分", InGameOpeningFlow.OpeningBoardPostDetectionWaitSeconds, cancellationToken);
		await DeployOpeningCharactersAsync(cancellationToken);
		await TryHandleGalaStarChoiceAsync(cancellationToken);
		await RunOpeningBattlesUntilTwoContinueClicksAsync(cancellationToken);
		await RunWeeklyStrategyChoiceAsync(cancellationToken);
		await RunIndependentReturnToCurrencyWarsAsync(cancellationToken);
	}

	private async Task RunWeeklyStrategyChoiceAsync(CancellationToken cancellationToken)
	{
		AppendLog("自动刷周常积分：策略页复用局内识别默认黑名单逻辑。");
		if (!(await WaitForMajorPageAsync("自动刷周常积分：策略选择页", InGameOpeningFlow.StrategyScreenAliases, CurrencyWarsFlow.FullWindow, InGameOpeningFlow.StrategyScreenWaitTimeoutSeconds, 0.8, InGameOpeningFlow.StrategyFuzzyScore, cancellationToken)))
		{
			AppendLog("自动刷周常积分：策略选择页等待超时，跳过策略确认。");
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
			if (_automationSuccessStop)
			{
				AppendLog("独立局内预设：成功停止。");
				SetStatus("状态：局内策略命中停止");
				ShowSuccessFeedback("成功刷出目标策略：" + strategyName + "！");
			}
		}
		catch (OperationCanceledException)
		{
			AppendLog(_automationSuccessStop ? "独立局内预设：成功停止。" : "独立局内预设：已手动停止。");
			SetStatus(_automationSuccessStop ? "状态：局内策略命中停止" : "状态：已停止");
			if (_automationSuccessStop)
			{
				ShowSuccessFeedback("成功刷出目标策略：" + strategyName + "！");
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

	private async Task StartCombinedModeAsync()
	{
		if (_automationCts != null)
		{
			return;
		}
		AutomationConfig storedConfig = _config;
		AutomationConfig combinedConfig = CreateCombinedRuntimeConfig(storedConfig);
		if ((object)_gameWindow == null && !TryFindWindow())
		{
			return;
		}
		_automationCts = new CancellationTokenSource();
		_automationSuccessStop = false;
		_combinedSuccessMessage = null;
		_lastSafeInvestmentPoint = null;
		_blockedHitThisCycle = false;
		SetAutomationButtonsEnabled(enabled: false);
		SetStatus("状态：局外+局内运行中");
		_config = combinedConfig;
		bool showSuccessFeedback = false;
		try
		{
			await RunCombinedModeLoopAsync(combinedConfig.InGameStrategyTargets, combinedConfig.InGameInvestmentTargets, _automationCts.Token);
			showSuccessFeedback = _automationSuccessStop;
			if (_automationSuccessStop)
			{
				AppendLog("局外+局内：成功停止。");
				SetStatus("状态：局外+局内命中停止");
			}
		}
		catch (OperationCanceledException)
		{
			showSuccessFeedback = _automationSuccessStop;
			AppendLog(_automationSuccessStop ? "局外+局内：成功停止。" : "局外+局内：已手动停止。");
			SetStatus(_automationSuccessStop ? "状态：局外+局内命中停止" : "状态：已停止");
		}
		catch (Exception ex)
		{
			AppendLog("局外+局内失败：" + ex.Message);
			SetStatus("状态：局外+局内失败");
			MessageBox.Show(this, ex.Message, "局外+局内失败", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		finally
		{
			_config = storedConfig;
			_automationCts?.Dispose();
			_automationCts = null;
			SetAutomationButtonsEnabled(enabled: true);
		}
		if (showSuccessFeedback)
		{
			ShowSuccessFeedback(_combinedSuccessMessage ?? "局外+局内目标命中！");
		}
	}

	private static AutomationConfig CreateCombinedRuntimeConfig(AutomationConfig source)
	{
		AutomationConfig config = new AutomationConfig
		{
			WindowTitle = source.WindowTitle,
			DebuffEnabled = source.CombinedDebuffEnabled,
			DebuffMatchAny = source.CombinedDebuffMatchAny,
			TargetWords = source.CombinedTargetWords.ToList(),
			BlockedEnabled = source.CombinedBlockedEnabled,
			BlockedWords = source.CombinedBlockedWords.ToList(),
			InvestmentEnabled = true,
			InvestmentTargets = source.CombinedInvestmentTargets.ToList(),
			CheckInvestmentWhenBlocked = source.CombinedCheckInvestmentWhenBlocked,
			InGameStrategyTargets = source.CombinedInGameStrategyTargets.ToList(),
			InGameInvestmentTargets = source.CombinedInGameInvestmentTargets.ToList(),
			CombinedMainRule = source.CombinedMainRule,
			CombinedBlockedRule = source.CombinedBlockedRule,
			CombinedOuterInvestmentRule = source.CombinedOuterInvestmentRule,
			CombinedInGameInvestmentRule = source.CombinedInGameInvestmentRule,
			CombinedFlowRulesConfigured = true,
			FuzzyScore = source.FuzzyScore,
			BlockedFuzzyScore = source.BlockedFuzzyScore,
			ButtonFuzzyScore = source.ButtonFuzzyScore,
			InvestmentFuzzyScore = source.InvestmentFuzzyScore,
			StartDelaySeconds = source.StartDelaySeconds,
			DebuffCheckDelaySeconds = source.DebuffCheckDelaySeconds,
			InvestmentIntervalSeconds = source.InvestmentIntervalSeconds
		};
		config.Normalize();
		return config;
	}

	private async Task RunCombinedModeLoopAsync(IReadOnlyList<string> strategyAliases, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
	{
		string strategyName = string.Join("、", strategyAliases);
		bool strategyTargetEmpty = strategyAliases.Count == 0;
		AppendLog($"局外+局内：启动缓冲 1 秒；流程规则：主词条={GetCombinedRuleText(_config.CombinedMainRule)}，不想要={GetCombinedRuleText(_config.CombinedBlockedRule)}，投资={GetCombinedRuleText(_config.CombinedOuterInvestmentRule)}。");
		await DelayWithCancellationAsync(1.0, cancellationToken);
		int round = 1;
		while (!cancellationToken.IsCancellationRequested)
		{
			_blockedHitThisCycle = false;
			bool investmentTargetEmpty = investmentGateAliases.Count == 0;
			AppendLog(investmentTargetEmpty
				? $"局外+局内：第 {round} 轮开始，局内投资目标为空。"
				: $"局外+局内：第 {round} 轮开始，局内投资目标：{string.Join("、", investmentGateAliases)}。");
			await RefreshGameWindowForIndependentLoopAsync(cancellationToken);
			BasicScanEvaluation? evaluation = await RunCombinedOuterFlowBeforeInvestmentAsync(cancellationToken);
			bool blockedRejectsRound = _config.CombinedBlockedRule == CombinedBlockedRule.RestartOnMatch && evaluation?.BlockedHit == true;
			bool mainTargetEmpty = _config.TargetWords.Count == 0;
			bool mainMatched = _config.CombinedMainRule == CombinedMainRule.Ignore || mainTargetEmpty || evaluation?.TargetSatisfied == true;
			bool roundCanContinue = !blockedRejectsRound && (_config.CombinedMainRule != CombinedMainRule.RequireThenContinue || mainMatched);

			if (_config.CombinedMainRule == CombinedMainRule.StopOnMatch && evaluation?.TargetSatisfied == true && !blockedRejectsRound)
			{
				_combinedSuccessMessage = "成功刷出局外主词条：" + string.Join("、", evaluation.TargetMatch.HitWords) + "！";
				AppendLog("局外+局内：" + evaluation.DecisionReason + " 停止。");
				StopAutomationForSuccess("状态：组合主词条成功停止");
				break;
			}
			if (blockedRejectsRound)
			{
				AppendLog("局外+局内：命中不想要词条，按当前规则结束本轮并重开。");
			}
			else if (_config.CombinedMainRule == CombinedMainRule.RequireThenContinue && !mainMatched)
			{
				AppendLog("局外+局内：主词条未满足“必须命中”，结束本轮并重开。");
			}
			else if (_config.CombinedBlockedRule == CombinedBlockedRule.ContinueOnMatch && evaluation?.BlockedHit == true)
			{
				AppendLog("局外+局内：命中不想要词条，但当前规则允许继续后续阶段。");
			}
			if (_config.CombinedMainRule == CombinedMainRule.OptionalContinue && !blockedRejectsRound)
			{
				if (mainTargetEmpty)
				{
					AppendLog("局外+局内：主词条目标为空，按“尝试即可”规则继续后续阶段。");
				}
				else if (evaluation?.TargetSatisfied == true)
				{
					AppendLog("局外+局内：主词条已命中，但当前规则为“尝试即可”，继续后续阶段。");
				}
				else
				{
					AppendLog("局外+局内：主词条未命中，按“尝试即可”规则继续后续阶段。");
				}
			}

			string? investmentHit = null;
			if (!roundCanContinue)
			{
				AppendLog("局外+局内：前置条件未通过，不进入局内。");
			}
			else if (_config.CombinedOuterInvestmentRule == CombinedOuterInvestmentRule.Ignore || investmentTargetEmpty)
			{
				AppendLog("局外+局内：忽略投资目标，但仍执行 3 次黑名单保护扫描；选择安全投资后直接进入局内。");
				await ClickSafeInvestmentAsync(rememberChoice: false, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken, blacklistScanAttempts: 3);
			}
			else
			{
				investmentHit = await ExecuteIndependentInvestmentGateAsync(investmentGateAliases, cancellationToken);
				if (_config.CombinedOuterInvestmentRule == CombinedOuterInvestmentRule.StopOnMatch && !string.IsNullOrWhiteSpace(investmentHit))
				{
					_combinedSuccessMessage = "成功刷出投资词条：" + investmentHit + "！";
					AppendLog("局外+局内：投资词条命中：" + investmentHit + "，停止。");
					StopAutomationForSuccess("状态：组合投资成功停止");
					break;
				}
				if (_config.CombinedOuterInvestmentRule == CombinedOuterInvestmentRule.RequireThenContinue && string.IsNullOrWhiteSpace(investmentHit))
				{
					roundCanContinue = false;
					AppendLog("局外+局内：投资未满足“必须命中”，本轮不进入局内。");
				}
			}

			bool gateHit = roundCanContinue && !strategyTargetEmpty;
			if (roundCanContinue && strategyTargetEmpty)
			{
				AppendLog("局外+局内：局内策略目标为空，本轮不进入局内出战；固定确认后直接退出结算并返回货币战争。");
			}
			bool allowExtraStrategyRefresh = IsExtraStrategyRefreshInvestment(investmentHit);
			await DelayWithCancellationAsync(0.4, cancellationToken);
			RefreshGameWindowForIndependentStep();
			await ClickRatioPointAsync(new RatioPoint(0.565, 0.91), "局外+局内：固定确认", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
			AppendLog("局外+局内：固定确认完成，执行旧版蓝海二段点位 2 轮兜底。");
			await ClickBlueOceanFollowupGuardAsync(cancellationToken);
			if (gateHit)
			{
				AppendLog("局外+局内：投资条件允许，进入局内棋盘和策略识别。");
				await WaitForOpeningBoardReadyAsync("局外+局内", InGameOpeningFlow.OpeningBoardPostDetectionWaitSeconds, cancellationToken);
				await DeployOpeningCharactersAsync(cancellationToken);
				await TryHandleGalaStarChoiceAsync(cancellationToken);
				await RunOpeningBattlesUntilTwoContinueClicksAsync(cancellationToken);
				await RunStrategyRecognitionAsync(strategyName, strategyAliases, allowExtraStrategyRefresh, cancellationToken);
				if (_automationSuccessStop)
				{
					_combinedSuccessMessage = "成功刷出局内目标策略：" + strategyName + "！";
					break;
				}
				AppendLog("局外+局内：本轮策略未命中，退出结算并返回货币战争。");
			}
			else
			{
				AppendLog("局外+局内：本轮不进入局内，直接退出并重开。");
			}
			await RunIndependentReturnToCurrencyWarsAsync(cancellationToken);
			AppendLog($"局外+局内：第 {round} 轮完成，继续下一轮。");
			round++;
		}
	}

	private async Task<BasicScanEvaluation?> RunCombinedOuterFlowBeforeInvestmentAsync(CancellationToken cancellationToken)
	{
		await RapidAdvanceOpeningPagesAsync("局外+局内", cancellationToken);
		BasicScanEvaluation? evaluation = null;
		if (_config.DebuffEnabled || _config.BlockedEnabled)
		{
			evaluation = await WaitForDebuffEvaluationAsync("局外+局内", cancellationToken);
			if (_config.CombinedMainRule == CombinedMainRule.StopOnMatch
				&& evaluation?.TargetSatisfied == true
				&& !(_config.CombinedBlockedRule == CombinedBlockedRule.RestartOnMatch && evaluation.BlockedHit))
			{
				return evaluation;
			}
		}
		await DelayWithCancellationAsync(0.6, cancellationToken);
		await IndependentClickTextStepAsync("下一步", new _003C_003Ez__ReadOnlySingleElementList<string>("下一步"), CurrencyWarsFlow.FullWindow, new RatioPoint(0.88, 0.895), 8.0, 0.6, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickRatioPointAsync(new RatioPoint(0.5, 0.58), "局外+局内：点击空白继续", cancellationToken);
		await DelayWithCancellationAsync(1.8, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickSafeInvestmentAsync(rememberChoice: true, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
		await DelayWithCancellationAsync(InGameOpeningFlow.PresetInvestmentPostSafeChoiceDelaySeconds, cancellationToken);
		RefreshGameWindowForIndependentStep();
		await ClickRatioPointAsync(_lastSafeInvestmentPoint ?? new RatioPoint(0.5, 0.38), "局外+局内：动画后再次点击安全投资", cancellationToken);
		return evaluation;
	}

	private async Task RunIndependentStrategyPresetLoopAsync(string strategyName, IReadOnlyList<string> strategyAliases, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
	{
		AppendLog("独立局内预设：启动缓冲 1 秒，固定使用统一速度。");
		await DelayWithCancellationAsync(1.0, cancellationToken);
		int round = 1;
		while (!cancellationToken.IsCancellationRequested)
		{
			bool skipInvestmentGate = investmentGateAliases.Count == 0;
			AppendLog(skipInvestmentGate
				? $"独立局内预设：第 {round} 轮开始，未设置投资目标，本轮不限制投资品质。"
				: $"独立局内预设：第 {round} 轮开始，投资门槛：{string.Join("、", investmentGateAliases)}。");
			await RefreshGameWindowForIndependentLoopAsync(cancellationToken);
			await RunIndependentOuterFlowBeforeInvestmentAsync(cancellationToken);
			string? investmentGateHit = null;
			if (skipInvestmentGate)
			{
				AppendLog("独立局内预设：投资目标为空，跳过投资扫描和刷新，自动选择安全投资后直接进入局内。");
				await ClickSafeInvestmentAsync(rememberChoice: false, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
			}
			else
			{
				AppendLog("独立局内预设：检查固定投资门槛。");
				investmentGateHit = await ExecuteIndependentInvestmentGateAsync(investmentGateAliases, cancellationToken);
			}
			bool gateHit = skipInvestmentGate || !string.IsNullOrWhiteSpace(investmentGateHit);
			bool allowExtraStrategyRefresh = IsExtraStrategyRefreshInvestment(investmentGateHit);
			await DelayWithCancellationAsync(0.4, cancellationToken);
			RefreshGameWindowForIndependentStep();
			await ClickRatioPointAsync(new RatioPoint(0.565, 0.91), "独立局内预设：固定确认", cancellationToken);
			await DelayWithCancellationAsync(0.2, cancellationToken);
			AppendLog("独立局内预设：固定确认完成，执行旧版蓝海二段点位 2 轮兜底。");
			await ClickBlueOceanFollowupGuardAsync(cancellationToken);
			if (gateHit)
			{
				AppendLog("独立局内预设：投资门槛命中，识别局内棋盘后进入 1-1 / 1-2。");
				await WaitForOpeningBoardReadyAsync("独立局内预设", InGameOpeningFlow.OpeningBoardPostDetectionWaitSeconds, cancellationToken);
				await DeployOpeningCharactersAsync(cancellationToken);
				await TryHandleGalaStarChoiceAsync(cancellationToken);
				await RunOpeningBattlesUntilTwoContinueClicksAsync(cancellationToken);
				await RunStrategyRecognitionAsync(strategyName, strategyAliases, allowExtraStrategyRefresh, cancellationToken);
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
		await RapidAdvanceOpeningPagesAsync("独立局内预设", cancellationToken);
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

	private async Task RapidAdvanceOpeningPagesAsync(string scope, CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		int clickCount = (int)Math.Ceiling(CurrencyWarsFlow.OpeningRapidAdvanceDurationSeconds / CurrencyWarsFlow.OpeningRapidAdvanceClickIntervalSeconds);
		AppendLog($"{scope}：前三页使用共同固定点位持续点击 {CurrencyWarsFlow.OpeningRapidAdvanceDurationSeconds:g} 秒，间隔 {CurrencyWarsFlow.OpeningRapidAdvanceClickIntervalSeconds:g} 秒，共 {clickCount} 次；结束后直接开始主词条 OCR。");
		for (int i = 0; i < clickCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(CurrencyWarsFlow.OpeningRapidAdvancePoint, $"{scope}：开局连续点击 {i + 1}/{clickCount}", cancellationToken);
			if (i + 1 < clickCount)
			{
				await DelayWithCancellationAsync(CurrencyWarsFlow.OpeningRapidAdvanceClickIntervalSeconds, cancellationToken);
			}
		}
		double clickSpanSeconds = Math.Max(0.0, (clickCount - 1) * CurrencyWarsFlow.OpeningRapidAdvanceClickIntervalSeconds);
		double remainingSeconds = CurrencyWarsFlow.OpeningRapidAdvanceDurationSeconds - clickSpanSeconds;
		if (remainingSeconds > 0.0)
		{
			await DelayWithCancellationAsync(remainingSeconds, cancellationToken);
		}
	}

	private async Task RunIndependentReturnToCurrencyWarsAsync(CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		await ExecuteFastExitToSettlementAsync(cancellationToken);
		await DelayWithCancellationAsync(0.4, cancellationToken);
		await ClickBottomReturnSequenceWhenNextDetectedAsync("独立局内预设", new _003C_003Ez__ReadOnlySingleElementList<string>("下一步"), 8.0, cancellationToken);
		await EnsureReturnedToCurrencyWarsAsync("独立局内预设", cancellationToken);
		await RestartOcrAtSafePointIfDueAsync("局内循环", cancellationToken);
	}

	private async Task ClickFixedBottomReturnSequenceAsync(string scope, CancellationToken cancellationToken)
	{
		RatioPoint point = new RatioPoint(0.5, 0.829);
		AppendLog($"{scope}：识别到“下一步”后立即使用底部固定点位连续点击 {CurrencyWarsFlow.BottomReturnFixedClickCount} 次，不等待“下一页”。");
		for (int i = 0; i < CurrencyWarsFlow.BottomReturnFixedClickCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(point, $"{scope}：结算返回固定连点 {i + 1}/{CurrencyWarsFlow.BottomReturnFixedClickCount}", cancellationToken);
			await DelayWithCancellationAsync(CurrencyWarsFlow.FastExitProbeIntervalSeconds, cancellationToken);
		}
		await DelayWithCancellationAsync(0.7, cancellationToken);
	}

	private async Task EnsureReturnedToCurrencyWarsAsync(string scope, CancellationToken cancellationToken)
	{
		string[] homeAliases = new string[1] { "开始货币战争" };
		string[] staleProgressAliases = new string[3] { "当前进度", "继续进度", "结束并结算" };
		DateTime deadline = DateTime.UtcNow.AddSeconds(4.0);
		string lastText = "";
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
			lastText = scan.RawText;
			if ((object)OcrClickResolver.FindBest(scan, homeAliases, _config.ButtonFuzzyScore) != null)
			{
				AppendLog(scope + "：固定连点后已确认返回货币战争首页。");
				return;
			}
			if ((object)OcrClickResolver.FindBest(scan, staleProgressAliases, _config.ButtonFuzzyScore) != null)
			{
				throw new InvalidOperationException("结算返回未完成，仍检测到当前进度/继续进度页面。已阻止开始下一轮，避免误点进入标准博弈。最后 OCR：" + ShortText(lastText));
			}
			await DelayWithCancellationAsync(0.3, cancellationToken);
		}
		throw new InvalidOperationException("固定连点后未能确认返回货币战争首页。已阻止开始下一轮。最后 OCR：" + ShortText(lastText));
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
		AppendLog("独立局内预设：投资门槛未命中，已选择默认安全投资，等待后续固定确认。");
		return null;
	}

	private async Task<string?> TryClickIndependentInvestmentTargetAsync(string scope, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
	{
		AppendLog($"独立局内预设：{scope}开始，固定扫描 {InGameOpeningFlow.PresetInvestmentScanAttemptCount} 次。");
		OcrClickCandidate? bestCandidate = null;
		int bestPriority = int.MaxValue;
		for (int attempt = 1; attempt <= InGameOpeningFlow.PresetInvestmentScanAttemptCount; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AppendLog($"独立局内预设：{scope}第 {attempt} 次扫描上半屏。");
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
			AppendLog($"独立局内预设：{scope}第 {attempt} 次 OCR 原文：{ShortText(scan.RawText)}");
			OcrClickCandidate? candidate = OcrClickResolver.FindByPriority(scan, investmentGateAliases, 88);
			if (candidate != null)
			{
				int priority = GetAliasPriority(investmentGateAliases, candidate.Alias);
				if (priority < bestPriority)
				{
					bestCandidate = candidate;
					bestPriority = priority;
				}
				if (bestPriority == 0)
				{
					break;
				}
			}
			if (attempt < InGameOpeningFlow.PresetInvestmentScanAttemptCount)
			{
				await DelayWithCancellationAsync(0.08, cancellationToken);
			}
		}
		if (bestCandidate != null && _latestCaptureScreenRegion != null)
		{
			Rect bounds = bestCandidate.Item.Bounds;
			await ExecuteClickAsync(new ClickRequest("独立局内预设：投资门槛：" + bestCandidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
			AppendLog($"独立局内预设：按优先级选择第 {bestPriority + 1} 项：{bestCandidate.Alias}。");
			return bestCandidate.Alias;
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
				IReadOnlyList<string> verificationAliases = GetPostClickVerificationAliases(name, aliases);
				if (await ClickTextUntilPageChangesAsync("独立局内预设：" + name, candidate, verificationAliases, searchRegion, null, cancellationToken))
				{
					await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
					return;
				}
				AppendLog("独立局内预设：" + name + " 点击后原按钮仍存在，继续在超时时间内重试。");
			}
			await DelayWithCancellationAsync(0.6, cancellationToken);
		}
		if ((object)fallbackPoint != null)
		{
			AppendLog("独立局内预设：" + name + " OCR 未命中，使用兜底坐标。最后 OCR：" + ShortText(lastText));
			bool fallbackSucceeded = await ClickFixedPointUntilPageChangesAsync("独立局内预设：" + name + " 兜底", fallbackPoint, GetPostClickVerificationAliases(name, aliases), searchRegion, cancellationToken);
			if (!fallbackSucceeded && GetExpectedPostClickAliases(name).Count > 0)
			{
				throw new InvalidOperationException("独立局内预设：" + name + " 兜底点击后没有识别到目标下一页，已停止继续执行，避免跳错步骤。");
			}
			await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
			return;
		}
		throw new InvalidOperationException("独立局内预设超时：没有找到按钮文字“" + name + "”。最后 OCR：" + ShortText(lastText));
	}

	private async Task ClickBottomReturnSequenceWhenNextDetectedAsync(string scope, IReadOnlyList<string> aliases, double timeoutSeconds, CancellationToken cancellationToken)
	{
		RefreshGameWindowForIndependentStep();
		DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
		string lastText = "";
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
			lastText = scan.RawText;
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, aliases, _config.ButtonFuzzyScore);
			if ((object)candidate != null)
			{
				AppendLog($"{scope}：已识别到“下一步”（匹配 {candidate.Alias}），不等待下一页，立即开始固定连点。");
				await ClickFixedBottomReturnSequenceAsync(scope, cancellationToken);
				return;
			}
			await DelayWithCancellationAsync(0.15, cancellationToken);
		}
		AppendLog(scope + "：下一步 OCR 未命中，直接使用底部固定连点兜底。最后 OCR：" + ShortText(lastText));
		await ClickFixedBottomReturnSequenceAsync(scope, cancellationToken);
	}

	private async Task DeployOpeningCharactersAsync(CancellationToken cancellationToken)
	{
		AppendLog("局内识别：固定拖拽底部前 4 个备战席到前台前 4 格。");
		int count = Math.Min(4, InGameOpeningFlow.ForwardSlots.Length);
		for (int i = 0; i < count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ExecuteDragRatioAsync(InGameOpeningFlow.PrepareSlots[i], InGameOpeningFlow.ForwardSlots[i], $"局内识别：备战席 {i + 1} -> 前台 {i + 1}", cancellationToken);
			await DelayWithCancellationAsync(0.5, cancellationToken);
		}
		AppendLog("局内识别：追加拖拽备战席 5 到前台 1。");
		await ExecuteDragRatioAsync(InGameOpeningFlow.PrepareSlots[4], InGameOpeningFlow.ForwardSlots[0], "局内识别：追加备战席 5 -> 前台 1", cancellationToken);
		await DelayWithCancellationAsync(0.5, cancellationToken);
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
		AppendLog($"局内识别：准备点击第 {nextCount} 次出战，固定快速点击 {InGameOpeningFlow.BattleButtonClickCount} 次后确认按钮是否消失。");
		OcrClickCandidate candidate = OcrClickResolver.FindBest(await CaptureAndOcrAsync(InGameOpeningFlow.BattleButtonRegion, cancellationToken), InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore);
		if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
		{
			Rect bounds = candidate.Item.Bounds;
			ClickRequest request = new ClickRequest($"局内识别：第 {nextCount} 次{candidate.Item.Text}", _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0));
			await ExecuteRepeatedClickAsync(request, InGameOpeningFlow.BattleButtonClickCount, InGameOpeningFlow.BattleButtonClickIntervalSeconds, cancellationToken);
		}
		else
		{
			await ClickRatioPointRepeatedAsync(InGameOpeningFlow.BattleButton, $"局内识别：第 {nextCount} 次出战兜底", InGameOpeningFlow.BattleButtonClickCount, InGameOpeningFlow.BattleButtonClickIntervalSeconds, cancellationToken);
		}
		await TryHandleUnderfilledTeamConfirmAsync(cancellationToken);
		await DelayWithCancellationAsync(0.8, cancellationToken);
		OcrClickCandidate remaining = OcrClickResolver.FindBest(await CaptureAndOcrAsync(InGameOpeningFlow.BattleButtonRegion, cancellationToken), InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore);
		if ((object)remaining == null)
		{
			AppendLog($"局内识别：第 {nextCount} 次出战按钮已消失，确认三连点生效。");
			return true;
		}
		AppendLog($"局内识别：第 {nextCount} 次出战三连点后按钮仍存在（{remaining.Item.Text}），本次不计入已出战次数，交回主循环继续识别。");
		return false;
	}

	private async Task EnsureAutoBattleEnabledAsync(CancellationToken cancellationToken)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(InGameOpeningFlow.AutoBattleDetectionTimeoutSeconds);
		int completedScans = 0;
		int switchAttempts = 0;
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
			CaptureRegion region = new CaptureRegion("自动战斗关闭标识", InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.X, InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.Y, InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.Width, InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.Height);
			BitmapSource image = _windowCapture.Capture(_gameWindow, region);
			AutoBattleDetectionResult detection = AutoBattleStateDetector.Detect(image);
			completedScans++;
			if (!detection.IsDisabled)
			{
				if (switchAttempts > 0)
				{
					AppendLog($"局内识别：自动战斗关闭标识已消失，确认已尝试开启；本场共按 V {switchAttempts} 次。最后相似度 {detection.Similarity:0.000}。");
					return;
				}
				await DelayWithCancellationAsync(InGameOpeningFlow.AutoBattleDetectionIntervalSeconds, cancellationToken);
				continue;
			}

			AppendLog($"局内识别：检测到自动战斗关闭固定图标，相似度 {detection.Similarity:0.000}，准备按 V 开启。");
			if (switchAttempts < InGameOpeningFlow.AutoBattleMaxSwitchAttempts)
			{
				switchAttempts++;
				AppendLog((await _clickService.PressKeyAsync("V", _gameWindow.Handle, cancellationToken)).Message);
				await DelayWithCancellationAsync(InGameOpeningFlow.AutoBattleVerificationDelaySeconds, cancellationToken);
				continue;
			}

			AppendLog($"局内识别：自动战斗关闭图标仍存在，但本场已达到 {InGameOpeningFlow.AutoBattleMaxSwitchAttempts} 次按 V 上限，不再切换。");
			await DelayWithCancellationAsync(InGameOpeningFlow.AutoBattleDetectionIntervalSeconds, cancellationToken);
		}
		if (switchAttempts == 0)
		{
			AppendLog($"局内识别：{completedScans} 次固定图标扫描均未发现自动战斗关闭标识，本场未发送切换按键。");
			return;
		}
		AppendLog($"局内识别：自动战斗固定图标检测超时，共扫描 {completedScans} 次、按 V {switchAttempts} 次。");
	}

	private async Task CheckAutoBattleDisabledIndicatorOnceAsync(CancellationToken cancellationToken)
	{
		_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
		CaptureRegion region = new CaptureRegion("自动战斗关闭标识", InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.X, InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.Y, InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.Width, InGameOpeningFlow.AutoBattleDisabledIndicatorRegion.Height);
		BitmapSource image = _windowCapture.Capture(_gameWindow, region);
		AutoBattleDetectionResult detection = AutoBattleStateDetector.Detect(image);
		if (!detection.IsDisabled)
		{
			return;
		}

		AppendLog($"局内识别：持续检测命中自动战斗关闭固定图标，相似度 {detection.Similarity:0.000}，按 V 开启。");
		AppendLog((await _clickService.PressKeyAsync("V", _gameWindow.Handle, cancellationToken)).Message);
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
			if (battleStartCount > continueChallengeCount)
			{
				await CheckAutoBattleDisabledIndicatorOnceAsync(cancellationToken);
			}
			if (await TryHandleRoleChoicePopupAsync(scan, cancellationToken))
			{
				continue;
			}
			OcrClickCandidate continueCandidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.ContinueButtonAliases, _config.ButtonFuzzyScore);
			if ((object)continueCandidate != null && (object)_latestCaptureScreenRegion != null)
			{
				string clickedAlias = continueCandidate.Alias;
				if (!(await ClickTextUntilPageChangesAsync("局内识别：" + continueCandidate.Item.Text, continueCandidate, new string[1] { clickedAlias }, CurrencyWarsFlow.FullWindow, null, cancellationToken)))
				{
					AppendLog("局内识别：" + continueCandidate.Item.Text + " 连续点击后仍存在，本次不计数，继续检测当前画面。");
					await DelayWithCancellationAsync(0.6, cancellationToken);
					continue;
				}
				if (TextMatcher.Normalize(clickedAlias) == TextMatcher.Normalize("继续挑战"))
				{
					continueChallengeCount++;
					AppendLog($"局内识别：已点击继续挑战 {continueChallengeCount}/2 次。");
					if (continueChallengeCount >= 2)
					{
						AppendLog("局内识别：已点击 2 次继续挑战，停止局内开局流程，避免第三次出战。");
						return;
					}
				}
				await DelayWithCancellationAsync(0.2, cancellationToken);
			}
			else if ((object)OcrClickResolver.FindBest(scan, InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore) != null)
			{
				if (await ClickInGameBattleButtonAsync(battleStartCount, cancellationToken))
				{
					battleStartCount++;
					AppendLog($"局内识别：已点击出战 {battleStartCount} 次。");
					DateTime battleWaitStartedAt = DateTime.UtcNow;
					await EnsureAutoBattleEnabledAsync(cancellationToken);
					double remainingBattleWaitSeconds = InGameOpeningFlow.AfterBattleClickSeconds - (DateTime.UtcNow - battleWaitStartedAt).TotalSeconds;
					if (remainingBattleWaitSeconds > 0.0)
					{
						await DelayWithCancellationAsync(remainingBattleWaitSeconds, cancellationToken);
					}
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

	private async Task RunStrategyRecognitionAsync(string strategyName, IReadOnlyList<string> strategyAliases, bool allowExtraRefresh, CancellationToken cancellationToken)
	{
		AppendLog("局内识别：开始策略识别，目标：" + strategyName + "。");
		if (!(await WaitForMajorPageAsync("局内识别：策略选择页", InGameOpeningFlow.StrategyScreenAliases, CurrencyWarsFlow.FullWindow, InGameOpeningFlow.StrategyScreenWaitTimeoutSeconds, 0.8, InGameOpeningFlow.StrategyFuzzyScore, cancellationToken)))
		{
			AppendLog("局内识别：策略选择页等待超时，本轮不做策略命中判断。");
			return;
		}
		if (await TryClickTargetStrategyAsync("首次策略识别", strategyAliases, 2, cancellationToken))
		{
			StopAutomationForSuccess("状态：局内策略命中停止");
			return;
		}
		AppendLog("局内识别：首次策略未命中，点击 3 个刷新按钮。");
		RatioPoint[] strategyRefreshButtons = InGameOpeningFlow.StrategyRefreshButtons;
		for (int positionIndex = 0; positionIndex < strategyRefreshButtons.Length; positionIndex++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(strategyRefreshButtons[positionIndex], "局内识别：刷新策略", cancellationToken);
			if (positionIndex < strategyRefreshButtons.Length - 1)
			{
				await DelayWithCancellationAsync(0.2, cancellationToken);
			}
		}
		await DelayWithCancellationAsync(InGameOpeningFlow.StrategyRefreshDelaySeconds, cancellationToken);
		if (await TryClickTargetStrategyAsync("左中右刷新后策略识别", strategyAliases, 1, cancellationToken))
		{
			StopAutomationForSuccess("状态：局内策略命中停止");
			return;
		}
		if (!allowExtraRefresh)
		{
			AppendLog("局内识别：本轮投资不是“银·金·彩”，跳过左中右额外刷新。");
		}
		string[] refreshPositionNames = new string[3] { "左侧", "中间", "右侧" };
		for (int i = 0; allowExtraRefresh && i < InGameOpeningFlow.ExtraStrategyRefreshCountPerButton; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int positionIndex = 0; positionIndex < strategyRefreshButtons.Length; positionIndex++)
			{
				await ClickRatioPointAsync(strategyRefreshButtons[positionIndex], $"局内识别：银·金·彩额外刷新第 {i + 1}/{InGameOpeningFlow.ExtraStrategyRefreshCountPerButton} 轮：{refreshPositionNames[positionIndex]}", cancellationToken);
				if (positionIndex < strategyRefreshButtons.Length - 1)
				{
					await DelayWithCancellationAsync(0.2, cancellationToken);
				}
			}
			await DelayWithCancellationAsync(InGameOpeningFlow.StrategyRefreshDelaySeconds, cancellationToken);
			if (await TryClickTargetStrategyAsync($"左中右第 {i + 2} 轮刷新后策略识别", strategyAliases, InGameOpeningFlow.InitialStrategyScanAttemptCount, cancellationToken))
			{
				StopAutomationForSuccess("状态：局内策略命中停止");
				return;
			}
		}
		AppendLog("局内识别：刷新后仍未命中目标策略，优先选择图鉴未收集策略；没有标识时再随机选择。");
		await ClickRandomStrategyCardAsync(cancellationToken);
		await ClickStrategyConfirmAsync(cancellationToken);
	}

	private async Task<bool> IsStrategySelectionScreenAsync(CancellationToken cancellationToken)
	{
		OcrClickCandidate candidate = OcrClickResolver.FindBest(await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken), InGameOpeningFlow.StrategyScreenAliases, InGameOpeningFlow.StrategyFuzzyScore);
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
		OcrClickCandidate? bestCandidate = null;
		int bestPriority = int.MaxValue;
		for (int attempt = 1; attempt <= scanAttemptCount; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			AppendLog($"局内识别：{scope}第 {attempt} 次扫描。");
			OcrClickCandidate? candidate = OcrClickResolver.FindByPriority(await CaptureAndOcrAsync(InGameOpeningFlow.StrategyRegion, cancellationToken), strategyAliases, InGameOpeningFlow.StrategyFuzzyScore);
			if (candidate != null)
			{
				int priority = GetAliasPriority(strategyAliases, candidate.Alias);
				if (priority < bestPriority)
				{
					bestCandidate = candidate;
					bestPriority = priority;
				}
				if (bestPriority == 0)
				{
					break;
				}
			}
			if (attempt < scanAttemptCount)
			{
				await DelayWithCancellationAsync(0.1, cancellationToken);
			}
		}
		if (bestCandidate != null && _latestCaptureScreenRegion != null)
		{
			Rect bounds = bestCandidate.Item.Bounds;
			await ExecuteClickAsync(new ClickRequest("局内策略：" + bestCandidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
			AppendLog($"局内识别：{scope}按优先级命中第 {bestPriority + 1} 项：{bestCandidate.Alias}。");
			return true;
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
		if (await TryClickUncollectedStrategyCardAsync(blockedColumns, cancellationToken))
		{
			await DelayWithCancellationAsync(0.1, cancellationToken);
			return;
		}
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

	private async Task<bool> TryClickUncollectedStrategyCardAsync(HashSet<int> blockedColumns, CancellationToken cancellationToken)
	{
		if ((object)_gameWindow == null)
		{
			return false;
		}
		string templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Templates", "currency-wars-new.png");
		if (!File.Exists(templatePath))
		{
			AppendLog("局内识别：缺少图鉴未收集标识模板，继续原随机兜底。");
			return false;
		}

		BitmapImage marker = new BitmapImage();
		marker.BeginInit();
		marker.CacheOption = BitmapCacheOption.OnLoad;
		marker.UriSource = new Uri(templatePath, UriKind.Absolute);
		marker.EndInit();
		marker.Freeze();
		BitmapSource screenshot = _windowCapture.Capture(_gameWindow, new CaptureRegion("策略图鉴标识", 0.0, 0.0, 1.0, 1.0));
		IReadOnlyList<StrategyCollectionMarkerMatch> matches = StrategyCollectionMarkerDetector.FindMatches(screenshot, marker, InGameOpeningFlow.StrategyCardSearchRegions, cancellationToken);
		int[] preferredColumns = new int[3] { 1, 0, 2 };
		foreach (int column in preferredColumns)
		{
			if (blockedColumns.Contains(column))
			{
				continue;
			}
			StrategyCollectionMarkerMatch match = matches.First((StrategyCollectionMarkerMatch item) => item.Column == column);
			if (match.Score >= InGameOpeningFlow.StrategyCollectionMarkerThreshold)
			{
				await ClickRatioPointAsync(InGameOpeningFlow.StrategyCards[column], $"局内识别：选择图鉴未收集策略 {column + 1}", cancellationToken);
				AppendLog($"局内识别：检测到第 {column + 1} 张策略的图鉴未收集标识（相似度 {match.Score:0.000}），优先选择。");
				return true;
			}
		}

		AppendLog("局内识别：未检测到可选的图鉴未收集标识，最高相似度：" + string.Join("、", matches.Select((StrategyCollectionMarkerMatch item) => $"{item.Column + 1}={item.Score:0.000}")) + "。");
		return false;
	}

	private HashSet<int> FindBlacklistedStrategyColumns(OcrScanResult scan)
	{
		HashSet<int> blockedColumns = new HashSet<int>();
		foreach (OcrTextItem item in scan.Items)
		{
			if (InGameOpeningFlow.StrategyChoiceBlacklist.Any((string word) => TextMatcher.FuzzyContains(item.Text, word, InGameOpeningFlow.StrategyFuzzyScore)))
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
		await ClickFixedPointUntilPageChangesAsync("局内识别：策略固定确认", InGameOpeningFlow.StrategyConfirmPoint, InGameOpeningFlow.StrategyScreenAliases, CurrencyWarsFlow.FullWindow, cancellationToken);
	}

	private async Task StartAutomationAsync()
	{
		if (_automationCts != null)
		{
			return;
		}
		ReadUiToConfig();
		_configStore.Save(_config);
		_automationCts = new CancellationTokenSource();
		_automationSuccessStop = false;
		_outerOpeningRapidAdvanceCompleted = false;
		_outerBottomReturnRapidSequenceCompleted = false;
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
			if (_automationSuccessStop)
			{
				AppendLog("自动流程：成功停止。");
				SetStatus("状态：成功停止");
				ShowSuccessFeedback("成功刷出目标词条或投资词条！");
			}
		}
		catch (OperationCanceledException)
		{
			AppendLog(_automationSuccessStop ? "自动流程：成功停止。" : "自动流程：已手动停止。");
			SetStatus(_automationSuccessStop ? "状态：成功停止" : "状态：已停止");
			if (_automationSuccessStop)
			{
				ShowSuccessFeedback("成功刷出目标词条或投资词条！");
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

	private void ShowSuccessFeedback(string message)
	{
		PlaySuccessAudio();
		MessageBox.Show(this, message, "成功", MessageBoxButton.OK, MessageBoxImage.Asterisk);
	}

	private void InitializeSuccessAudio()
	{
		if (_successAudioInitialized)
		{
			return;
		}
		string audioPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Audio", "success.mp3");
		if (!File.Exists(audioPath))
		{
			AppendLog("成功提示音文件不存在：" + audioPath);
			return;
		}
		_successAudioInitialized = true;
		_successAudioPlayer.MediaOpened += delegate
		{
			_successAudioReady = true;
			AppendLog("成功提示音：已预加载。");
			if (_successAudioPlayPending)
			{
				_successAudioPlayPending = false;
				PlaySuccessAudio();
			}
		};
		_successAudioPlayer.MediaFailed += delegate(object? sender, ExceptionEventArgs args)
		{
			_successAudioReady = false;
			_successAudioPlayPending = false;
			AppendLog("成功提示音加载失败：" + args.ErrorException.Message);
		};
		_successAudioPlayer.Open(new Uri(audioPath, UriKind.Absolute));
	}

	private void PlaySuccessAudio()
	{
		if (!_successAudioInitialized)
		{
			InitializeSuccessAudio();
		}
		try
		{
			if (!_successAudioReady)
			{
				_successAudioPlayPending = true;
				AppendLog("成功提示音：等待音频加载完成后播放。");
				return;
			}
			_successAudioPlayer.Stop();
			_successAudioPlayer.Position = TimeSpan.Zero;
			_successAudioPlayer.Volume = 1.0;
			_successAudioPlayer.Play();
			AppendLog("成功提示音：已播放。");
		}
		catch (Exception ex)
		{
			AppendLog("成功提示音播放失败：" + ex.Message);
		}
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
			await RestartOcrAtSafePointIfDueAsync("局外循环", cancellationToken);
			_blockedHitThisCycle = false;
			_outerOpeningRapidAdvanceCompleted = false;
			_outerBottomReturnRapidSequenceCompleted = false;
		}
		if ((object)_gameWindow == null && !TryFindWindow())
		{
			throw new InvalidOperationException("没有可用的游戏窗口。");
		}
		_gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
		if (step == CurrencyWarsFlow.Steps[0])
		{
			await RapidAdvanceOpeningPagesAsync("自动流程", cancellationToken);
			_outerOpeningRapidAdvanceCompleted = true;
			return;
		}
		if (_outerOpeningRapidAdvanceCompleted && step == CurrencyWarsFlow.Steps[1])
		{
			AppendLog("自动流程：进入标准博弈已由前三页固定连点完成，跳过本步 OCR。");
			return;
		}
		if (_outerOpeningRapidAdvanceCompleted && step == CurrencyWarsFlow.Steps[2])
		{
			AppendLog("自动流程：开始对局已由前三页固定连点完成，直接开始主词条 OCR。");
			if (_config.DebuffEnabled)
			{
				await WaitForDebuffResultAsync(cancellationToken);
			}
			return;
		}
		if (step == CurrencyWarsFlow.Steps[10])
		{
			await ClickBottomReturnSequenceWhenNextDetectedAsync("自动流程", step.Aliases, step.TimeoutSeconds, cancellationToken);
			await EnsureReturnedToCurrencyWarsAsync("自动流程", cancellationToken);
			_outerBottomReturnRapidSequenceCompleted = true;
			return;
		}
		if (_outerBottomReturnRapidSequenceCompleted && (step == CurrencyWarsFlow.Steps[11] || step == CurrencyWarsFlow.Steps[12]))
		{
			AppendLog("自动流程：" + step.Name + " 已由识别“下一步”后的固定连点完成，跳过等待和 OCR。");
			return;
		}
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
			if (string.Equals(step.Name, "固定确认", StringComparison.Ordinal))
			{
				AppendLog("自动流程：固定确认完成，执行旧版蓝海二段点位 2 轮兜底。");
				await ClickBlueOceanFollowupGuardAsync(cancellationToken);
			}
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
		await WaitForOpeningBoardReadyAsync("自动流程：退出结算前", 0.0, cancellationToken);
		await ClickEscAndSettlementPointsAsync(cancellationToken);
	}

	private async Task ClickEscAndSettlementPointsAsync(CancellationToken cancellationToken)
	{
		AppendLog("自动流程：点击左上角退出，并等待识别“放弃并结算”。");
		DateTime deadline = DateTime.UtcNow.AddSeconds(CurrencyWarsFlow.SettlementPageWaitTimeoutSeconds);
		string lastText = "";
		int exitClickCount = 0;
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (exitClickCount > 0)
			{
				OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
				lastText = scan.RawText;
				OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, CurrencyWarsFlow.SettlementDialogAliases, _config.ButtonFuzzyScore);
				if ((object)candidate != null && (object)_latestCaptureScreenRegion != null)
				{
					if (await ClickTextUntilPageChangesAsync("自动流程：放弃并结算", candidate, CurrencyWarsFlow.SettlementDialogAliases, CurrencyWarsFlow.FullWindow, null, cancellationToken))
					{
						AppendLog($"自动流程：退出确认页已识别、点击并确认切换（匹配 {candidate.Alias}）。");
						return;
					}
					AppendLog("自动流程：放弃并结算点击后确认页仍存在，继续检测。");
				}
			}
			exitClickCount++;
			await ClickRatioPointAsync(CurrencyWarsFlow.FastEscApproxPoint, $"左上角退出区域 第 {exitClickCount} 次", cancellationToken);
			await DelayWithCancellationAsync(CurrencyWarsFlow.MajorPageScanIntervalSeconds, cancellationToken);
		}
		AppendLog("自动流程：退出确认页识别超时，执行原固定坐标兜底。最后 OCR：" + ShortText(lastText));
		for (int i = 0; i < CurrencyWarsFlow.FastExitSettlementAlternateClickCount; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(CurrencyWarsFlow.FastEscApproxPoint, $"左上角退出区域兜底 第 {i + 1} 次", cancellationToken);
			await DelayWithCancellationAsync(CurrencyWarsFlow.FastExitProbeIntervalSeconds, cancellationToken);
			await ClickRatioPointAsync(CurrencyWarsFlow.FastSettlementApproxPoint, $"放弃并结算区域兜底 第 {i + 1} 次", cancellationToken);
			await DelayWithCancellationAsync(CurrencyWarsFlow.FastExitProbeIntervalSeconds, cancellationToken);
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
				RatioPoint? fixedClickPoint = useBottomReturnPoint ? new RatioPoint(0.5, 0.829) : null;
				if (await ClickTextUntilPageChangesAsync(step.Name, candidate, GetPostClickVerificationAliases(step.Name, step.Aliases), step.SearchRegion, fixedClickPoint, cancellationToken))
				{
					SetStatus("状态：已点击 " + step.Name);
					return;
				}
				AppendLog("自动流程：" + step.Name + " 点击后原按钮仍存在，继续在超时时间内重试。");
			}
			await DelayWithCancellationAsync(useBottomReturnPoint ? 0.15 : 0.6, cancellationToken);
		}
		if ((object)step.FallbackPoint != null)
		{
			AppendLog("自动流程：" + step.Name + " OCR 未命中，使用兜底坐标。最后 OCR：" + ShortText(lastText));
			bool fallbackSucceeded = await ClickFixedPointUntilPageChangesAsync(step.Name + " 兜底", step.FallbackPoint, GetPostClickVerificationAliases(step.Name, step.Aliases), step.SearchRegion, cancellationToken);
			if (!fallbackSucceeded && GetExpectedPostClickAliases(step.Name).Count > 0)
			{
				throw new InvalidOperationException("自动流程：" + step.Name + " 兜底点击后没有识别到目标下一页，已停止继续执行，避免跳错步骤。");
			}
			return;
		}
		if (useBottomReturnPoint)
		{
			AppendLog("自动流程：" + step.Name + " OCR 未命中，使用底部固定坐标。最后 OCR：" + ShortText(lastText));
			await ClickFixedPointUntilPageChangesAsync(step.Name + " 固定坐标兜底", new RatioPoint(0.5, 0.829), GetPostClickVerificationAliases(step.Name, step.Aliases), step.SearchRegion, cancellationToken);
			return;
		}
		throw new InvalidOperationException("超时：没有找到按钮文字“" + step.Name + "”。最后 OCR：" + ShortText(lastText));
	}

	private async Task<bool> ClickTextUntilPageChangesAsync(string scope, OcrClickCandidate initialCandidate, IReadOnlyList<string> verificationAliases, RatioRegion verificationRegion, RatioPoint? fixedClickPoint, CancellationToken cancellationToken)
	{
		OcrClickCandidate candidate = initialCandidate;
		IReadOnlyList<string> expectedAliases = GetExpectedPostClickAliases(scope);
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ((object)fixedClickPoint != null)
			{
				await ClickRatioPointAsync(fixedClickPoint, $"{scope} 第 {attempt} 次", cancellationToken);
			}
			else
			{
				if ((object)_latestCaptureScreenRegion == null)
				{
					return false;
				}
				Rect bounds = candidate.Item.Bounds;
				await ExecuteClickAsync(new ClickRequest($"{scope}：{candidate.Item.Text} 第 {attempt} 次", _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
			}
			await DelayWithCancellationAsync(0.8, cancellationToken);
			if (expectedAliases.Count > 0)
			{
				if (await WaitForExpectedPageAfterClickAsync(scope, expectedAliases, cancellationToken))
				{
					return true;
				}
				OcrScanResult currentPageScan = await CaptureAndOcrAsync(verificationRegion, cancellationToken);
				OcrClickCandidate currentPageCandidate = OcrClickResolver.FindBest(currentPageScan, verificationAliases, _config.ButtonFuzzyScore);
				if ((object)currentPageCandidate == null)
				{
					AppendLog($"{scope}：旧按钮虽已消失，但目标下一页仍未出现；交回外层重新识别当前页面，不提前进入下一步。");
					return false;
				}
				candidate = currentPageCandidate;
				AppendLog($"{scope}：目标下一页未出现，且仍识别到 {currentPageCandidate.Item.Text}（匹配 {currentPageCandidate.Alias}），准备重试本步骤。");
				continue;
			}
			OcrScanResult verificationScan = await CaptureAndOcrAsync(verificationRegion, cancellationToken);
			OcrClickCandidate remaining = OcrClickResolver.FindBest(verificationScan, verificationAliases, _config.ButtonFuzzyScore);
			if ((object)remaining == null)
			{
				AppendLog($"{scope}：点击后原按钮已消失，确认页面已切换。");
				return true;
			}
			candidate = remaining;
			AppendLog($"{scope}：点击后仍识别到 {remaining.Item.Text}（匹配 {remaining.Alias}），准备重试。");
		}
		return false;
	}

	private async Task<bool> ClickFixedPointUntilPageChangesAsync(string scope, RatioPoint point, IReadOnlyList<string> currentPageAliases, RatioRegion verificationRegion, CancellationToken cancellationToken)
	{
		IReadOnlyList<string> expectedAliases = GetExpectedPostClickAliases(scope);
		for (int attempt = 1; attempt <= 3; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await ClickRatioPointAsync(point, $"{scope} 第 {attempt} 次", cancellationToken);
			await DelayWithCancellationAsync(0.8, cancellationToken);
			if (expectedAliases.Count > 0)
			{
				if (await WaitForExpectedPageAfterClickAsync(scope, expectedAliases, cancellationToken))
				{
					return true;
				}
				OcrScanResult currentPageScan = await CaptureAndOcrAsync(verificationRegion, cancellationToken);
				OcrClickCandidate currentPageCandidate = OcrClickResolver.FindBest(currentPageScan, currentPageAliases, _config.ButtonFuzzyScore);
				if ((object)currentPageCandidate != null)
				{
					AppendLog($"{scope}：目标下一页未出现，但已识别到本步骤按钮 {currentPageCandidate.Item.Text}，继续点击同一固定点位。");
				}
				else
				{
					AppendLog($"{scope}：目标下一页尚未出现，继续用同一固定点位恢复当前步骤，不提前进入下一步。");
				}
				continue;
			}
			OcrScanResult scan = await CaptureAndOcrAsync(verificationRegion, cancellationToken);
			OcrClickCandidate remaining = OcrClickResolver.FindBest(scan, currentPageAliases, _config.ButtonFuzzyScore);
			if ((object)remaining == null)
			{
				AppendLog(scope + "：原页面特征已消失，确认页面已切换。");
				return true;
			}
			AppendLog($"{scope}：原页面仍存在（{remaining.Item.Text}），准备重试。");
		}
		AppendLog(scope + "：连续 3 次点击后原页面仍存在，交回外层流程继续判断。");
		return false;
	}

	private async Task<bool> WaitForExpectedPageAfterClickAsync(string scope, IReadOnlyList<string> expectedAliases, CancellationToken cancellationToken)
	{
		DateTime deadline = DateTime.UtcNow.AddSeconds(4.0);
		string lastText = "";
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
			lastText = scan.RawText;
			OcrClickCandidate expected = OcrClickResolver.FindBest(scan, expectedAliases, _config.ButtonFuzzyScore);
			if ((object)expected != null)
			{
				AppendLog($"{scope}：已确认进入目标下一页：{expected.Item.Text}（匹配 {expected.Alias}）。");
				return true;
			}
			await DelayWithCancellationAsync(0.35, cancellationToken);
		}
		AppendLog($"{scope}：等待目标下一页超时，期望特征：{string.Join("、", expectedAliases)}。最后 OCR：" + ShortText(lastText));
		return false;
	}

	private static IReadOnlyList<string> GetPostClickVerificationAliases(string name, IReadOnlyList<string> fallbackAliases)
	{
		switch (name)
		{
		case "开始「货币战争」":
			return new string[1] { "开始货币战争" };
		case "进入标准博弈":
			return new string[2] { "进入标准博弈", "开始标准博弈" };
		case "开始对局":
			return new string[1] { "开始对局" };
		default:
			return fallbackAliases;
		}
	}

	private static IReadOnlyList<string> GetExpectedPostClickAliases(string name)
	{
		if (name.Contains("开始「货币战争」", StringComparison.Ordinal))
		{
			return new string[4] { "进入标准博弈", "零和博弈", "创业指南", "晋升等级" };
		}
		if (name.Contains("进入标准博弈", StringComparison.Ordinal))
		{
			return new string[1] { "开始对局" };
		}
		return System.Array.Empty<string>();
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
	}

	private async Task<string?> TryClickInvestmentTargetAsync(string scope, CancellationToken cancellationToken, bool logRawText = false)
	{
		AppendLog($"自动流程：{scope}开始，固定扫描 {CurrencyWarsFlow.InvestmentScanAttemptCount} 次。");
		OcrClickCandidate? bestCandidate = null;
		int bestPriority = int.MaxValue;
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
			OcrClickCandidate? candidate = OcrClickResolver.FindByPriority(scan, _config.InvestmentTargets, _config.InvestmentFuzzyScore);
			if (candidate != null)
			{
				int priority = GetAliasPriority(_config.InvestmentTargets, candidate.Alias);
				if (priority < bestPriority)
				{
					bestCandidate = candidate;
					bestPriority = priority;
				}
				if (bestPriority == 0)
				{
					break;
				}
			}
			if (attempt < CurrencyWarsFlow.InvestmentScanAttemptCount)
			{
				await DelayWithCancellationAsync(0.1, cancellationToken);
			}
		}
		if (bestCandidate != null && _latestCaptureScreenRegion != null)
		{
			Rect bounds = bestCandidate.Item.Bounds;
			await ExecuteClickAsync(new ClickRequest("投资词条：" + bestCandidate.Item.Text, _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0), _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
			AppendLog($"自动流程：{scope}按优先级选择第 {bestPriority + 1} 项：{bestCandidate.Alias}。");
			return bestCandidate.Alias;
		}
		AppendLog("自动流程：" + scope + "结束，未命中投资词条。");
		return null;
	}

	private static int GetAliasPriority(IReadOnlyList<string> aliases, string alias)
	{
		for (int index = 0; index < aliases.Count; index++)
		{
			if (string.Equals(aliases[index].Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return index;
			}
		}
		return int.MaxValue;
	}

	private async Task ClickSafeInvestmentAsync(bool rememberChoice, bool useConfiguredInvestmentTargetsForBlacklist, CancellationToken cancellationToken, int blacklistScanAttempts = 1)
	{
		List<string> activeTargets = (useConfiguredInvestmentTargetsForBlacklist ? _config.InvestmentTargets : new List<string>());
		HashSet<int> blockedColumns = new HashSet<int>();
		int attempts = Math.Max(1, blacklistScanAttempts);
		for (int attempt = 1; attempt <= attempts; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
			blockedColumns.UnionWith(FindBlacklistedInvestmentColumns(scan, activeTargets));
			if (attempts > 1)
			{
				AppendLog($"默认投资选择：黑名单保护扫描 {attempt}/{attempts}，已发现列：{(blockedColumns.Count == 0 ? "无" : string.Join("、", blockedColumns.OrderBy((int index) => index).Select((int index) => index + 1)))}。");
			}
			if (attempt < attempts)
			{
				await DelayWithCancellationAsync(0.1, cancellationToken);
			}
		}
		if (blockedColumns.Count > 0)
		{
			AppendLog("默认投资选择：避开特殊投资列 " + string.Join("、", blockedColumns.Select((int index) => index + 1)));
		}
		int? uncollectedColumn = FindUncollectedInvestmentColumn(blockedColumns, cancellationToken);
		int chosenIndex = uncollectedColumn ?? ChooseSafeInvestmentIndex(blockedColumns);
		RatioPoint point = CurrencyWarsFlow.InvestmentFallbackPoints[chosenIndex];
		if (rememberChoice)
		{
			_lastSafeInvestmentPoint = point;
		}
		string reason = (uncollectedColumn.HasValue ? $"图鉴未收录投资 {chosenIndex + 1}" : "默认安全投资");
		await ClickRatioPointAsync(point, reason, cancellationToken);
	}

	private async Task ClickBlueOceanFollowupGuardAsync(CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(0.3, cancellationToken);
		for (int i = 0; i < 2; i++)
		{
			await ClickRatioPointAsync(new RatioPoint(0.52, 0.49), $"蓝海二次投资中间选项兜底 {i + 1}/2", cancellationToken);
			await DelayWithCancellationAsync(0.1, cancellationToken);
			await ClickRatioPointAsync(new RatioPoint(0.565, 0.91), $"蓝海二次投资确认兜底 {i + 1}/2", cancellationToken);
			await DelayWithCancellationAsync(0.1, cancellationToken);
		}
	}

	private int? FindUncollectedInvestmentColumn(HashSet<int> blockedColumns, CancellationToken cancellationToken)
	{
		if ((object)_gameWindow == null)
		{
			return null;
		}
		string templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Templates", "currency-wars-new.png");
		if (!File.Exists(templatePath))
		{
			AppendLog("默认投资选择：缺少图鉴未收录标识模板，继续默认安全投资。");
			return null;
		}

		BitmapImage marker = new BitmapImage();
		marker.BeginInit();
		marker.CacheOption = BitmapCacheOption.OnLoad;
		marker.UriSource = new Uri(templatePath, UriKind.Absolute);
		marker.EndInit();
		marker.Freeze();
		BitmapSource screenshot = _windowCapture.Capture(_gameWindow, new CaptureRegion("投资图鉴标识", 0.0, 0.0, 1.0, 1.0));
		IReadOnlyList<StrategyCollectionMarkerMatch> matches = StrategyCollectionMarkerDetector.FindMatches(screenshot, marker, CurrencyWarsFlow.InvestmentCardSearchRegions, cancellationToken);
		int[] preferredColumns = new int[3] { 1, 0, 2 };
		foreach (int column in preferredColumns)
		{
			if (blockedColumns.Contains(column))
			{
				continue;
			}
			StrategyCollectionMarkerMatch match = matches.First((StrategyCollectionMarkerMatch item) => item.Column == column);
			if (match.Score >= CurrencyWarsFlow.InvestmentCollectionMarkerThreshold)
			{
				AppendLog($"默认投资选择：检测到第 {column + 1} 张投资卡的图鉴未收录标识（相似度 {match.Score:0.000}），优先选择。");
				return column;
			}
		}

		AppendLog("默认投资选择：未检测到可选的图鉴未收录标识，继续默认安全投资。最高相似度：" + string.Join("、", matches.Select((StrategyCollectionMarkerMatch item) => $"{item.Column + 1}={item.Score:0.000}")) + "。");
		return null;
	}

	private static int ChooseSafeInvestmentIndex(HashSet<int> blockedColumns)
	{
		int chosenIndex = new int[3] { 1, 0, 2 }.FirstOrDefault((int index) => !blockedColumns.Contains(index));
		if (blockedColumns.Contains(chosenIndex))
		{
			chosenIndex = 1;
		}
		return chosenIndex;
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

	private async Task<bool> WaitForMajorPageAsync(string scope, IReadOnlyList<string> aliases, RatioRegion region, double timeoutSeconds, double initialDelaySeconds, int fuzzyScore, CancellationToken cancellationToken)
	{
		if (initialDelaySeconds > 0.0)
		{
			await DelayWithCancellationAsync(initialDelaySeconds, cancellationToken);
		}
		AppendLog($"{scope}：开始等待页面识别，特征：{string.Join("、", aliases)}，最长 {timeoutSeconds:0.#} 秒。");
		DateTime startedAt = DateTime.UtcNow;
		DateTime deadline = startedAt.AddSeconds(timeoutSeconds);
		string lastText = "";
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(region, cancellationToken);
			lastText = scan.RawText;
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, aliases, fuzzyScore);
			if ((object)candidate != null)
			{
				double elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
				AppendLog($"{scope}：页面已识别：{candidate.Item.Text}（匹配 {candidate.Alias}），等待 {elapsed:0.0} 秒。");
				return true;
			}
			await DelayWithCancellationAsync(CurrencyWarsFlow.MajorPageScanIntervalSeconds, cancellationToken);
		}
		AppendLog(scope + "：页面识别等待超时，继续原兜底流程。最后 OCR：" + ShortText(lastText));
		return false;
	}

	private async Task<bool> WaitForOpeningBoardReadyAsync(string scope, double postDetectionWaitSeconds, CancellationToken cancellationToken)
	{
		await DelayWithCancellationAsync(0.6, cancellationToken);
		AppendLog($"{scope}：开始等待局内棋盘/备战页，最长 {InGameOpeningFlow.OpeningBoardWaitTimeoutSeconds:0.#} 秒。");
		DateTime startedAt = DateTime.UtcNow;
		DateTime deadline = startedAt.AddSeconds(InGameOpeningFlow.OpeningBoardWaitTimeoutSeconds);
		string lastText = "";
		while (DateTime.UtcNow < deadline)
		{
			cancellationToken.ThrowIfCancellationRequested();
			OcrScanResult scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
			lastText = scan.RawText;
			if (await TryHandleRoleChoicePopupAsync(scan, cancellationToken))
			{
				AppendLog(scope + "：等待棋盘时先处理角色/圣杯选择弹窗。");
				await DelayWithCancellationAsync(0.5, cancellationToken);
				continue;
			}
			OcrClickCandidate candidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.OpeningBoardScreenAliases, _config.ButtonFuzzyScore);
			if ((object)candidate != null)
			{
				if (postDetectionWaitSeconds > 0.0)
				{
					AppendLog($"{scope}：局内棋盘/备战页已识别：{candidate.Item.Text}（匹配 {candidate.Alias}），识别后再等待 {postDetectionWaitSeconds:0.0} 秒加载拖拽区域。");
					await DelayWithCancellationAsync(postDetectionWaitSeconds, cancellationToken);
				}
				double totalWait = (DateTime.UtcNow - startedAt).TotalSeconds;
				AppendLog($"{scope}：局内棋盘/备战页准备完成，总等待 {totalWait:0.0} 秒。");
				return true;
			}
			await DelayWithCancellationAsync(CurrencyWarsFlow.MajorPageScanIntervalSeconds, cancellationToken);
		}
		AppendLog(scope + "：局内棋盘/备战页等待超时，继续原兜底流程。最后 OCR：" + ShortText(lastText));
		return false;
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
		CaptureInfoText.Text = $"截图：自动流程区域  {resolved.Width}x{resolved.Height}  left={resolved.Left}, top={resolved.Top}  后端={_windowCapture.LastCaptureBackend}";
		OcrScanResult scan = (_latestOcrResult = await _ocrService.RecognizeAsync(image, cancellationToken));
		OcrRawTextBox.Text = FormatOcrResult(captureRegion.Name, scan);
		OcrInfoText.Text = $"OCR：自动流程区域，文本块 {scan.Items.Count}，字符 {scan.RawText.Length}";
		return scan;
	}

	private async Task RestartOcrAtSafePointIfDueAsync(string scope, CancellationToken cancellationToken)
	{
		if (_ocrService is not ExternalRapidOcrService rapidOcr
			|| !rapidOcr.IsMaintenanceRestartDue(OcrMaintenanceRestartInterval))
		{
			return;
		}
		AppendLog($"{scope}：OCR 常驻进程已运行 90 分钟，本轮已回到安全位置，等待 2 秒后执行维护重启。");
		await DelayWithCancellationAsync(2.0, cancellationToken);
		if (await rapidOcr.RestartForMaintenanceIfDueAsync(OcrMaintenanceRestartInterval, cancellationToken))
		{
			AppendLog($"{scope}：OCR 常驻进程维护重启完成；下一次识别时会自动重新加载模型。");
			await DelayWithCancellationAsync(1.0, cancellationToken);
		}
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
		string bridgeExe = Path.Combine(AppContext.BaseDirectory, "OCRRuntime", "rapidocr_bridge", "rapidocr_bridge.exe");
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
		_logLineCount++;
		TrimLogBox();
		LogBox.ScrollToEnd();
		_gameLogOverlay.AppendLog(line);
	}

	private void AppendStartupNotice()
	{
		const string notice = "      ========================================================\r\n      ||                                                  ||\r\n      ||       本工具为开源免费项目，没有任何收费，如果您是付费获得此款产品请立即退款        ||\r\n      ||                                                  ||\r\n      ========================================================\r\n";
		LogBox.AppendText(notice);
		_logLineCount += notice.Count(character => character == '\n');
		LogBox.ScrollToEnd();
	}

	private void TrimLogBox()
	{
		int linesToRemove = _logLineCount - MaxLogLines;
		if (linesToRemove <= 0)
		{
			return;
		}
		int firstKeptCharacter = LogBox.GetCharacterIndexFromLineIndex(linesToRemove);
		if (firstKeptCharacter <= 0)
		{
			return;
		}
		LogBox.Text = LogBox.Text.Substring(firstKeptCharacter);
		_logLineCount -= linesToRemove;
		LogBox.CaretIndex = LogBox.Text.Length;
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
