using System.Diagnostics;

namespace ADOFAI.ModManager.Core.Services;

/// <summary>检测 ADOFAI 是否正在运行（防止 DLL 文件占用导致安装/更新失败）。</summary>
public interface IGameProcessGuard
{
    bool IsGameRunning();
}

public sealed class GameProcessGuard : IGameProcessGuard
{
    public const string DefaultProcessName = SteamLocatorService.GameProcessName;

    public bool IsGameRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(DefaultProcessName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception)
        {
            // 枚举失败（权限等）时按“未检测到”处理，由用户自行判断
            return false;
        }
    }
}
