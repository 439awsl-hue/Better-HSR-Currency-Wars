using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class AutomationConfig
{
    public const int MaxTargetAllWords = 4;
    public const int MaxTargetAnyWords = 20;
    public const int MaxInvestmentWords = 20;
    public const int MaxInGameWords = 20;
    public const int WordHistoryLimit = 100;

    public string WindowTitle { get; set; } = "";
    public bool DebuffEnabled { get; set; } = true;
    public bool DebuffMatchAny { get; set; }
    public List<string> TargetWords { get; set; } = [];
    public bool BlockedEnabled { get; set; }
    public List<string> BlockedWords { get; set; } = [];
    public bool InvestmentEnabled { get; set; }
    public List<string> InvestmentTargets { get; set; } = [];
    public bool CheckInvestmentWhenBlocked { get; set; }
    public string InGameStrategyTarget { get; set; } = "";
    public string InGameInvestmentTarget { get; set; } = "";
    public List<string> InGameStrategyTargets { get; set; } = [];
    public List<string> InGameInvestmentTargets { get; set; } = [];
    public List<string> TargetWordHistory { get; set; } = [];
    public List<string> BlockedWordHistory { get; set; } = [];
    public List<string> InvestmentWordHistory { get; set; } = [];
    public List<string> InGameStrategyHistory { get; set; } = [];
    public List<string> InGameInvestmentHistory { get; set; } = [];
    public int FuzzyScore { get; set; } = 82;
    public int ButtonFuzzyScore { get; set; } = 78;
    public int InvestmentFuzzyScore { get; set; } = 88;
    public string SpeedMode { get; set; } = "standard";
    public double StartDelaySeconds { get; set; } = 1.0;
    public double DebuffCheckDelaySeconds { get; set; } = 4.0;
    public double InvestmentIntervalSeconds { get; set; } = 0.2;

    public void Normalize()
    {
        if (SpeedMode is not ("fast" or "standard" or "slow"))
        {
            SpeedMode = "standard";
        }

        var targetLimit = DebuffMatchAny ? MaxTargetAnyWords : MaxTargetAllWords;
        TargetWords = NormalizeWords(TargetWords, targetLimit);
        BlockedWords = NormalizeWords(BlockedWords, MaxTargetAnyWords);
        InvestmentTargets = NormalizeWords(InvestmentTargets, MaxInvestmentWords);
        InGameStrategyTargets = NormalizeWords(InGameStrategyTargets, MaxInGameWords);
        InGameInvestmentTargets = NormalizeWords(InGameInvestmentTargets, MaxInGameWords);
        if (InGameStrategyTargets.Count == 0 && !string.IsNullOrWhiteSpace(InGameStrategyTarget))
        {
            InGameStrategyTargets = NormalizeWords([InGameStrategyTarget], MaxInGameWords);
        }

        if (InGameInvestmentTargets.Count == 0 && !string.IsNullOrWhiteSpace(InGameInvestmentTarget))
        {
            InGameInvestmentTargets = NormalizeWords([InGameInvestmentTarget], MaxInGameWords);
        }

        InGameStrategyTarget = InGameStrategyTargets.FirstOrDefault() ?? "";
        InGameInvestmentTarget = InGameInvestmentTargets.FirstOrDefault() ?? "";

        TargetWordHistory = MergeHistory(TargetWords, TargetWordHistory);
        BlockedWordHistory = MergeHistory(BlockedWords, BlockedWordHistory);
        InvestmentWordHistory = MergeHistory(InvestmentTargets, InvestmentWordHistory);
        InGameStrategyHistory = MergeHistory(InGameStrategyTargets, InGameStrategyHistory);
        InGameInvestmentHistory = MergeHistory(InGameInvestmentTargets, InGameInvestmentHistory);
    }

    public double SpeedFactor()
    {
        return SpeedMode switch
        {
            "fast" => 0.4,
            "slow" => 1.35,
            _ => 1.0
        };
    }

    private static List<string> NormalizeWords(IEnumerable<string>? words, int maxCount)
    {
        return (words ?? [])
            .Select(word => word.Trim())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Distinct(StringComparer.Ordinal)
            .Take(maxCount)
            .ToList();
    }

    private static List<string> MergeHistory(params IEnumerable<string>[] wordLists)
    {
        var history = new List<string>();
        foreach (var words in wordLists)
        {
            foreach (var word in words)
            {
                var value = word.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                history.Remove(value);
                history.Insert(0, value);
            }
        }

        return history.Take(WordHistoryLimit).ToList();
    }
}

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigStore(string appDirectory)
    {
        ConfigPath = Path.Combine(appDirectory, "config.clean.json");
    }

    public string ConfigPath { get; }

    public AutomationConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            return new AutomationConfig();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AutomationConfig>(json, JsonOptions) ?? new AutomationConfig();
            config.Normalize();
            return config;
        }
        catch
        {
            return new AutomationConfig();
        }
    }

    public void Save(AutomationConfig config)
    {
        config.Normalize();
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
