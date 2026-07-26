using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using HsrCurrencyWarsCleanWpf.Core;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed class ExternalRapidOcrService : IOcrService, IDisposable
{
	private sealed class OcrBridgeResponse
	{
		[JsonPropertyName("raw_text")]
		public string? RawText { get; set; }

		[JsonPropertyName("items")]
		public List<OcrBridgeItem> Items { get; set; } = new List<OcrBridgeItem>();

		[JsonPropertyName("error")]
		public string? Error { get; set; }
	}

	private sealed class OcrBridgeItem
	{
		[JsonPropertyName("text")]
		public string? Text { get; set; }

		[JsonPropertyName("confidence")]
		public double Confidence { get; set; }

		[JsonPropertyName("bounds")]
		public OcrBridgeBounds Bounds { get; set; } = new OcrBridgeBounds();
	}

	private sealed class OcrBridgeBounds
	{
		[JsonPropertyName("x")]
		public double X { get; set; }

		[JsonPropertyName("y")]
		public double Y { get; set; }

		[JsonPropertyName("width")]
		public double Width { get; set; }

		[JsonPropertyName("height")]
		public double Height { get; set; }
	}

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly string _executablePath;

	private readonly string? _bridgeScript;

	private readonly TimeSpan _timeout;

	private readonly SemaphoreSlim _requestLock = new SemaphoreSlim(1, 1);

	private Process? _process;

	public string Name { get; }

	public ExternalRapidOcrService(string executablePath, string? bridgeScript = null, TimeSpan? timeout = null)
	{
		_executablePath = executablePath;
		_bridgeScript = bridgeScript;
		_timeout = timeout ?? TimeSpan.FromSeconds(30L);
		Name = "RapidOCR 常驻进程";
	}

	public async Task<OcrScanResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default(CancellationToken))
	{
		await _requestLock.WaitAsync(cancellationToken);
		string tempPath = Path.Combine(Path.GetTempPath(), $"hsr-clean-ocr-{Guid.NewGuid():N}.png");
		bool shouldRestartProcess = false;
		try
		{
			SavePng(image, tempPath);
			string request = JsonSerializer.Serialize(new
			{
				image_path = tempPath
			});
			OcrBridgeResponse response = JsonSerializer.Deserialize<OcrBridgeResponse>(await SendRequestAsync(request, cancellationToken), JsonOptions);
			if (response == null || !string.IsNullOrWhiteSpace(response.Error))
			{
				string? obj = response?.Error ?? "OCR 返回为空。";
				if (IsStaleTempFileError(obj))
				{
					shouldRestartProcess = true;
				}
				throw new InvalidOperationException(obj);
			}
			List<OcrTextItem> items = (from item in response.Items
				select new OcrTextItem(item.Text ?? "", new Rect(item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height), item.Confidence) into item
				where !string.IsNullOrWhiteSpace(item.Text)
				select item).ToList();
			return new OcrScanResult(response.RawText ?? "", items, DateTime.Now);
		}
		catch (OperationCanceledException)
		{
			shouldRestartProcess = true;
			throw;
		}
		catch (TimeoutException)
		{
			shouldRestartProcess = true;
			throw;
		}
		finally
		{
			if (shouldRestartProcess)
			{
				RestartProcess();
			}
			TryDelete(tempPath);
			_requestLock.Release();
		}
	}

	public void Dispose()
	{
		if (_process == null)
		{
			return;
		}
		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}
		finally
		{
			_process.Dispose();
			_requestLock.Dispose();
			_process = null;
		}
	}

	private async Task<string> SendRequestAsync(string request, CancellationToken cancellationToken)
	{
		EnsureProcess();
		if (_process?.StandardInput == null || _process.StandardOutput == null)
		{
			throw new InvalidOperationException("OCR 进程没有正确启动。");
		}
		using CancellationTokenSource timeoutCts = new CancellationTokenSource(_timeout);
		using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
		_ = 2;
		try
		{
			await _process.StandardInput.WriteLineAsync(request.AsMemory(), linkedCts.Token);
			await _process.StandardInput.FlushAsync(linkedCts.Token);
			string obj = await _process.StandardOutput.ReadLineAsync(linkedCts.Token);
			if (string.IsNullOrWhiteSpace(obj))
			{
				throw new InvalidOperationException("OCR 进程没有返回结果。");
			}
			return obj;
		}
		catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw new TimeoutException($"OCR 请求超过 {_timeout.TotalSeconds:g} 秒未返回。");
		}
	}

	private void RestartProcess()
	{
		try
		{
			Process process = _process;
			if (process != null && !process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
			}
		}
		catch
		{
		}
		finally
		{
			_process?.Dispose();
			_process = null;
		}
	}

	private static bool IsStaleTempFileError(string error)
	{
		if (error.Contains("hsr-clean-ocr-", StringComparison.OrdinalIgnoreCase))
		{
			return error.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private void EnsureProcess()
	{
		Process process = _process;
		if (process == null || process.HasExited)
		{
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = _executablePath,
				Arguments = ((_bridgeScript == null) ? "--server" : ("\"" + _bridgeScript + "\" --server")),
				UseShellExecute = false,
				RedirectStandardInput = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
				StandardInputEncoding = Encoding.UTF8,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};
			_process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 OCR Python 进程。");
		}
	}

	private static void SavePng(BitmapSource image, string path)
	{
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(image));
		using FileStream stream = File.Create(path);
		encoder.Save(stream);
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}
}
