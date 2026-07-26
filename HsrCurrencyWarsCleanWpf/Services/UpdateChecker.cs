using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HsrCurrencyWarsCleanWpf.Services;

public static class UpdateChecker
{
	private const string UpdateManifestUrl = "https://439awsl-hue.github.io/Better-HSR-Currency-Wars/update.json";

	private static readonly HttpClient Http = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(8L)
	};

	public static string CurrentVersion
	{
		get
		{
			Assembly assembly = Assembly.GetExecutingAssembly();
			return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
		}
	}

	public static async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_ = 3;
		try
		{
			using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://439awsl-hue.github.io/Better-HSR-Currency-Wars/update.json");
			request.Headers.UserAgent.ParseAdd("Better-HSR-Currency-Wars-Updater");
			using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				return new UpdateCheckResult(IsConfigured: true, HasUpdate: false, $"更新配置读取失败：{(int)response.StatusCode} {response.ReasonPhrase}。请确认 GitHub Pages 已开启并发布 update.json。", null);
			}
			response.EnsureSuccessStatusCode();
			UpdateCheckResult result;
			await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
			{
				using JsonDocument document = await JsonDocument.ParseAsync(stream, default(JsonDocumentOptions), cancellationToken);
				JsonElement root = document.RootElement;
				string remoteVersion = GetString(root, "version");
				if (string.IsNullOrWhiteSpace(remoteVersion))
				{
					result = new UpdateCheckResult(IsConfigured: true, HasUpdate: false, "更新检查失败：update.json 没有 version。", null);
				}
				else
				{
					string releasePageUrl = GetString(root, "releasePageUrl");
					string title = GetString(root, "title");
					string notes = GetString(root, "notes");
					string downloadUrl = GetString(root, "downloadUrl");
					if (!IsRemoteNewer(remoteVersion, CurrentVersion))
					{
						result = new UpdateCheckResult(IsConfigured: true, HasUpdate: false, "当前已是最新版本：" + CurrentVersion, null);
					}
					else
					{
						UpdateInfo update = new UpdateInfo(remoteVersion, string.IsNullOrWhiteSpace(title) ? remoteVersion : title, notes, releasePageUrl, downloadUrl);
						result = new UpdateCheckResult(IsConfigured: true, HasUpdate: true, "发现新版本：" + remoteVersion, update);
					}
				}
			}
			return result;
		}
		catch (OperationCanceledException)
		{
			return new UpdateCheckResult(IsConfigured: true, HasUpdate: false, "更新检查已取消或超时。", null);
		}
		catch (Exception ex2)
		{
			return new UpdateCheckResult(IsConfigured: true, HasUpdate: false, "更新检查失败：" + ex2.Message + "。请确认 GitHub Pages 已开启并发布 update.json。", null);
		}
	}

	private static string GetString(JsonElement root, string propertyName)
	{
		if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static bool IsRemoteNewer(string remoteVersion, string currentVersion)
	{
		Version remote = ParseVersion(remoteVersion);
		Version current = ParseVersion(currentVersion);
		if ((object)remote != null && (object)current != null)
		{
			return remote > current;
		}
		return false;
	}

	private static Version? ParseVersion(string value)
	{
		if (Version.TryParse(new string(value.Trim().TrimStart(new char[2] { 'v', 'V' }).TakeWhile((char ch) => char.IsDigit(ch) || ch == '.')
			.ToArray()).Trim('.'), out Version version))
		{
			return version;
		}
		return null;
	}
}
