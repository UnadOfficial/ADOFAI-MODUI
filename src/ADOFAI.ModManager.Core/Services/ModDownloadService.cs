using ADOFAI.ModManager.Core.Helpers;
using System.Diagnostics;
using ADOFAI.ModManager.Core.Models.Transfer;
using Microsoft.Extensions.Logging;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>下载请求。</summary>
public sealed record DownloadRequest(string Url, string DestinationFilePath, string DisplayName = "");

/// <summary>下载结果。</summary>
public sealed record DownloadResult(string FilePath, long TotalBytes);

/// <summary>
/// 后台下载服务：流式写入 .part 临时文件，支持 Range 断点续传、自动重试（指数退避）、
/// 进度通知（IProgress&lt;DownloadProgress&gt;）与取消（CancellationToken）。
/// </summary>
public interface IModDownloadService
{
    Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>删除未完成的 .part 文件。</summary>
    void CleanPartial(string destinationFilePath);
}

public sealed class ModDownloadService : IModDownloadService
{
    private const int BufferSize = 1024 * 256; // 256 KB
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(100);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGitHubMirrorService? _mirrorService;
    private readonly ILogger<ModDownloadService> _logger;

    public ModDownloadService(
        IHttpClientFactory httpClientFactory,
        IGitHubMirrorService mirrorService,
        ILogger<ModDownloadService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _mirrorService = mirrorService;
        _logger = logger;
    }

    public ModDownloadService(IHttpClientFactory httpClientFactory, ILogger<ModDownloadService> logger)
        : this(httpClientFactory, null!, logger)
    {
    }

    public async Task<DownloadResult> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var dest = request.DestinationFilePath;
        var part = dest + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        var attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await DownloadOnceAsync(request, part, dest, progress, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (Retry.IsRetryable(ex) && attempt < attempts)
            {
                _logger.LogWarning(ex, "下载失败（第 {Attempt} 次），稍后从断点重试：{Url}", attempt, request.Url);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<DownloadResult> DownloadOnceAsync(
        DownloadRequest request, string part, string dest,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        var client = _httpClientFactory.CreateClient("downloads");

        var effectiveUrl = _mirrorService is not null ? _mirrorService.ApplyMirror(request.Url) : request.Url;
        if (!string.Equals(effectiveUrl, request.Url, StringComparison.Ordinal))
        {
            _logger.LogInformation("应用 GitHub 镜像下载：{Url} → {MirrorUrl}", request.Url, effectiveUrl);
        }

        using var request2 = new HttpRequestMessage(HttpMethod.Get, effectiveUrl);
        if (existing > 0)
        {
            request2.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
        }

        using var response = await client.SendAsync(request2, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        // 服务器不支持 Range 时返回 200 → 从头下载
        if (response.StatusCode == System.Net.HttpStatusCode.OK && existing > 0)
        {
            existing = 0;
        }
        else if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
        {
            // .part 已完整（上次恰好下载完但未改名）
            File.Move(part, dest, overwrite: true);
            return new DownloadResult(dest, existing);
        }
        else
        {
            response.EnsureSuccessStatusCode();
        }

        long? total = response.Content.Headers.ContentLength is { } len ? existing + len : null;
        var received = existing;

        await using (var content = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            file.Seek(existing, SeekOrigin.Begin);

            var buffer = new byte[BufferSize];
            var lastReport = Stopwatch.GetTimestamp();
            var lastBytes = received;
            var speed = 0d;

            int read;
            while ((read = await content.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                received += read;

                var now = Stopwatch.GetTimestamp();
                if (now - lastReport >= ReportInterval.Ticks)
                {
                    var elapsed = Stopwatch.GetTimestamp() - lastReport;
                    var delta = received - lastBytes;
                    speed = delta / (elapsed / (double)Stopwatch.Frequency);
                    lastReport = now;
                    lastBytes = received;

                    progress?.Report(new DownloadProgress
                    {
                        BytesReceived = received,
                        TotalBytes = total,
                        SpeedBytesPerSecond = speed,
                    });
                }
            }

            await file.FlushAsync(ct).ConfigureAwait(false);
        }

        File.Move(part, dest, overwrite: true);

        progress?.Report(new DownloadProgress
        {
            BytesReceived = received,
            TotalBytes = total ?? received,
            SpeedBytesPerSecond = 0,
        });

        _logger.LogInformation("下载完成：{Url} → {File}（{Bytes} 字节）", request.Url, dest, received);
        return new DownloadResult(dest, total ?? received);
    }

    public void CleanPartial(string destinationFilePath)
    {
        var part = destinationFilePath + ".part";
        try
        {
            if (File.Exists(part))
            {
                File.Delete(part);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "清理 .part 文件失败：{File}", part);
        }
    }
}
