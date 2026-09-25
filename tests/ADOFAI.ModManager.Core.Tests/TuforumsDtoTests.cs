using System.Text.Json;
using ADOFAI.ModManager.Core.Helpers;
using ADOFAI.ModManager.Core.Models.Tuforums;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class TuforumsDtoTests
{
    private static string Fixture(string name)
    {
        var root = AppContext.BaseDirectory;
        var path = Path.Combine(root, "Fixtures", "Tuforums", name);
        return File.ReadAllText(path);
    }

    [Fact]
    public void ModsPage_Deserializes_RealShape()
    {
        var page = JsonSerializer.Deserialize<TufModsPage>(Fixture("mods_page.json"), JsonOptions.Web);

        Assert.NotNull(page);
        Assert.Equal(156, page!.Total);
        Assert.Equal(2, page.Mods!.Count);
        Assert.True(page.HasMore);

        var tuf = page.Mods[0];
        Assert.Equal("tufhelper", tuf.Slug);
        Assert.Equal("TUFHelper", tuf.Name);
        Assert.Equal(210, tuf.DownloadCount);
        Assert.Equal(2, tuf.Tags!.Count);
        Assert.Equal("#ff7070", tuf.Tags[0].Color);
        Assert.NotNull(tuf.PostedBy);

        tuf.Normalize();
        Assert.Equal("3.1.1", tuf.EffectiveVersion);
    }

    [Fact]
    public void ModsPage_Tolerates_NullFields()
    {
        var page = JsonSerializer.Deserialize<TufModsPage>(Fixture("mods_page.json"), JsonOptions.Web);
        var jipper = page!.Mods![1];

        Assert.Null(jipper.ImageUrl);
        Assert.Null(jipper.ProjectUrl);
        Assert.Null(jipper.PostedBy);
        Assert.Null(jipper.DeprecatedAfter);

        jipper.Normalize();
        Assert.NotNull(jipper.Description);
        Assert.Equal("1.7.2", jipper.EffectiveVersion);
        // postedBy 为 null 不影响后续使用
        Assert.Null(jipper.PostedBy);
    }

    [Fact]
    public void ModDetail_Deserializes_VersionsHistory()
    {
        var dto = JsonSerializer.Deserialize<DetailDto>(Fixture("mod_detail.json"), JsonOptions.Web);

        var mod = dto!.Mod!;
        Assert.Equal(2, mod.Versions!.Count);
        Assert.NotNull(mod.LatestVersion);
        Assert.Equal("3.1.1", mod.LatestVersion!.Version);
        Assert.Equal("github", mod.Versions[0].Source);
        Assert.Equal(0, mod.Versions[0].ReleasedAt!.Value.Offset.Hours);
    }

    [Fact]
    public void Missing_Collections_FallBackToEmpty()
    {
        const string json = """{"total":1,"mods":[{"id":1,"slug":"x"}],"hasMore":false}""";
        var page = JsonSerializer.Deserialize<TufModsPage>(json, JsonOptions.Web);
        var mod = page!.Mods![0];
        mod.Normalize();

        Assert.Empty(mod.Tags!);
        Assert.Empty(mod.Assignees!);
        Assert.Empty(mod.Versions!);
        Assert.Equal("x", mod.Name); // Name 缺失 → 回退 slug
    }

    public sealed class DetailDto
    {
        public TufMod? Mod { get; set; }
    }
}
