using ADOFAI.ModManager.Core.Helpers;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class PathGuardTests
{
    [Theory]
    [InlineData("Info.json", "Info.json")]
    [InlineData("Foo/Bar/x.dll", "Foo/Bar/x.dll")]
    [InlineData("Foo\\Bar\\x.dll", "Foo/Bar/x.dll")]
    [InlineData("/abs/path.txt", "abs/path.txt")]
    public void Safe_Paths_Normalized(string input, string expected)
    {
        Assert.Equal(expected, PathGuard.GetSafeRelativePath(input));
    }

    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("Foo/../../evil.dll")]
    [InlineData("C:/Windows/system32/evil.dll")]
    [InlineData("/..")]
    [InlineData("..\\evil")]
    public void Unsafe_Paths_Throw(string input)
    {
        Assert.Throws<UnsafeArchiveEntryException>(() => PathGuard.GetSafeRelativePath(input));
    }

    [Fact]
    public void Empty_Throws()
    {
        Assert.Throws<UnsafeArchiveEntryException>(() => PathGuard.GetSafeRelativePath(""));
    }
}

public class ZipNestingTests
{
    private static readonly string[] FlatEntries = ["Info.json", "AdofaiTweaks.dll", "Assets/x.png"];
    private static readonly string[] SingleWrappedEntries = ["AdofaiTweaks/Info.json", "AdofaiTweaks/AdofaiTweaks.dll"];
    private static readonly string[] DoubleWrappedEntries = ["Outer/Inner/Info.json", "Outer/Inner/x.dll"];
    private static readonly string[] MixedRootEntries = ["Info.json", "libs/x.dll"];
    private static readonly string[] WrappedEntries = ["OnlyMod/Info.json"];
    private static readonly string[] BareEntry = ["Info.json"];

    [Fact]
    public void FlatZip_NoStrip()
    {
        Assert.Equal(0, ZipNesting.ComputeStripDepth(FlatEntries));
    }

    [Fact]
    public void SingleWrap_StripOne()
    {
        Assert.Equal(1, ZipNesting.ComputeStripDepth(SingleWrappedEntries));
    }

    [Fact]
    public void DoubleWrap_StripTwo()
    {
        Assert.Equal(2, ZipNesting.ComputeStripDepth(DoubleWrappedEntries));
    }

    [Fact]
    public void MixedRoots_NoStrip()
    {
        Assert.Equal(0, ZipNesting.ComputeStripDepth(MixedRootEntries));
    }

    [Fact]
    public void SingleFile_StripsWrap()
    {
        Assert.Equal(1, ZipNesting.ComputeStripDepth(WrappedEntries));
        Assert.Equal(0, ZipNesting.ComputeStripDepth(BareEntry));
    }

    [Fact]
    public void Empty_NoStrip()
    {
        Assert.Equal(0, ZipNesting.ComputeStripDepth(Array.Empty<string>()));
    }
}
