using HsrCurrencyWarsCleanWpf.Core;
using HsrCurrencyWarsCleanWpf.Services;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf;

public partial class MainWindow : Window
{
    private const int HotkeyStopId = 1001;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkF8 = 0x77;

    private readonly ConfigStore _configStore = new(AppContext.BaseDirectory);
    private readonly WindowCaptureService _windowCapture = new();
    private readonly IOcrService _ocrService = CreateOcrService();
    private readonly ScanEvaluator _scanEvaluator = new();
    private readonly IClickService _clickService = new MouseClickService();
    private AutomationConfig _config = new();
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

    public MainWindow()
    {
        InitializeComponent();
        LoadConfigToUi();
        InitializeFlowList();
        AppendLog("程序已启动。");
        AppendLog($"配置文件：{_configStore.ConfigPath}");
        AppendLog("第 6 阶段已启用：完整自动刷新闭环，返回货币战争后继续循环。");
        AppendLog($"OCR 状态：{_ocrService.Name}");
        AppendLog("点击/按键语义对齐旧 Python 稳定版：流程中直接执行输入。");
        SetStatus("状态：未运行");
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RegisterHotkeys();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _automationCts?.Cancel();
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
        await StartAutomationAsync();
    }

    private async void StartLuochaPreset_Click(object sender, RoutedEventArgs e)
    {
        await StartIndependentStrategyPresetAsync("本姑娘就是罗刹", InGameOpeningFlow.TargetStrategyAliases, InGameOpeningFlow.PrismInvestmentGateAliases);
    }

    private async void StartReincarnationPreset_Click(object sender, RoutedEventArgs e)
    {
        await StartIndependentStrategyPresetAsync("轮回不止", InGameOpeningFlow.ReincarnationStrategyAliases, InGameOpeningFlow.PrismInvestmentGateAliases);
    }

    private async void StartSandGoldPreset_Click(object sender, RoutedEventArgs e)
    {
        await StartIndependentStrategyPresetAsync("砂里淘金", InGameOpeningFlow.SandGoldStrategyAliases, InGameOpeningFlow.LongTermGoodInvestmentGateAliases);
    }

    private async void StartCustomInGame_Click(object sender, RoutedEventArgs e)
    {
        var strategies = InGameStrategyListBox.Items.OfType<string>().ToList();
        var investments = InGameInvestmentListBox.Items.OfType<string>().ToList();
        if (strategies.Count == 0 || investments.Count == 0)
        {
            MessageBox.Show(this, "请至少添加 1 个局内投资目标和 1 个局内策略目标。", "局内自定义", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReadUiToConfig();
        _configStore.Save(_config);
        RefreshWordHistoryControls();
        await StartIndependentStrategyPresetAsync(string.Join("、", strategies), strategies, investments);
    }

    private void StopAutomation_Click(object sender, RoutedEventArgs e)
    {
        StopAutomation();
    }

    private void TargetWordInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddTargetWord_Click(sender, e);
            e.Handled = true;
        }
    }

