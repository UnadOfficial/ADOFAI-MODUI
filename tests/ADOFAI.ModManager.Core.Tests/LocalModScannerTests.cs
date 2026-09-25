using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Services;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class LocalModScannerTests : IDisposable
{
    private readonly string _gameRoot = TestHost.CreateTempDir();
    private readonly TestHost.FakeSettingsService _settings;
    private readonly LocalModService _service;

    public LocalModScannerTests()
    {
        _settings = TestHost.CreateSettings(_gameRoot);
        _service = TestHost.CreateLocalService(_settings);
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Mods"));
    }

    private string ModsDir => Path.Combine(_gameRoot, "Mods");

    [Fact]
    public async Task Scan_ReadsStandardInfoJson()
    {
        WriteMod("AdofaiTweaks", """
            {
              "Id": "AdofaiTweaks",
              "DisplayName": "ADOFAI Tweaks",
              "Author": "PizzaLovers007",
              "Version": "2.9.3",
              "ManagerVersion": "0.22.14.0",
              "AssemblyName": "AdofaiTweaks.dll",
              "EntryMethod": "AdofaiTweaks.Startup.Load"
            }
            """);

        var snapshot = await _service.ScanAsync();

        var mod = Assert.Single(snapshot.Mods);
        Assert.Equal("AdofaiTweaks", mod.Info!.Id);
        Assert.Equal("ADOFAI Tweaks", mod.DisplayName);
        Assert.Equal("2.9.3", mod.VersionText);
        Assert.False(mod.Disabled);
        Assert.Equal(LocalModStatus.Unlisted, mod.Status); // 未与远端比对前
        Assert.True(UmVersion.TryParse("2.9.3", out var v));
        Assert.Equal(v, mod.LocalVersion);
    }

    [Fact]
    public async Task Scan_DetectsDisabledAndUnityModManagerDir()
    {
        WriteMod("ModA", """{"Id":"ModA","Version":"1.0.0"}""", disabled: true);
        Directory.CreateDirectory(Path.Combine(ModsDir, "UnityModManager"));
        File.WriteAllText(Path.Combine(ModsDir, "UnityModManager", "dummy.txt"), "x");

        var snapshot = await _service.ScanAsync();

        var mod = Assert.Single(snapshot.Mods);
        Assert.Equal("ModA", mod.Info!.Id);
        Assert.True(mod.Disabled);
        Assert.Equal("Info.json.disabled", mod.InfoFileName);
    }

    [Fact]
    public async Task Scan_MarksBrokenForMissingOrInvalidInfo()
    {
        Directory.CreateDirectory(Path.Combine(ModsDir, "NoInfo"));
        Directory.CreateDirectory(Path.Combine(ModsDir, "BadInfo"));
        File.WriteAllText(Path.Combine(ModsDir, "BadInfo", "Info.json"), "{ not valid json ");

        var snapshot = await _service.ScanAsync();

        Assert.Equal(2, snapshot.Mods.Count);
        Assert.All(snapshot.Mods, m => Assert.Equal(LocalModStatus.Broken, m.Status));
    }

    [Fact]
    public async Task Scan_ToleratesCommentsAndMissingFields()
    {
        WriteMod("ModC", """
            {
              // UMM 的 Info.json 常带注释
              "Id": "ModC",
              "Version": "1.0",
            }
            """);

        var snapshot = await _service.ScanAsync();
        var mod = Assert.Single(snapshot.Mods);
        Assert.Equal("ModC", mod.Info!.Id);
        Assert.Equal("ModC", mod.DisplayName); // DisplayName 缺失 → 回退 Id
    }

    [Fact]
    public async Task Toggle_SwitchesStateBothWays()
    {
        WriteMod("ModT", """{"Id":"ModT","Version":"1.0.0"}""");
        var snapshot = await _service.ScanAsync();
        var mod = Assert.Single(snapshot.Mods);
        Assert.False(mod.Disabled);

        var afterDisable = await _service.ToggleEnabledAsync(mod.DirectoryPath);
        Assert.True(Assert.Single(afterDisable.Mods).Disabled);
        Assert.False(File.Exists(Path.Combine(ModsDir, "ModT", "Info.json")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "ModT", "Info.json.disabled")));

        var afterEnable = await _service.ToggleEnabledAsync(Path.Combine(ModsDir, "ModT"));
        Assert.False(Assert.Single(afterEnable.Mods).Disabled);
        Assert.True(File.Exists(Path.Combine(ModsDir, "ModT", "Info.json")));
    }

    [Fact]
    public async Task Uninstall_MovesToTrash()
    {
        WriteMod("ModU", """{"Id":"ModU"}""");
        var snapshot = await _service.ScanAsync();
        var mod = Assert.Single(snapshot.Mods);

        var trash = await _service.UninstallAsync(mod.DirectoryPath);

        Assert.True(File.Exists(Path.Combine(trash, "Info.json")));
        Assert.False(Directory.Exists(mod.DirectoryPath));
    }

    private void WriteMod(string folder, string infoJson, bool disabled = false)
    {
        var dir = Directory.CreateDirectory(Path.Combine(ModsDir, folder));
        File.WriteAllText(Path.Combine(dir.FullName, disabled ? "Info.json.disabled" : "Info.json"), infoJson);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_gameRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }
}
