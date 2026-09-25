namespace ADOFAI.ModManager.Core.Models.Transfer;

/// <summary>下载进度通知。</summary>
public sealed class DownloadProgress
{
    public long BytesReceived { get; init; }

    /// <summary>服务器未提供总长度时为 null。</summary>
    public long? TotalBytes { get; init; }

    /// <summary>0..100；无法确定总长度时为 null。</summary>
    public double? Percent => TotalBytes is > 0 ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value) : null;

    public double SpeedBytesPerSecond { get; init; }
}

/// <summary>操作结果（供 ViewModel 呈现 InfoBar / 对话框）。</summary>
public sealed class OperationResult
{
    public ResultStatus Status { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Warning 时的附加载荷（如浏览器下载兜底 URL）。</summary>
    public string? Payload { get; init; }

    public Exception? Exception { get; init; }

    public static OperationResult Ok(string message = "操作成功") => new() { Status = ResultStatus.Ok, Message = message };

    public static OperationResult Warn(string message, string? payload = null) => new() { Status = ResultStatus.Warning, Message = message, Payload = payload };

    public static OperationResult Error(string message, Exception? ex = null) => new() { Status = ResultStatus.Error, Message = message, Exception = ex };
}
