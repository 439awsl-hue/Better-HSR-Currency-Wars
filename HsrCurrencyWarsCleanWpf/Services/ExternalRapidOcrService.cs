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

	private DateTime _processStartedAtUtc;

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
		bool shouldRestartProcess = false;
		try
		{
			byte[] imageBytes = EncodePng(image);
			OcrBridgeResponse response = JsonSerializer.Deserialize<OcrBridgeResponse>(await SendRequestAsync(imageBytes, cancellationToken), JsonOptions);
			if (response == null || !string.IsNullOrWhiteSpace(response.Error))
			{
				throw new InvalidOperationException(response?.Error ?? "OCR 返回为空。");
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
			_requestLock.Release();
		}
	}

	public void Dispose()
	{
		try
		{
			RestartProcess();
		}
		finally
		{
			_requestLock.Dispose();
		}
	}

	public bool IsMaintenanceRestartDue(TimeSpan interval)
	{
		try
		{
			Process? process = _process;
			return process != null
				&& !process.HasExited
				&& _processStartedAtUtc != default(DateTime)
				&& DateTime.UtcNow - _processStartedAtUtc >= interval;
		}
		catch
		{
			return false;
		}
	}

	public async Task<bool> RestartForMaintenanceIfDueAsync(TimeSpan interval, CancellationToken cancellationToken)
	{
		await _requestLock.WaitAsync(cancellationToken);
		try
		{
			if (!IsMaintenanceRestartDue(interval))
			{
				return false;
			}
			RestartProcess();
			return true;
		}
		finally
		{
			_requestLock.Release();
		}
	}

	private async Task<string> SendRequestAsync(byte[] imageBytes, CancellationToken cancellationToken)
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
			string header = JsonSerializer.Serialize(new
			{
				image_size = imageBytes.Length
			}) + "\n";
			byte[] headerBytes = Encoding.UTF8.GetBytes(header);
			Stream input = _process.StandardInput.BaseStream;
			await input.WriteAsync(headerBytes.AsMemory(), linkedCts.Token);
			await input.WriteAsync(imageBytes.AsMemory(), linkedCts.Token);
			await input.FlushAsync(linkedCts.Token);
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
				try
				{
					byte[] shutdownRequest = Encoding.UTF8.GetBytes("{\"command\":\"shutdown\"}\n");
					Stream input = process.StandardInput.BaseStream;
					input.Write(shutdownRequest, 0, shutdownRequest.Length);
					input.Flush();
				}
				catch
				{
				}
				if (!process.WaitForExit(3000))
				{
					process.Kill(entireProcessTree: true);
					process.WaitForExit(2000);
				}
			}
		}
		catch
		{
		}
		finally
		{
			_process?.Dispose();
			_process = null;
			_processStartedAtUtc = default(DateTime);
		}
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
				WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? AppContext.BaseDirectory,
				StandardInputEncoding = Encoding.UTF8,
				StandardOutputEncoding = Encoding.UTF8,
				StandardErrorEncoding = Encoding.UTF8
			};
			_process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 OCR Python 进程。");
			_processStartedAtUtc = DateTime.UtcNow;
			_ = DrainStandardErrorAsync(_process);
		}
	}

	private static async Task DrainStandardErrorAsync(Process process)
	{
		try
		{
			while (await process.StandardError.ReadLineAsync() != null)
			{
			}
		}
		catch
		{
		}
	}

	private static byte[] EncodePng(BitmapSource image)
	{
		PngBitmapEncoder encoder = new PngBitmapEncoder();
		encoder.Frames.Add(BitmapFrame.Create(image));
		using MemoryStream stream = new MemoryStream();
		encoder.Save(stream);
		return stream.ToArray();
	}
}
