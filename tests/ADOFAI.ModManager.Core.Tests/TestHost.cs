using System.IO.Compression;
using ADOFAI.ModManager.Core.Models.App;
using ADOFAI.ModManager.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ADOFAI.ModManager.Core.Tests;

/// <summary>测试共享工具：内存设置服务、临时目录、测试 zip 构造。</summary>
public static class TestHost
{
    public static FakeSettingsService CreateSettings(string? gameRoot = null)
    {
        var settings = new FakeSettingsService();
        settings.State.Settings.GameRoot = gameRoot ?? string.Empty;
        return settings;
    }

    public static LocalModService CreateLocalService(ISettingsService settings)
        => new(settings, NullLogger<LocalModService>.Instance);

    public static ModInstallerService CreateInstaller(ILocalModService local)
        => new(local, NullLogger<ModInstallerService>.Instance);

    /// <summary>创建测试 zip。entryPath 使用 '/' 分隔；content 为文件文本内容。</summary>
    public static string CreateZip(string directory, params (string EntryPath, string Content)[] entries)
    {
        Directory.CreateDirectory(directory);
        var zipPath = Path.Combine(directory, "test-" + Guid.NewGuid().ToString("N")[..6] + ".zip");
        using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            var entry = archive.CreateEntry(entryPath.Replace('/', Path.DirectorySeparatorChar));
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream);
            writer.Write(content);
        }

        return zipPath;
    }

    public static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "adofai-modui-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(path);
        return path;
    }

    public sealed class FakeSettingsService : ISettingsService
    {
        public FakeSettingsService()
        {
            State = new AppState();
            DataDirectory = Path.Combine(Path.GetTempPath(), "adofai-modui-tests", "state-" + Guid.NewGuid().ToString("N")[..6]);
        }

        public AppState State { get; private set; }

        public string DataDirectory { get; }

        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
