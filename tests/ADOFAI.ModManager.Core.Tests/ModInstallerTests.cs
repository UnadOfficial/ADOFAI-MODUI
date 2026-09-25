using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Services;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class ModInstallerTests : IDisposable
{
    private readonly string _gameRoot = TestHost.CreateTempDir();
    private readonly TestHost.FakeSettingsService _settings;
    private readonly LocalModService _local;
    private readonly ModInstallerService _installer;
    private readonly string _zips;

    public ModInstallerTests()
    {
        _settings = TestHost.CreateSettings(_gameRoot);
        _local = TestHost.CreateLocalService(_settings);
        _installer = TestHost.CreateInstaller(_local);
        _zips = TestHost.CreateTempDir();
        Directory.CreateDirectory(Path.Combine(_gameRoot, "Mods"));
    }

    private string ModsDir => Path.Combine(_gameRoot, "Mods");

    [Fact]
    public async Task Install_FlatZip_LandsAtModsRoot()
    {
        var zip = TestHost.CreateZip(_zips,
            ("Info.json", """{"Id":"FlatMod","Version":"1.0.0"}"""),
            ("FlatMod.dll", "dll"));

        var outcome = await _installer.InstallFromZipAsync(zip);

        Assert.Equal("FlatMod", outcome.ModId);
        Assert.True(File.Exists(Path.Combine(ModsDir, "FlatMod", "Info.json")));
        Assert.True(File.Exists(Path.Combine(ModsDir, "FlatMod", "FlatMod.dll")));
        Assert.False(Directory.Exists(Path.Combine(ModsDir, ".modui_staging"))); // staging 已清理
    }

    [Fact]
    public async Task Install_SingleWrappedZip_StripsWrapper()
    {
        var zip = TestHost.CreateZip(_zips,
            ("WrappedMod/Info.json", """{"Id":"WrappedMod","Version":"1.0.0"}"""),
            ("WrappedMod/WrappedMod.dll", "dll"));

        var outcome = await _installer.InstallFromZipAsync(zip);

        Assert.True(File.Exists(Path.Combine(ModsDir, "WrappedMod", "Info.json")));
        Assert.False(Directory.Exists(Path.Combine(ModsDir, "WrappedMod", "WrappedMod")));
    }

    [Fact]
    public async Task Install_DoubleWrappedZip_StripsBothLayers()
    {
        var zip = TestHost.CreateZip(_zips,
            ("Outer/Inner/Info.json", """{"Id":"Inner","Version":"1.0.0"}"""),
            ("Outer/Inner/lib.dll", "dll"));

        var outcome = await _installer.InstallFromZipAsync(zip);

        Assert.Equal("Inner", outcome.ModId);
        Assert.True(File.Exists(Path.Combine(ModsDir, "Inner", "Info.json")));
    }

    [Fact]
    public async Task Update_PreservesUserConfigs_AndReplacesDll()
    {
        // 第一次安装 1.0.0
        var v1 = TestHost.CreateZip(_zips,
            ("MyMod/Info.json", """{"Id":"MyMod","Version":"1.0.0"}"""),
            ("MyMod/MyMod.dll", "old-dll"));
        await _installer.InstallFromZipAsync(v1);

        // 用户修改了配置
        var settingsPath = Path.Combine(ModsDir, "MyMod", "Settings.xml");
        await File.WriteAllTextAsync(settingsPath, "<settings userEdited='1'/>");

        // 更新 2.0.0（新包不含 Settings.xml）
        var v2 = TestHost.CreateZip(_zips,
            ("MyMod/Info.json", """{"Id":"MyMod","Version":"2.0.0"}"""),
            ("MyMod/MyMod.dll", "new-dll"));
        var outcome = await _installer.InstallFromZipAsync(v2);

        Assert.Equal("2.0.0", outcome.Info.Version);
        Assert.Equal("new-dll", await File.ReadAllTextAsync(Path.Combine(ModsDir, "MyMod", "MyMod.dll")));
        Assert.Equal("<settings userEdited='1'/>", await File.ReadAllTextAsync(settingsPath));
        Assert.Contains("Settings.xml", outcome.PreservedFiles);
    }

    [Fact]
    public async Task Install_UpdatesExistingDirectory_InPlace()
    {
        var v1 = TestHost.CreateZip(_zips,
            ("Info.json", """{"Id":"SameMod","Version":"1.0.0"}"""),
            ("SameMod.dll", "a"));
        var first = await _installer.InstallFromZipAsync(v1);

        var v2 = TestHost.CreateZip(_zips,
            ("Info.json", """{"Id":"SameMod","Version":"1.1.0"}"""));
        var second = await _installer.InstallFromZipAsync(v2);

        Assert.Equal(first.ModDirectory, second.ModDirectory);
        var snapshot = await _local.ScanAsync();
        var mod = Assert.Single(snapshot.Mods);
        Assert.Equal("1.1.0", mod.VersionText);
    }

    [Fact]
    public async Task Install_ZipSlipEntry_ThrowsAndLeavesNoTarget()
    {
        // 构造包含 ../ 逃逸条目的 zip
        var zipPath = Path.Combine(_zips, "evil.zip");
        using (var stream = File.Create(zipPath))
        using (var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.dll");
            await using var es = entry.Open();
            await es.WriteAsync("x"u8.ToArray());
        }

        await Assert.ThrowsAnyAsync<Exception>(() => _installer.InstallFromZipAsync(zipPath));

        // 未产生目标目录，也没有把文件写到 Mods 之外
        Assert.False(Directory.Exists(Path.Combine(ModsDir, "escape.dll")));
    }

    [Fact]
    public async Task Install_MissingInfoJson_Throws()
    {
        var zip = TestHost.CreateZip(_zips, ("readme.txt", "no info here"));
        await Assert.ThrowsAsync<InvalidDataException>(() => _installer.InstallFromZipAsync(zip));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_gameRoot, recursive: true);
            Directory.Delete(_zips, recursive: true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }
}
