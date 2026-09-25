using ADOFAI.ModManager.Core.Models;
using ADOFAI.ModManager.Core.Services;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class ConfigPreservationTests : IDisposable
{
    private readonly string _root = TestHost.CreateTempDir();
    private readonly string _oldDir;
    private readonly string _newDir;

    public ConfigPreservationTests()
    {
        _oldDir = Path.Combine(_root, "old");
        _newDir = Path.Combine(_root, "new");
        Directory.CreateDirectory(_oldDir);
        Directory.CreateDirectory(_newDir);
    }

    [Fact]
    public void Preserves_UserConfig_MissingInNewPackage()
    {
        File.WriteAllText(Path.Combine(_oldDir, "Settings.xml"), "<settings/>");
        File.WriteAllText(Path.Combine(_oldDir, "config.json"), "{}");
        File.WriteAllText(Path.Combine(_oldDir, "Info.json"), "{\"Id\":\"X\"}");

        var preserved = ConfigPreservation.PreserveConfigs(_oldDir, _newDir);

        Assert.Contains("Settings.xml", preserved);
        Assert.Contains("config.json", preserved);
        Assert.DoesNotContain("Info.json", preserved);
        Assert.True(File.Exists(Path.Combine(_newDir, "Settings.xml")));
        Assert.False(File.Exists(Path.Combine(_newDir, "Info.json")));
    }

    [Fact]
    public void DoesNotOverwrite_FilesPresentInNewPackage()
    {
        File.WriteAllText(Path.Combine(_oldDir, "config.json"), "{\"old\":true}");
        File.WriteAllText(Path.Combine(_newDir, "config.json"), "{\"new\":true}");

        var preserved = ConfigPreservation.PreserveConfigs(_oldDir, _newDir);

        Assert.DoesNotContain("config.json", preserved);
        Assert.Equal("{\"new\":true}", File.ReadAllText(Path.Combine(_newDir, "config.json")));
    }

    [Fact]
    public void Preserves_ConfigDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_oldDir, "config"));
        File.WriteAllText(Path.Combine(_oldDir, "config", "keys.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_oldDir, "gameplay"));
        File.WriteAllText(Path.Combine(_oldDir, "gameplay", "data.bin"), "xx");

        var preserved = ConfigPreservation.PreserveConfigs(_oldDir, _newDir);

        Assert.Contains("config/keys.json", preserved);
        Assert.DoesNotContain("gameplay/data.bin", preserved);
        Assert.True(File.Exists(Path.Combine(_newDir, "config", "keys.json")));
    }

    [Theory]
    [InlineData("Settings.xml", true)]
    [InlineData("settings.ini", true)]
    [InlineData("weird.cfg", true)]
    [InlineData("notes.txt", false)]
    [InlineData("sub/file.json", false)]
    [InlineData("config/anything.txt", true)]
    [InlineData("Info.json", false)]
    [InlineData("info.json", false)]
    public void IsConfigPath_Classification(string path, bool expected)
    {
        Assert.Equal(expected, ConfigPreservation.IsConfigPath(path));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        GC.SuppressFinalize(this);
    }
}
