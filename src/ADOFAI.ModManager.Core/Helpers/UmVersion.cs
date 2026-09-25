namespace ADOFAI.ModManager.Core.Helpers;

/// <summary>
/// 兼容 SemVer 与 Unity Mod Manager 四段版本号的版本类型。
/// 支持 "v1.2.3"、"1.2"、"1.2.3.4"、"1.2.3-beta.2" 等形式。
/// 无法解析时 TryParse 返回 false，调用方应把两个"未知版本"视为相等（避免误报更新）。
/// </summary>
public readonly struct UmVersion : IComparable<UmVersion>, IEquatable<UmVersion>
{
    private readonly int _m1;
    private readonly int _m2;
    private readonly int _m3;
    private readonly int _m4;
    private readonly string? _prerelease;

    public UmVersion(int m1, int m2 = 0, int m3 = 0, int m4 = 0, string? prerelease = null)
    {
        _m1 = m1;
        _m2 = m2;
        _m3 = m3;
        _m4 = m4;
        _prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease.Trim();
    }

    public static bool TryParse(string? text, out UmVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim();

        // 去掉常见前缀：v / V / Version
        if (s.Length > 1 && (s[0] is 'v' or 'V') && char.IsDigit(s[1]))
        {
            s = s[1..];
        }
        else if (s.StartsWith("Version", StringComparison.OrdinalIgnoreCase))
        {
            s = s["Version".Length..].TrimStart();
        }

        // 分离核心与预发布段（只按第一个 '-' 切分）
        string core = s;
        string? pre = null;
        var dash = s.IndexOf('-');
        if (dash >= 0)
        {
            core = s[..dash];
            pre = s[(dash + 1)..].Trim();
        }

        var parts = core.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        var nums = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var n))
            {
                return false;
            }

            nums[i] = n < 0 ? 0 : n;
        }

        version = new UmVersion(nums[0], nums[1], nums[2], nums[3], pre);
        return true;
    }

    public int CompareTo(UmVersion other)
    {
        var c = _m1.CompareTo(other._m1);
        if (c != 0) return c;
        c = _m2.CompareTo(other._m2);
        if (c != 0) return c;
        c = _m3.CompareTo(other._m3);
        if (c != 0) return c;
        c = _m4.CompareTo(other._m4);
        if (c != 0) return c;

        return ComparePrerelease(_prerelease, other._prerelease);
    }

    /// <summary>SemVer 规则：无预发布 &gt; 有预发布；预发布段按 '.' 逐段比较，数字段按数值、数字段 &lt; 字母段。</summary>
    private static int ComparePrerelease(string? a, string? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;   // 正式版更高
        if (b is null) return -1;

        var pa = a.Split('.');
        var pb = b.Split('.');

        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var xa = i < pa.Length ? pa[i] : null;
            var xb = i < pb.Length ? pb[i] : null;
            if (xa is null) return -1; // 前缀相同者更小
            if (xb is null) return 1;

            var na = int.TryParse(xa, out var ia);
            var nb = int.TryParse(xb, out var ib);
            if (na && nb)
            {
                var c = ia.CompareTo(ib);
                if (c != 0) return c;
            }
            else if (na != nb)
            {
                return na ? -1 : 1; // 纯数字段小于字母段
            }
            else
            {
                var c = string.CompareOrdinal(xa, xb);
                if (c != 0) return c;
            }
        }

        return 0;
    }

    public bool Equals(UmVersion other) => CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is UmVersion v && Equals(v);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_m1);
        hash.Add(_m2);
        hash.Add(_m3);
        hash.Add(_m4);
        hash.Add(_prerelease ?? string.Empty, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        // 输出时折叠尾部的 .0，让展示更接近作者写法（1.2.3.0 -> 1.2.3）
        var segs = new List<int> { _m1, _m2, _m3, _m4 };
        while (segs.Count > 2 && segs[^1] == 0)
        {
            segs.RemoveAt(segs.Count - 1);
        }

        var core = string.Join('.', segs);
        return _prerelease is null ? core : $"{core}-{_prerelease}";
    }

    public static bool operator <(UmVersion left, UmVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(UmVersion left, UmVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(UmVersion left, UmVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(UmVersion left, UmVersion right) => left.CompareTo(right) >= 0;

    public static bool operator ==(UmVersion left, UmVersion right) => left.Equals(right);

    public static bool operator !=(UmVersion left, UmVersion right) => !left.Equals(right);
}
