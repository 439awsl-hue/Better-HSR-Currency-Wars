using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HsrCurrencyWarsCleanWpf.Core;

public sealed class ConfigStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		Converters = { (JsonConverter)new JsonStringEnumConverter() }
	};

	public string ConfigPath { get; }

	public ConfigStore(string appDirectory)
	{
		ConfigPath = Path.Combine(appDirectory, "config.clean.json");
	}

	public AutomationConfig Load()
	{
		if (!File.Exists(ConfigPath))
		{
			return new AutomationConfig();
		}
		try
		{
			AutomationConfig? obj = JsonSerializer.Deserialize<AutomationConfig>(File.ReadAllText(ConfigPath), JsonOptions) ?? new AutomationConfig();
			obj.Normalize();
			return obj;
		}
		catch
		{
			return new AutomationConfig();
		}
	}

	public void Save(AutomationConfig config)
	{
		config.Normalize();
		string json = JsonSerializer.Serialize(config, JsonOptions);
		File.WriteAllText(ConfigPath, json);
	}
}