    private void BlockedWordInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddBlockedWord_Click(sender, e);
            e.Handled = true;
        }
    }

    private void InvestmentWordInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
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
        AddWord(BlockedWordInputBox, BlockedWordsListBox, AutomationConfig.MaxTargetAnyWords, "不想要词条");
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
        AddWord(InvestmentWordInputBox, InvestmentWordsListBox, AutomationConfig.MaxInvestmentWords, "投资词条");
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
        AddWord(InGameInvestmentInputBox, InGameInvestmentListBox, AutomationConfig.MaxInGameWords, "局内投资");
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
        AddWord(InGameStrategyInputBox, InGameStrategyListBox, AutomationConfig.MaxInGameWords, "局内策略");
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
        if (!IsLoaded)
        {
            return;
        }

        SaveConfigFromUi("主词条命中模式已更新。");
    }

    private void CheckInvestmentWhenBlocked_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        SaveConfigFromUi("命中不想要后投资检查开关已更新。");
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
        InvestmentEnabledBox.IsChecked = _config.InvestmentEnabled;
        CheckInvestmentWhenBlockedBox.IsChecked = _config.CheckInvestmentWhenBlocked;
        SetListBoxItems(InvestmentWordsListBox, _config.InvestmentTargets);
        SetListBoxItems(InGameStrategyListBox, _config.InGameStrategyTargets);
        SetListBoxItems(InGameInvestmentListBox, _config.InGameInvestmentTargets);
        RefreshWordHistoryControls();
        SelectSpeedMode(_config.SpeedMode);
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
        _config.InvestmentEnabled = InvestmentEnabledBox.IsChecked == true;
        _config.InvestmentTargets = ReadWords(InvestmentWordsListBox);
        _config.CheckInvestmentWhenBlocked = CheckInvestmentWhenBlockedBox.IsChecked == true;
        _config.InGameStrategyTargets = ReadWords(InGameStrategyListBox);
        _config.InGameInvestmentTargets = ReadWords(InGameInvestmentListBox);
        _config.InGameStrategyTarget = "";
        _config.InGameInvestmentTarget = "";
        _config.SpeedMode = GetSelectedSpeedMode();
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
        var targetLimit = _config.DebuffMatchAny ? AutomationConfig.MaxTargetAnyWords : AutomationConfig.MaxTargetAllWords;
        var mode = _config.DebuffMatchAny ? "任意" : "全部";
        TargetWordsCountText.Text = $"{mode} {_config.TargetWords.Count} / 上限 {targetLimit}";
        BlockedWordsCountText.Text = $"不想要 {_config.BlockedWords.Count} / 上限 {AutomationConfig.MaxTargetAnyWords}";
        InvestmentWordsCountText.Text = $"投资 {_config.InvestmentTargets.Count} / 上限 {AutomationConfig.MaxInvestmentWords}";
        InGameInvestmentCountText.Text = $"{_config.InGameInvestmentTargets.Count} / {AutomationConfig.MaxInGameWords}";
        InGameStrategyCountText.Text = $"{_config.InGameStrategyTargets.Count} / {AutomationConfig.MaxInGameWords}";
    }

    private static List<string> ReadWords(ListBox listBox)
    {
        return listBox.Items
            .OfType<string>()
            .Select(word => word.Trim())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToList();
    }

    private static void SetListBoxItems(ListBox listBox, IEnumerable<string> words)
    {
        listBox.Items.Clear();
        foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            listBox.Items.Add(word);
        }
    }

    private static void SetComboBoxItems(ComboBox comboBox, IEnumerable<string> words)
    {
        var currentText = comboBox.Text;
        comboBox.Items.Clear();
        foreach (var word in words.Where(word => !string.IsNullOrWhiteSpace(word)).Distinct(StringComparer.OrdinalIgnoreCase))
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
        return DebuffMatchAnyBox.IsChecked == true ? AutomationConfig.MaxTargetAnyWords : AutomationConfig.MaxTargetAllWords;
    }

    private void AddWord(ComboBox inputBox, ListBox listBox, int limit, string label)
    {
        var word = inputBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        if (listBox.Items.OfType<string>().Any(existing => string.Equals(existing, word, StringComparison.OrdinalIgnoreCase)))
        {
            inputBox.Text = "";
            AppendLog($"{label}已存在：{word}");
            return;
        }

        if (listBox.Items.Count >= limit)
        {
            MessageBox.Show(this, $"{label}最多保存 {limit} 个。", "超过上限", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        listBox.Items.Add(word);
        inputBox.Text = "";
        SaveConfigFromUi($"{label}已添加：{word}");
    }

    private void DeleteSelectedWords(ListBox listBox, string label)
    {
        if (listBox.SelectedItems.Count == 0)
        {
            return;
        }

        var selected = listBox.SelectedItems.OfType<string>().ToList();
        foreach (var item in selected)
        {
            listBox.Items.Remove(item);
        }

        SaveConfigFromUi($"{label}已删除：{string.Join("、", selected)}");
    }

    private void ClearWords(ListBox listBox, string label)
    {
        if (listBox.Items.Count == 0)
        {
            return;
        }

        listBox.Items.Clear();
        SaveConfigFromUi($"{label}已清空。");
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
        var index = 1;
        foreach (var step in CurrencyWarsFlow.Steps)
        {
            FlowStepsListBox.Items.Add($"{index}. {step.Name}");
            index++;
        }
    }

    private string GetSelectedSpeedMode()
    {
        if (SpeedModeBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            return tag;
        }

        return "standard";
    }

    private void SelectSpeedMode(string speedMode)
    {
        foreach (var item in SpeedModeBox.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == speedMode)
            {
                SpeedModeBox.SelectedItem = item;
                return;
            }
        }

        SpeedModeBox.SelectedIndex = 1;
    }

    private bool TryFindWindow()
    {
        try
        {
            ReadUiToConfig();
            _configStore.Save(_config);

            _gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
            var rect = _gameWindow.ClientRect;
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
            AppendLog($"找窗口失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "找窗口失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void CapturePreview(CaptureRegion region)
    {
        try
        {
            if (_gameWindow is null && !TryFindWindow())
            {
                return;
            }

            _gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
            var resolved = _windowCapture.ResolveRegion(_gameWindow.ClientRect, region);
            var image = _windowCapture.Capture(_gameWindow, region);
            _latestPreviewImage = image;
            _latestCaptureScreenRegion = resolved;
            _latestOcrResult = null;
            _latestPreviewRegion = region;
            PreviewImage.Source = image;
            PreviewPlaceholder.Visibility = Visibility.Collapsed;
            CaptureInfoText.Text = $"截图：{region.Name}  {resolved.Width}x{resolved.Height}  left={resolved.Left}, top={resolved.Top}";
            SetStatus($"状态：截图完成：{region.Name}");
            AppendLog($"截图完成：{region.Name}，{resolved.Width}x{resolved.Height}，left={resolved.Left}, top={resolved.Top}");
        }
        catch (Exception ex)
        {
            AppendLog($"截图失败：{region.Name}，{ex.Message}");
            SetStatus("状态：截图失败");
            MessageBox.Show(this, ex.Message, "截图失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RunOcrOnLatestPreviewAsync()
    {
        try
        {
            if (_latestPreviewImage is null)
            {
                CapturePreview(CaptureRegion.FullWindow);
            }

            if (_latestPreviewImage is null)
            {
                return;
            }

            OcrInfoText.Text = $"OCR：正在识别 {_latestPreviewRegion.Name}...";
            SetStatus($"状态：OCR 识别中：{_latestPreviewRegion.Name}");
            AppendLog($"OCR 开始：{_latestPreviewRegion.Name}");
            var result = await _ocrService.RecognizeAsync(_latestPreviewImage);
            _latestOcrResult = result;
            ReadUiToConfig();
            var evaluation = _scanEvaluator.Evaluate(_config, result.RawText);
            OcrInfoText.Text = $"OCR：{_latestPreviewRegion.Name}，文本块 {result.Items.Count}，字符 {result.RawText.Length}";
            OcrRawTextBox.Text = FormatOcrResult(_latestPreviewRegion.Name, result);
            EvaluationTextBox.Text = FormatEvaluation(evaluation);
            ApplyEvaluationSummary(evaluation);
            SetStatus("状态：OCR 完成");
            AppendLog($"OCR 完成：{_latestPreviewRegion.Name}，文本块 {result.Items.Count}，字符 {result.RawText.Length}");
            AppendLog($"命中评估：主词条{(evaluation.DebuffSuccess ? "成功" : "未成功")}，命中 {evaluation.TargetMatch.HitWords.Count}，不想要命中 {evaluation.BlockedMatch.HitWords.Count}，投资命中 {evaluation.InvestmentMatch.HitWords.Count}");
        }
        catch (Exception ex)
        {
            OcrInfoText.Text = "OCR：失败";
            SetStatus("状态：OCR 失败");
            AppendLog($"OCR 失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "OCR 失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ClickWindowCenterAsync()
    {
        try
        {
            if (_gameWindow is null && !TryFindWindow())
            {
                return;
            }

            if (_gameWindow is null)
            {
                return;
            }

            var rect = _gameWindow.ClientRect;
            var request = new ClickRequest("窗口中心", rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            await ExecuteClickAsync(request);
        }
        catch (Exception ex)
        {
            AppendLog($"点击窗口中心失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "点击失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ClickOcrTextAsync()
    {
        try
        {
            if (_latestPreviewImage is null)
            {
                CapturePreview(CaptureRegion.FullWindow);
            }

            if (_latestOcrResult is null)
            {
                await RunOcrOnLatestPreviewAsync();
            }

            if (_latestOcrResult is null || _latestCaptureScreenRegion is null)
            {
                return;
            }

            var aliases = ClickTextBox.Text
                .Split(['/', '／', ',', '，', ';', '；', '|', '｜'], StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
            var candidate = OcrClickResolver.FindBest(_latestOcrResult, aliases, _config.ButtonFuzzyScore);
            if (candidate is null)
            {
                var text = string.Join(" / ", aliases);
                AppendLog($"OCR 点击未命中：{text}");
                MessageBox.Show(this, $"当前 OCR 结果里没有找到：{text}", "OCR 点击未命中", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var bounds = candidate.Item.Bounds;
            var request = new ClickRequest(
                $"OCR 文字：{candidate.Item.Text}（匹配 {candidate.Alias}）",
                _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0));
            await ExecuteClickAsync(request);
        }
        catch (Exception ex)
        {
            AppendLog($"OCR 文字点击失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "点击失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ExecuteClickAsync(ClickRequest request)
    {
        if (_gameWindow is null && !TryFindWindow())
        {
            return;
        }

        var result = await _clickService.ClickAsync(request, _gameWindow?.Handle ?? nint.Zero);
        AppendLog(result.Message);
    }

    private async Task ExecuteDragRatioAsync(RatioPoint start, RatioPoint end, string reason, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_gameWindow is null && !TryFindWindow())
        {
            throw new InvalidOperationException("没有可用的游戏窗口。");
        }

        _gameWindow = _windowCapture.FindWindow(_config.WindowTitle);
        var rect = _gameWindow.ClientRect;
        var request = new DragRequest(
            reason,
            rect.Left + (int)Math.Round(rect.Width * start.X),
            rect.Top + (int)Math.Round(rect.Height * start.Y),
            rect.Left + (int)Math.Round(rect.Width * end.X),
            rect.Top + (int)Math.Round(rect.Height * end.Y));
        var result = await _clickService.DragAsync(request, _gameWindow.Handle, cancellationToken);
        AppendLog(result.Message);
    }

    private void SetAutomationButtonsEnabled(bool enabled)
    {
        StartAutoButton.IsEnabled = enabled;
        StartLuochaPresetButton.IsEnabled = enabled;
        StartReincarnationPresetButton.IsEnabled = enabled;
        StartSandGoldPresetButton.IsEnabled = enabled;
        StartCustomInGameButton.IsEnabled = enabled;
        StopAutoButton.IsEnabled = !enabled;
    }

    private async Task StartIndependentStrategyPresetAsync(string strategyName, IReadOnlyList<string> strategyAliases, IReadOnlyList<string> investmentGateAliases)
    {
        if (_automationCts is not null)
        {
            return;
        }

        ReadUiToConfig();
        _configStore.Save(_config);
        if (_gameWindow is null && !TryFindWindow())
        {
            return;
        }

        _automationCts = new CancellationTokenSource();
        _automationSuccessStop = false;
        _lastSafeInvestmentPoint = null;
        SetAutomationButtonsEnabled(false);
        SetStatus($"状态：独立局内预设运行中：{strategyName}");

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
                MessageBox.Show(this, $"成功刷出目标策略：{strategyName}！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"独立局内预设失败：{ex.Message}");
            SetStatus("状态：独立局内预设失败");
            MessageBox.Show(this, ex.Message, "独立局内预设失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _automationCts?.Dispose();
            _automationCts = null;
            SetAutomationButtonsEnabled(true);
        }
    }

    private async Task RunIndependentStrategyPresetLoopAsync(string strategyName, IReadOnlyList<string> strategyAliases, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
    {
        AppendLog("独立局内预设：启动缓冲 1 秒，固定使用标准速度。");
        await DelayWithCancellationAsync(1.0, cancellationToken);

        var round = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            AppendLog($"独立局内预设：第 {round} 轮开始，投资门槛：{string.Join("、", investmentGateAliases)}。");
            await RunIndependentOuterFlowBeforeInvestmentAsync(cancellationToken);

            AppendLog("独立局内预设：检查固定投资门槛。");
            var gateHit = await ExecuteIndependentInvestmentGateAsync(investmentGateAliases, cancellationToken);
            await IndependentClickTextStepAsync("确认", ["确认", "确定"], CurrencyWarsFlow.FullWindow, new RatioPoint(0.50, 0.91), 15, 0.1, cancellationToken);

            if (gateHit)
            {
                AppendLog("独立局内预设：投资门槛命中，等待局内棋盘稳定后进入 1-1 / 1-2。");
                await DelayWithCancellationAsync(InGameOpeningFlow.BeforeInGameDeployDelaySeconds, cancellationToken);
                await DeployOpeningCharactersAsync(cancellationToken);
                await TryHandleGalaStarChoiceAsync(cancellationToken);
                await RunOpeningBattlesUntilTwoContinueClicksAsync(cancellationToken);
                await RunStrategyRecognitionAsync(strategyName, strategyAliases, cancellationToken);

                if (_automationSuccessStop)
                {
                    return;
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
        await IndependentClickTextStepAsync("开始「货币战争」", ["开始货币战争", "开始", "货币战争"], CurrencyWarsFlow.RightBottom, new RatioPoint(0.82, 0.91), 8, 0.8, cancellationToken);
        await IndependentClickTextStepAsync("进入标准博弈", ["进入标准博弈", "开始标准博弈", "标准博弈"], CurrencyWarsFlow.RightBottom, new RatioPoint(0.82, 0.90), 12, 0.6, cancellationToken);
        await IndependentClickTextStepAsync("开始对局", ["开始对局", "对局"], CurrencyWarsFlow.FullWindow, new RatioPoint(0.88, 0.895), 15, 0.6, cancellationToken);
        await IndependentClickTextStepAsync("下一步", ["下一步"], CurrencyWarsFlow.FullWindow, new RatioPoint(0.88, 0.895), 15, 0.6, cancellationToken);
        await ClickRatioPointAsync(new RatioPoint(0.50, 0.58), "独立局内预设：点击空白继续", cancellationToken);
        await DelayWithCancellationAsync(1.0, cancellationToken);
        await ClickSafeInvestmentAsync(rememberChoice: true, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
        await DelayWithCancellationAsync(3.0, cancellationToken);
        await ClickRatioPointAsync(_lastSafeInvestmentPoint ?? new RatioPoint(0.5, 0.38), "独立局内预设：动画后再次点击安全投资", cancellationToken);
    }

    private async Task RunIndependentReturnToCurrencyWarsAsync(CancellationToken cancellationToken)
    {
        await ExecuteFastExitToSettlementAsync(cancellationToken);
        await DelayWithCancellationAsync(0.4, cancellationToken);
        await IndependentClickTextStepAsync("下一步", ["下一步"], CurrencyWarsFlow.FullWindow, null, 15, 0.4, cancellationToken);
        await IndependentClickTextStepAsync("下一页", ["下一页"], CurrencyWarsFlow.FullWindow, null, 15, 0.5, cancellationToken);
        await IndependentClickTextStepAsync("返回货币战争", ["返回货币战争", "返回"], CurrencyWarsFlow.FullWindow, null, 15, 0.7, cancellationToken);
    }

    private async Task<bool> ExecuteIndependentInvestmentGateAsync(IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
    {
        var hitWord = await TryClickIndependentInvestmentTargetAsync("首次投资识别", investmentGateAliases, cancellationToken);
        if (hitWord is not null)
        {
            AppendLog($"独立局内预设：投资门槛命中：{hitWord}。");
            return true;
        }

        AppendLog("独立局内预设：首次投资未命中，检查剩余次数刷新。");
        var remainingScan = await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken);
        var remaining = OcrClickResolver.FindBest(remainingScan, ["剩余次数"], _config.ButtonFuzzyScore);
        if (remaining is not null && _latestCaptureScreenRegion is not null)
        {
            var bounds = remaining.Item.Bounds;
            await ExecuteClickAsync(new ClickRequest(
                $"独立局内预设：投资刷新：{remaining.Item.Text}",
                _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
            await DelayWithCancellationAsync(0.3, cancellationToken);

            hitWord = await TryClickIndependentInvestmentTargetAsync("刷新后投资识别", investmentGateAliases, cancellationToken);
            if (hitWord is not null)
            {
                AppendLog($"独立局内预设：投资门槛命中：{hitWord}。");
                return true;
            }
        }

        await ClickSafeInvestmentAsync(rememberChoice: false, useConfiguredInvestmentTargetsForBlacklist: false, cancellationToken);
        return false;
    }

    private async Task<string?> TryClickIndependentInvestmentTargetAsync(string scope, IReadOnlyList<string> investmentGateAliases, CancellationToken cancellationToken)
    {
        AppendLog($"独立局内预设：{scope}开始，固定扫描 {InGameOpeningFlow.PresetInvestmentScanAttemptCount} 次。");
        for (var attempt = 1; attempt <= InGameOpeningFlow.PresetInvestmentScanAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendLog($"独立局内预设：{scope}第 {attempt} 次扫描上半屏。");
            var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
            AppendLog($"独立局内预设：{scope}第 {attempt} 次 OCR 原文：{ShortText(scan.RawText)}");
            var candidate = OcrClickResolver.FindBest(scan, investmentGateAliases, InGameOpeningFlow.PresetInvestmentFuzzyScore);
            if (candidate is not null && _latestCaptureScreenRegion is not null)
            {
                var bounds = candidate.Item.Bounds;
                await ExecuteClickAsync(new ClickRequest(
                    $"独立局内预设：投资门槛：{candidate.Item.Text}",
                    _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                    _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
                return candidate.Alias;
            }

            if (attempt < InGameOpeningFlow.PresetInvestmentScanAttemptCount)
            {
                await DelayWithCancellationAsync(CurrencyWarsFlow.InvestmentRecheckIntervalSeconds, cancellationToken);
            }
        }

        AppendLog($"独立局内预设：{scope}结束，未命中固定投资门槛。");
        return null;
    }

    private async Task IndependentClickTextStepAsync(
        string name,
        IReadOnlyList<string> aliases,
        RatioRegion searchRegion,
        RatioPoint? fallbackPoint,
        double timeoutSeconds,
        double standardDelaySeconds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string lastText = "";
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await CaptureAndOcrAsync(searchRegion, cancellationToken);
            lastText = scan.RawText;
            var candidate = OcrClickResolver.FindBest(scan, aliases, _config.ButtonFuzzyScore);
            if (candidate is not null && _latestCaptureScreenRegion is not null)
            {
                var bounds = candidate.Item.Bounds;
                await ExecuteClickAsync(new ClickRequest(
                    $"独立局内预设：{name}：{candidate.Item.Text}",
                    _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                    _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
                await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
                return;
            }

            await DelayWithCancellationAsync(0.6, cancellationToken);
        }

        if (fallbackPoint is not null)
        {
            AppendLog($"独立局内预设：{name} OCR 未命中，使用兜底坐标。最后 OCR：{ShortText(lastText)}");
            await ClickRatioPointAsync(fallbackPoint, $"独立局内预设：{name} 兜底", cancellationToken);
            await DelayWithCancellationAsync(standardDelaySeconds, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"独立局内预设超时：没有找到按钮文字“{name}”。最后 OCR：{ShortText(lastText)}");
    }

    private async Task DeployOpeningCharactersAsync(CancellationToken cancellationToken)
    {
        AppendLog("局内识别：固定拖拽底部前 4 个备战席到前台前 4 格。");
        var count = Math.Min(InGameOpeningFlow.PrepareSlots.Length, InGameOpeningFlow.ForwardSlots.Length);
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ExecuteDragRatioAsync(
                InGameOpeningFlow.PrepareSlots[i],
                InGameOpeningFlow.ForwardSlots[i],
                $"局内识别：备战席 {i + 1} -> 前台 {i + 1}",
                cancellationToken);
            await DelayWithCancellationAsync(InGameOpeningFlow.DragPauseSeconds, cancellationToken);
        }
    }

    private async Task TryHandleGalaStarChoiceAsync(CancellationToken cancellationToken)
    {
        await DelayWithCancellationAsync(0.5, cancellationToken);
        var scan = await CaptureAndOcrAsync(InGameOpeningFlow.DialogRegion, cancellationToken);
        if (OcrClickResolver.FindBest(scan, InGameOpeningFlow.GalaStarAliases, _config.ButtonFuzzyScore) is null)
        {
            AppendLog("局内识别：未检测到盛会之星弹窗。");
            return;
        }

        var choices = InGameOpeningFlow.GalaStarChoices;
        var index = Random.Shared.Next(choices.Length);
        AppendLog($"局内识别：检测到盛会之星弹窗，随机选择候选角色 {index + 1}。");
        await ClickRatioPointAsync(choices[index], $"局内识别：盛会之星候选 {index + 1}", cancellationToken);
        await DelayWithCancellationAsync(0.2, cancellationToken);
        await ClickRatioPointAsync(InGameOpeningFlow.GalaStarConfirmPoint, "局内识别：盛会之星确认选择", cancellationToken);
        await DelayWithCancellationAsync(0.5, cancellationToken);
    }

    private async Task<bool> ClickInGameBattleButtonAsync(int battleStartCount, CancellationToken cancellationToken)
    {
        var nextCount = battleStartCount + 1;
        AppendLog($"局内识别：准备点击第 {nextCount} 次出战，优先 OCR 查找按钮。");
        var scan = await CaptureAndOcrAsync(InGameOpeningFlow.BattleButtonRegion, cancellationToken);
        var candidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore);
        if (candidate is not null && _latestCaptureScreenRegion is not null)
        {
            var bounds = candidate.Item.Bounds;
            var request = new ClickRequest(
                $"局内识别：第 {nextCount} 次{candidate.Item.Text}",
                _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0));
            await ExecuteRepeatedClickAsync(request, InGameOpeningFlow.BattleButtonClickCount, InGameOpeningFlow.BattleButtonClickIntervalSeconds, cancellationToken);
            await TryHandleUnderfilledTeamConfirmAsync(cancellationToken);
            return true;
        }

        AppendLog($"局内识别：第 {nextCount} 次 OCR 未找到出战按钮，使用固定坐标兜底。");
        await ClickRatioPointRepeatedAsync(InGameOpeningFlow.BattleButton, $"局内识别：第 {nextCount} 次出战兜底", InGameOpeningFlow.BattleButtonClickCount, InGameOpeningFlow.BattleButtonClickIntervalSeconds, cancellationToken);
        await TryHandleUnderfilledTeamConfirmAsync(cancellationToken);
        return true;
    }

    private async Task ExecuteRepeatedClickAsync(ClickRequest request, int count, double intervalSeconds, CancellationToken cancellationToken)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reason = count == 1 ? request.Reason : $"{request.Reason} 连点 {i + 1}/{count}";
            await ExecuteClickAsync(request with { Reason = reason });
            if (i < count - 1)
            {
                await DelayWithCancellationAsync(intervalSeconds, cancellationToken);
            }
        }
    }

    private async Task ClickRatioPointRepeatedAsync(RatioPoint point, string reason, int count, double intervalSeconds, CancellationToken cancellationToken)
    {
        if (_gameWindow is null)
        {
            throw new InvalidOperationException("没有可用的游戏窗口。");
        }

        var rect = _gameWindow.ClientRect;
        var request = new ClickRequest(
            reason,
            rect.Left + (int)Math.Round(rect.Width * point.X),
            rect.Top + (int)Math.Round(rect.Height * point.Y));
        await ExecuteRepeatedClickAsync(request, count, intervalSeconds, cancellationToken);
    }

    private async Task TryHandleUnderfilledTeamConfirmAsync(CancellationToken cancellationToken)
    {
        await DelayWithCancellationAsync(0.5, cancellationToken);
        var scan = await CaptureAndOcrAsync(InGameOpeningFlow.DialogRegion, cancellationToken);
        if (OcrClickResolver.FindBest(scan, InGameOpeningFlow.UnderfilledTeamAliases, _config.ButtonFuzzyScore) is null)
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
        var battleStartCount = 0;
        var continueChallengeCount = 0;
        var deadline = DateTime.UtcNow.AddSeconds(InGameOpeningFlow.BoardWaitTimeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);

            var continueCandidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.ContinueButtonAliases, _config.ButtonFuzzyScore);
            if (continueCandidate is not null && _latestCaptureScreenRegion is not null)
            {
                var bounds = continueCandidate.Item.Bounds;
                await ExecuteClickAsync(new ClickRequest(
                    $"局内识别：{continueCandidate.Item.Text}",
                    _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                    _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));

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
                continue;
            }

            if (OcrClickResolver.FindBest(scan, InGameOpeningFlow.BattleButtonAliases, _config.ButtonFuzzyScore) is not null)
            {
                if (await ClickInGameBattleButtonAsync(battleStartCount, cancellationToken))
                {
                    battleStartCount++;
                    AppendLog($"局内识别：已点击出战 {battleStartCount} 次。");
                    await DelayWithCancellationAsync(InGameOpeningFlow.AfterBattleClickSeconds, cancellationToken);
                }

                continue;
            }

            await ClickRatioPointAsync(InGameOpeningFlow.ContinueFallbackPoint, "局内识别：点击空白继续兜底", cancellationToken);

            await DelayWithCancellationAsync(1.0, cancellationToken);
        }

        AppendLog("局内识别：开局两把等待超时，按当前状态结束。");
    }

    private async Task RunStrategyRecognitionAsync(string strategyName, IReadOnlyList<string> strategyAliases, CancellationToken cancellationToken)
    {
        await DelayWithCancellationAsync(InGameOpeningFlow.StrategyScreenDelaySeconds, cancellationToken);
        AppendLog($"局内识别：开始策略识别，目标：{strategyName}。");
        if (!await IsStrategySelectionScreenAsync(cancellationToken))
        {
            AppendLog("局内识别：当前不是策略选择界面，本轮不做策略命中判断。");
            return;
        }

        if (await TryClickTargetStrategyAsync("首次策略识别", strategyAliases, InGameOpeningFlow.InitialStrategyScanAttemptCount, cancellationToken))
        {
            StopAutomationForSuccess("状态：局内策略命中停止");
            return;
        }

        AppendLog("局内识别：首次策略未命中，点击 3 个刷新按钮。");
        foreach (var point in InGameOpeningFlow.StrategyRefreshButtons)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ClickRatioPointAsync(point, "局内识别：刷新策略", cancellationToken);
            await DelayWithCancellationAsync(InGameOpeningFlow.StrategyRefreshDelaySeconds, cancellationToken);
        }

        if (await TryClickTargetStrategyAsync("左中右刷新后策略识别", strategyAliases, InGameOpeningFlow.PostRefreshStrategyScanAttemptCount, cancellationToken))
        {
            StopAutomationForSuccess("状态：局内策略命中停止");
            return;
        }

        var rightRefreshPoint = InGameOpeningFlow.StrategyRefreshButtons[^1];
        for (var i = 0; i < InGameOpeningFlow.ExtraRightStrategyRefreshCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ClickRatioPointAsync(rightRefreshPoint, $"局内识别：额外刷新右侧策略 {i + 1}/{InGameOpeningFlow.ExtraRightStrategyRefreshCount}", cancellationToken);
            await DelayWithCancellationAsync(InGameOpeningFlow.StrategyRefreshDelaySeconds, cancellationToken);
            if (await TryClickTargetStrategyAsync($"右侧第 {i + 2} 次刷新后策略识别", strategyAliases, InGameOpeningFlow.PostRefreshStrategyScanAttemptCount, cancellationToken))
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
        var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.FullWindow, cancellationToken);
        var candidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.StrategyScreenAliases, InGameOpeningFlow.StrategyFuzzyScore);
        if (candidate is null)
        {
            return false;
        }

        AppendLog($"局内识别：确认策略选择界面：{candidate.Item.Text}（匹配 {candidate.Alias}）。");
        return true;
    }

    private async Task<bool> TryClickTargetStrategyAsync(string scope, IReadOnlyList<string> strategyAliases, int scanAttemptCount, CancellationToken cancellationToken)
    {
        AppendLog($"局内识别：{scope}开始。");
        for (var attempt = 1; attempt <= scanAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendLog($"局内识别：{scope}第 {attempt} 次扫描。");
            var scan = await CaptureAndOcrAsync(InGameOpeningFlow.StrategyRegion, cancellationToken);
            var candidate = OcrClickResolver.FindBest(scan, strategyAliases, InGameOpeningFlow.StrategyFuzzyScore);
            if (candidate is not null && _latestCaptureScreenRegion is not null)
            {
                var bounds = candidate.Item.Bounds;
                await ExecuteClickAsync(new ClickRequest(
                    $"局内策略：{candidate.Item.Text}",
                    _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                    _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
                AppendLog($"局内识别：{scope}命中目标策略：{candidate.Alias}。");
                return true;
            }

            if (attempt < scanAttemptCount)
            {
                await DelayWithCancellationAsync(InGameOpeningFlow.StrategyScanIntervalSeconds, cancellationToken);
            }
        }

        AppendLog($"局内识别：{scope}未命中目标策略。");
        return false;
    }

    private async Task ClickRandomStrategyCardAsync(CancellationToken cancellationToken)
    {
        var points = InGameOpeningFlow.StrategyCards;
        var scan = await CaptureAndOcrAsync(InGameOpeningFlow.StrategyRegion, cancellationToken);
        var blockedColumns = FindBlacklistedStrategyColumns(scan);
        var candidates = Enumerable.Range(0, points.Length)
            .Where(index => !blockedColumns.Contains(index))
            .ToList();
        if (candidates.Count == 0)
        {
            candidates = Enumerable.Range(0, points.Length).ToList();
        }

        if (blockedColumns.Count > 0)
        {
            AppendLog($"局内识别：随机选策略时避开黑名单列 {string.Join("、", blockedColumns.Select(index => index + 1))}。");
        }

        var index = candidates[Random.Shared.Next(candidates.Count)];
        await ClickRatioPointAsync(points[index], $"局内识别：随机选择策略 {index + 1}", cancellationToken);
        await DelayWithCancellationAsync(0.3, cancellationToken);
    }

    private HashSet<int> FindBlacklistedStrategyColumns(OcrScanResult scan)
    {
        var blockedColumns = new HashSet<int>();
        foreach (var item in scan.Items)
        {
            if (InGameOpeningFlow.StrategyChoiceBlacklist.Any(word => TextMatcher.FuzzyContains(item.Text, word, InGameOpeningFlow.StrategyFuzzyScore)))
            {
                blockedColumns.Add(GetStrategyColumn(item));
            }
        }

        return blockedColumns;
    }

    private int GetStrategyColumn(OcrTextItem item)
    {
        var centerX = item.Bounds.X + item.Bounds.Width / 2.0;
        var width = Math.Max(1, _latestCaptureScreenRegion?.Width ?? 1);
        var relativeX = centerX / width;
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
        var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken);
        var candidate = OcrClickResolver.FindBest(scan, InGameOpeningFlow.StrategyConfirmAliases, _config.ButtonFuzzyScore);
        if (candidate is not null && _latestCaptureScreenRegion is not null)
        {
            var bounds = candidate.Item.Bounds;
            await ExecuteClickAsync(new ClickRequest(
                $"局内识别：{candidate.Item.Text}",
                _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
            return;
        }

        AppendLog("局内识别：OCR 未找到确认按钮，使用固定坐标兜底。");
        await ClickRatioPointAsync(InGameOpeningFlow.StrategyConfirmPoint, "局内识别：策略确认兜底", cancellationToken);
    }

    private async Task StartAutomationAsync()
    {
        if (_automationCts is not null)
        {
            return;
        }

        ReadUiToConfig();
        _configStore.Save(_config);
        if (_config.DebuffEnabled && _config.TargetWords.Count == 0)
        {
            MessageBox.Show(this, "主词条检测开启时，请先添加至少一个目标词条。", "缺少目标词条", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _automationCts = new CancellationTokenSource();
        _automationSuccessStop = false;
        SetAutomationButtonsEnabled(false);
        _lastSafeInvestmentPoint = null;
        _blockedHitThisCycle = false;
        SetStatus("状态：自动刷新运行中");

        var runtime = new AutomationRuntime(
            _config,
            ExecuteFlowStepAsync,
            DelayWithCancellationAsync,
            VariableDelayWithCancellationAsync,
            message =>
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
                MessageBox.Show(this, "成功刷出目标词条或投资词条！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"自动流程失败：{ex.Message}");
            SetStatus("状态：自动流程失败");
            MessageBox.Show(this, ex.Message, "自动流程失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _automationCts?.Dispose();
            _automationCts = null;
            SetAutomationButtonsEnabled(true);
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
        AppendLog($"自动流程：投资词条命中：{hitWord}，停止。");
        DecisionReasonText.Text = $"当前决策：投资词条命中：{hitWord}，停止。";
        StopAutomationForSuccess("状态：投资识别成功停止");
    }

    private async Task ExecuteFlowStepAsync(FlowStep step, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(step, CurrencyWarsFlow.Steps[0]))
        {
            _blockedHitThisCycle = false;
        }

        if (_gameWindow is null && !TryFindWindow())
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
        var key = step.Key ?? "";
        var result = await _clickService.PressKeyAsync(key, _gameWindow?.Handle ?? nint.Zero, cancellationToken);
        AppendLog(result.Message);
    }

    private async Task ExecuteFastExitToSettlementAsync(CancellationToken cancellationToken)
    {
        if (_gameWindow is null)
        {
            throw new InvalidOperationException("没有可用的游戏窗口。");
        }

        await ClickEscAndSettlementPointsAsync(cancellationToken);
    }

    private async Task ClickEscAndSettlementPointsAsync(CancellationToken cancellationToken)
    {
        AppendLog($"自动流程：直接交替点击左上角退出和放弃并结算，各 {CurrencyWarsFlow.FastExitSettlementAlternateClickCount} 次。");
        for (var i = 0; i < CurrencyWarsFlow.FastExitSettlementAlternateClickCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ClickRatioPointAsync(CurrencyWarsFlow.FastEscApproxPoint, $"左上角退出区域 第 {i + 1} 次", cancellationToken);
            await DelayWithCancellationAsync(CurrencyWarsFlow.FastExitProbeIntervalSeconds, cancellationToken);
            await ClickRatioPointAsync(CurrencyWarsFlow.FastSettlementApproxPoint, $"放弃并结算区域 第 {i + 1} 次", cancellationToken);
            await DelayWithCancellationAsync(CurrencyWarsFlow.FastExitProbeIntervalSeconds, cancellationToken);
        }
    }

    private async Task ExecuteClickTextStepAsync(FlowStep step, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(step.TimeoutSeconds);
        string lastText = "";

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await CaptureAndOcrAsync(step.SearchRegion, cancellationToken);
            lastText = scan.RawText;
            var candidate = OcrClickResolver.FindBest(scan, step.Aliases, _config.ButtonFuzzyScore);
            if (candidate is not null && _latestCaptureScreenRegion is not null)
            {
                var bounds = candidate.Item.Bounds;
                var request = new ClickRequest(
                    $"{step.Name}：{candidate.Item.Text}",
                    _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                    _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0));
                await ExecuteClickAsync(request);
                SetStatus($"状态：已点击 {step.Name}");
                return;
            }

            await DelayWithCancellationAsync(0.6, cancellationToken);
        }

        if (step.FallbackPoint is not null)
        {
            AppendLog($"自动流程：{step.Name} OCR 未命中，使用兜底坐标。最后 OCR：{ShortText(lastText)}");
            await ClickRatioPointAsync(step.FallbackPoint, $"{step.Name} 兜底", cancellationToken);
            return;
        }

        throw new InvalidOperationException($"超时：没有找到按钮文字“{step.Name}”。最后 OCR：{ShortText(lastText)}");
    }

    private async Task WaitForDebuffResultAsync(CancellationToken cancellationToken)
    {
        await DelayWithCancellationAsync(_config.DebuffCheckDelaySeconds, cancellationToken);
        var deadline = DateTime.UtcNow.AddSeconds(CurrencyWarsFlow.DebuffRecheckTimeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken);
            var evaluation = _scanEvaluator.Evaluate(_config, scan.RawText);
            EvaluationTextBox.Text = FormatEvaluation(evaluation);
            ApplyEvaluationSummary(evaluation);
            _blockedHitThisCycle = evaluation.BlockedHit;
            if (evaluation.DebuffSuccess)
            {
                AppendLog($"自动流程：{evaluation.DecisionReason} 停止。");
                StopAutomationForSuccess("状态：主词条成功停止");
                return;
            }

            if (IsDebuffScreenReady(scan.RawText))
            {
                AppendLog("自动流程：词条页已识别，未命中，继续后续段落。");
                return;
            }

            await DelayWithCancellationAsync(CurrencyWarsFlow.DebuffRecheckIntervalSeconds, cancellationToken);
        }

        AppendLog("自动流程：词条页等待超时，按当前结果继续后续段落。");
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

        var hitWord = await TryClickInvestmentTargetAsync("首次投资识别", cancellationToken);
        if (hitWord is not null)
        {
            ResolveInvestmentHit(hitWord);
            return;
        }

        AppendLog("自动流程：首次投资识别未命中，准备检查剩余次数刷新。");
        var remainingScan = await CaptureAndOcrAsync(CurrencyWarsFlow.BottomHalf, cancellationToken);
        var remaining = OcrClickResolver.FindBest(remainingScan, ["剩余次数"], _config.ButtonFuzzyScore);
        if (remaining is not null && _latestCaptureScreenRegion is not null)
        {
            var bounds = remaining.Item.Bounds;
            await ExecuteClickAsync(new ClickRequest(
                $"投资刷新：{remaining.Item.Text}",
                _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
            await DelayWithCancellationAsync(_config.InvestmentIntervalSeconds, cancellationToken);

            hitWord = await TryClickInvestmentTargetAsync("刷新后投资识别", cancellationToken);
            if (hitWord is not null)
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
        for (var attempt = 1; attempt <= CurrencyWarsFlow.InvestmentScanAttemptCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetStatus($"状态：{scope} 第 {attempt} 次");
            AppendLog($"自动流程：{scope} 第 {attempt} 次扫描上半屏。");
            var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
            if (logRawText)
            {
                AppendLog($"自动流程：{scope} 第 {attempt} 次 OCR 原文：{ShortText(scan.RawText)}");
            }

            var candidate = OcrClickResolver.FindBest(scan, _config.InvestmentTargets, _config.InvestmentFuzzyScore);
            if (candidate is not null && _latestCaptureScreenRegion is not null)
            {
                var bounds = candidate.Item.Bounds;
                await ExecuteClickAsync(new ClickRequest(
                    $"投资词条：{candidate.Item.Text}",
                    _latestCaptureScreenRegion.Left + (int)Math.Round(bounds.X + bounds.Width / 2.0),
                    _latestCaptureScreenRegion.Top + (int)Math.Round(bounds.Y + bounds.Height / 2.0)));
                return candidate.Alias;
            }

            if (attempt < CurrencyWarsFlow.InvestmentScanAttemptCount)
            {
                await DelayWithCancellationAsync(CurrencyWarsFlow.InvestmentRecheckIntervalSeconds, cancellationToken);
            }
        }

        AppendLog($"自动流程：{scope}结束，未命中投资词条。");
        return null;
    }

    private async Task ClickSafeInvestmentAsync(bool rememberChoice, bool useConfiguredInvestmentTargetsForBlacklist, CancellationToken cancellationToken)
    {
        var scan = await CaptureAndOcrAsync(CurrencyWarsFlow.TopHalf, cancellationToken);
        var point = ChooseSafeInvestmentPoint(scan, useConfiguredInvestmentTargetsForBlacklist);
        if (rememberChoice)
        {
            _lastSafeInvestmentPoint = point;
        }

        await ClickRatioPointAsync(point, "默认安全投资", cancellationToken);
    }

    private RatioPoint ChooseSafeInvestmentPoint(OcrScanResult scan, bool useConfiguredInvestmentTargetsForBlacklist)
    {
        var activeTargets = useConfiguredInvestmentTargetsForBlacklist ? _config.InvestmentTargets : [];
        var blockedColumns = FindBlacklistedInvestmentColumns(scan, activeTargets);
        var preferredOrder = new[] { 1, 0, 2 };
        var chosenIndex = preferredOrder.FirstOrDefault(index => !blockedColumns.Contains(index));
        if (blockedColumns.Contains(chosenIndex))
        {
            chosenIndex = 1;
        }

        if (blockedColumns.Count > 0)
        {
            AppendLog($"默认投资选择：避开特殊投资列 {string.Join("、", blockedColumns.Select(index => index + 1))}");
        }

        return CurrencyWarsFlow.InvestmentFallbackPoints[chosenIndex];
    }

    private HashSet<int> FindBlacklistedInvestmentColumns(OcrScanResult scan, IReadOnlyList<string> activeTargets)
    {
        var normalizedTargets = activeTargets.Select(TextMatcher.Normalize).ToHashSet(StringComparer.Ordinal);
        var blacklist = CurrencyWarsFlow.SpecialInvestmentBlacklist
            .Where(word => !normalizedTargets.Contains(TextMatcher.Normalize(word)))
            .ToList();
        var blockedColumns = new HashSet<int>();
        var score = Math.Max(76, _config.InvestmentFuzzyScore - 10);

        foreach (var item in scan.Items)
        {
            if (blacklist.Any(word => TextMatcher.FuzzyContains(item.Text, word, score)))
            {
                blockedColumns.Add(GetInvestmentColumn(item));
            }
        }

        return blockedColumns;
    }

    private int GetInvestmentColumn(OcrTextItem item)
    {
        var centerX = item.Bounds.X + item.Bounds.Width / 2.0;
        var width = Math.Max(1, _latestCaptureScreenRegion?.Width ?? 1);
        var relativeX = centerX / width;
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
        if (_gameWindow is null)
        {
            throw new InvalidOperationException("没有可用的游戏窗口。");
        }

        var rect = _gameWindow.ClientRect;
        var request = new ClickRequest(
            reason,
            rect.Left + (int)Math.Round(rect.Width * point.X),
            rect.Top + (int)Math.Round(rect.Height * point.Y));
        await ExecuteClickAsync(request);
    }

    private async Task<OcrScanResult> CaptureAndOcrAsync(RatioRegion region, CancellationToken cancellationToken)
    {
        if (_gameWindow is null)
        {
            throw new InvalidOperationException("没有可用的游戏窗口。");
        }

        var captureRegion = new CaptureRegion("自动流程区域", region.X, region.Y, region.Width, region.Height);
        var resolved = _windowCapture.ResolveRegion(_gameWindow.ClientRect, captureRegion);
        var image = _windowCapture.Capture(_gameWindow, captureRegion);
        _latestPreviewImage = image;
        _latestCaptureScreenRegion = resolved;
        _latestPreviewRegion = captureRegion;
        PreviewImage.Source = image;
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        CaptureInfoText.Text = $"截图：自动流程区域  {resolved.Width}x{resolved.Height}  left={resolved.Left}, top={resolved.Top}";

        var scan = await _ocrService.RecognizeAsync(image, cancellationToken);
        _latestOcrResult = scan;
        OcrRawTextBox.Text = FormatOcrResult(captureRegion.Name, scan);
        OcrInfoText.Text = $"OCR：自动流程区域，文本块 {scan.Items.Count}，字符 {scan.RawText.Length}";
        return scan;
    }

    private static bool IsDebuffScreenReady(string ocrText)
    {
        var normalized = TextMatcher.Normalize(ocrText);
        return CurrencyWarsFlow.DebuffScreenHints.Any(hint => normalized.Contains(TextMatcher.Normalize(hint), StringComparison.Ordinal));
    }

    private static async Task DelayWithCancellationAsync(double seconds, CancellationToken cancellationToken)
    {
        if (seconds <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    private Task VariableDelayWithCancellationAsync(double seconds, CancellationToken cancellationToken)
    {
        return DelayWithCancellationAsync(Math.Max(0.0, seconds * _config.SpeedFactor()), cancellationToken);
    }

    private static string ShortText(string text)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= 80 ? text : text[..80] + "...";
    }

    private static string FormatOcrResult(string scope, OcrScanResult result)
    {
        var header = $"范围：{scope}{Environment.NewLine}时间：{result.ScannedAt:HH:mm:ss}{Environment.NewLine}文本块：{result.Items.Count}{Environment.NewLine}{Environment.NewLine}";
        if (string.IsNullOrWhiteSpace(result.RawText))
        {
            return header + "没有识别到文本。";
        }

        return header + result.RawText;
    }

    private static string FormatEvaluation(BasicScanEvaluation evaluation)
    {
        return string.Join(Environment.NewLine,
            $"主词条检测：{(evaluation.DebuffSuccess ? "成功" : "未成功")}",
            $"命中模式：{evaluation.DebuffModeText}",
            $"主词条已命中：{JoinWords(evaluation.TargetMatch.HitWords)}",
            $"主词条未命中：{JoinWords(evaluation.TargetMatch.MissingWords)}",
            $"不想要词条命中：{JoinWords(evaluation.BlockedMatch.HitWords)}",
            $"投资词条命中：{JoinWords(evaluation.InvestmentMatch.HitWords)}",
            $"当前决策：{evaluation.DecisionReason}",
            "",
            "说明：手动 OCR 会显示当前冲突处理模式下的解释；自动流程会按同一套模式决定停止或继续。");
    }

    private void ApplyEvaluationSummary(BasicScanEvaluation? evaluation)
    {
        if (evaluation is null)
        {
            HitWordsText.Text = "已命中：无";
            BlockedHitWordsText.Text = "不想要命中：无";
            MissingWordsText.Text = "未命中：无";
            DecisionReasonText.Text = "当前决策：未评估";
            return;
        }

        HitWordsText.Text = $"已命中：{JoinWords(evaluation.TargetMatch.HitWords)}";
        BlockedHitWordsText.Text = $"不想要命中：{JoinWords(evaluation.BlockedMatch.HitWords)}";
        MissingWordsText.Text = $"未命中：{JoinWords(evaluation.TargetMatch.MissingWords)}";
        DecisionReasonText.Text = $"当前决策：{evaluation.DecisionReason}";
    }

    private static string JoinWords(IReadOnlyList<string> words)
    {
        return words.Count == 0 ? "无" : string.Join("、", words);
    }

    private static IOcrService CreateOcrService()
    {
        var bridgeExe = Path.Combine(AppContext.BaseDirectory, "OCRRuntime", "rapidocr_bridge.exe");
        if (File.Exists(bridgeExe))
        {
            return new ExternalRapidOcrService(bridgeExe);
        }

        var bridgeScript = Path.Combine(AppContext.BaseDirectory, "Tools", "rapidocr_bridge.py");
        if (!File.Exists(bridgeScript))
        {
            return new PendingOcrService($"找不到桥接脚本：{bridgeScript}");
        }

        var pythonExe = FindPythonExe();
        if (pythonExe is null)
        {
            return new PendingOcrService("找不到可用 Python。");
        }

        return new ExternalRapidOcrService(pythonExe, bridgeScript);
    }

    private static string? FindPythonExe()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "OCRRuntime", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, "ocr_runtime", "python.exe"),
            Path.Combine(AppContext.BaseDirectory, ".venv", "Scripts", "python.exe"),
            @"C:\Users\SHINELON\Documents\Codex\2026-06-07\windows-python-ocr-debuff-1-python\outputs\debuff_ocr_tool\.venv\Scripts\python.exe"
        };

        foreach (var candidate in candidates)
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
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    private void RegisterHotkeys()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        _hotkeySource = HwndSource.FromHwnd(handle);
        _hotkeySource?.AddHook(HotkeyHook);

        RegisterHotKey(handle, HotkeyStopId, ModNoRepeat, VkF8);
        AppendLog("热键已注册：F8 停止。");
    }

    private void UnregisterHotkeys()
    {
        var helper = new WindowInteropHelper(this);
        var handle = helper.Handle;
        UnregisterHotKey(handle, HotkeyStopId);
        _hotkeySource?.RemoveHook(HotkeyHook);
        _hotkeySource = null;
    }

    private nint HotkeyHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmHotkey)
        {
            return nint.Zero;
        }

        handled = true;
        switch (wParam.ToInt32())
        {
            case HotkeyStopId:
                StopAutomation();
                break;
        }

        return nint.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint windowHandle, int id);
}
