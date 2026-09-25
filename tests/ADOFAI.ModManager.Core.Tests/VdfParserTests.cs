using ADOFAI.ModManager.Core.Helpers;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class VdfParserTests
{
    private const string SampleVdf = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"contentsid"		"-1"
        	}
        	"1"
        	{
        		"path"		"D:\\SteamLibrary"
        		"label"		""
        	}
        	// 注释应被忽略
        	"2"
        	{
        		"path"		"E:\\Games\\Steam"
        	}
        }
        """;

    [Fact]
    public void Parse_AndCollectPaths()
    {
        Assert.True(VdfParser.TryParse(SampleVdf, out var root));
        var paths = VdfParser.CollectStringValues(root, "path");

        Assert.Equal(3, paths.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam", paths);
        Assert.Contains(@"D:\SteamLibrary", paths);
        Assert.Contains(@"E:\Games\Steam", paths);
    }

    [Fact]
    public void Parse_UnquotedTokens()
    {
        const string vdf = "root { child value }";
        Assert.True(VdfParser.TryParse(vdf, out var root));
        var child = Assert.IsType<Dictionary<string, object>>(root["root"]);
        Assert.Equal("value", child["child"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void Parse_EmptyInput(string? input)
    {
        Assert.False(VdfParser.TryParse(input, out _));
    }

    [Fact]
    public void Parse_UnterminatedString_Fails()
    {
        Assert.False(VdfParser.TryParse("\"key value", out _));
    }
}
