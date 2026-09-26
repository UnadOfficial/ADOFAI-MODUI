using System;
using System.Threading.Tasks;
using ADOFAI.ModManager.Core.Services;
using Microsoft.UI.Dispatching;

namespace ADOFAI.ModManager.Services.Ui;

/// <summary>WinUI 3 线程调度器实现：利用 DispatcherQueue 安全将后台任务切回 UI 线程。</summary>
public sealed class WinUiDispatcherService : IDispatcherService
{
    private readonly DispatcherQueue _dispatcherQueue;

    public WinUiDispatcherService()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("WinUiDispatcherService 必须在 UI 线程上实例化。");
    }

    public void Enqueue(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }

    public Task EnqueueAsync(Func<Task> action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            return action();
        }

        var tcs = new TaskCompletionSource();
        _dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }
}
