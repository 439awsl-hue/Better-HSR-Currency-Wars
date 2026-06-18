using HsrCurrencyWarsCleanWpf.Core;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HsrCurrencyWarsCleanWpf.Services;

public sealed class ExternalRapidOcrService : IOcrService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _executablePath;
    private readonly string? _bridgeScript;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _process;

    public ExternalRapidOcrService(string executablePath, string? bridgeScript = null, TimeSpan? timeout = null)
    {
        _executablePath = executablePath;
        _bridgeScript = bridgeScript;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        Name = "RapidOCR 常驻进程";
    }

    public string Name { get; }

    public async Task<OcrScanResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        await _requestLock.WaitAsync(cancellationToken);
        var tempPath = Path.Combine(Path.GetTempPath(), $"hsr-clean-ocr-{Guid.NewGuid():N}.png");
        var shouldRestartProcess = false;
        try
        {
            SavePng(image, tempPath);
            var request = JsonSerializer.Serialize(new { image_path = tempPath });
            var responseLine = await SendRequestAsync(request, cancellationToken);
            var response = JsonSerializer.Deserialize<OcrBridgeResponse>(responseLine, JsonOptions);
            if (response is null || !string.IsNullOrWhiteSpace(response.Error))
            {
                var error = response?.Error ?? "OCR 返回为空。";
                if (IsStaleTempFileError(error))
                {
                    shouldRestartProcess = true;
                }

                throw new InvalidOperationException(error);
            }

            var items = response.Items
                .Select(item => new OcrTextItem(
                    item.Text ?? "",
                    new Rect(item.Bounds.X, item.Bounds.Y, item.Bounds.Width, item.Bounds.Height),
                    item.Confidence))
                .Where(item => !string.IsNullOrWhiteSpace(item.Text))
                .ToList();

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
        if (_process is null)
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
        if (_process?.StandardInput is null || _process.StandardOutput is null)
        {
            throw new InvalidOperationException("OCR 进程没有正确启动。");
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), linkedCts.Token);
            await _process.StandardInput.FlushAsync(linkedCts.Token);

            var line = await _process.StandardOutput.ReadLineAsync(linkedCts.Token);
            if (string.IsNullOrWhiteSpace(line))
            {
                throw new InvalidOperationException("OCR 进程没有返回结果。");
            }

            return line;
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
            if (_process is { HasExited: false })
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
        return error.Contains("hsr-clean-ocr-", StringComparison.OrdinalIgnoreCase) &&
            error.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }


    private void EnsureProcess()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            Arguments = _bridgeScript is null ? "--server" : $"\"{_bridgeScript}\" --server",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 OCR Python 进程。");
    }

    private static void SavePng(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
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

    private sealed class OcrBridgeResponse
    {
        [JsonPropertyName("raw_text")]
        public string? RawText { get; set; }

        [JsonPropertyName("items")]
        public List<OcrBridgeItem> Items { get; set; } = [];

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
        public OcrBridgeBounds Bounds { get; set; } = new();
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
}
