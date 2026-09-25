namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>带指数退避的简单重试助手（网络/IO 抖动恢复）。</summary>
public static class Retry
{
    /// <summary>
    /// 执行 action，最多 attempts 次。仅重试可重试异常（HttpRequestException、IOException、SocketException、WebException）。
    /// 用户取消（OperationCanceledException）绝不吞掉。
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int attempts = 3,
        Func<int, TimeSpan>? backoff = null,
        Action<int, Exception>? onRetry = null,
        CancellationToken ct = default)
    {
        backoff ??= attempt => TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < attempts)
            {
                onRetry?.Invoke(attempt, ex);
                await Task.Delay(backoff(attempt), ct).ConfigureAwait(false);
            }
        }
    }

    public static bool IsRetryable(Exception ex) =>
        // SocketException/WebException 均派生或包装自 HttpRequestException/IOException 体系
        ex is HttpRequestException or IOException;

    /// <summary>执行无返回值版本。</summary>
    public static Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        int attempts = 3,
        Func<int, TimeSpan>? backoff = null,
        Action<int, Exception>? onRetry = null,
        CancellationToken ct = default)
        => ExecuteAsync<object?>(
            async token =>
            {
                await action(token).ConfigureAwait(false);
                return null;
            },
            attempts, backoff, onRetry, ct);
}
