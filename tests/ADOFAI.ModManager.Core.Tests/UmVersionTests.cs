using ADOFAI.ModManager.Core.Helpers;
using Xunit;

namespace ADOFAI.ModManager.Core.Tests;

public class UmVersionTests
{
    [Theory]
    [InlineData("1.2.3", true, "1.2.3")]
    [InlineData("v1.2.3", true, "1.2.3")]
    [InlineData("V2.0", true, "2.0")]
    [InlineData("1.2.3.4", true, "1.2.3.4")]
    [InlineData("0.22.14.0", true, "0.22.14")]
    [InlineData("1.2.3-beta.2", true, "1.2.3-beta.2")]
    [InlineData("7", true, "7.0")]
    [InlineData("", false, null)]
    [InlineData("beta", false, null)]
    [InlineData("1.2.3.4.5", false, null)]
    [InlineData("1.2.x", false, null)]
    public void TryParse_Basic(string input, bool expected, string? expectedText)
    {
        var ok = UmVersion.TryParse(input, out var v);
        Assert.Equal(expected, ok);
        if (expected)
        {
            Assert.Equal(expectedText, v.ToString());
        }
    }

    [Fact]
    public void Compare_SemanticOrdering()
    {
        Assert.True(P("1.2.3") < P("1.2.4"));
        Assert.True(P("1.2.3") < P("1.3.0"));
        Assert.True(P("2.0.0") > P("1.9.9.9"));
        Assert.True(P("1.2") == P("1.2.0"));        // 补零等价
        Assert.True(P("1.2") < P("1.2.0.1"));       // 四段扩展
    }

    [Fact]
    public void Compare_Prerelease()
    {
        Assert.True(P("1.0.0-alpha") < P("1.0.0"));          // 预发布低于正式版
        Assert.True(P("1.0.0-alpha") < P("1.0.0-beta"));
        Assert.True(P("1.0.0-alpha.2") > P("1.0.0-alpha.1"));
        Assert.True(P("1.0.0-alpha.2") < P("1.0.0-alpha.10")); // 数字段按数值比较
        Assert.True(P("1.0.0-1") < P("1.0.0-alpha"));          // 数字段 < 字母段
    }

    [Fact]
    public void Unparseable_TreatedAsEqualByCaller()
    {
        // 不可解析 → 调用方应视为相等（不误报更新）
        var a = UmVersion.TryParse("beta", out var va);
        var b = UmVersion.TryParse("beta", out var vb);
        Assert.False(a);
        Assert.False(b);
        Assert.True(va.Equals(vb));
    }

    private static UmVersion P(string s)
    {
        Assert.True(UmVersion.TryParse(s, out var v), $"无法解析 {s}");
        return v;
    }
}
