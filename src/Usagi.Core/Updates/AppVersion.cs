using System.Globalization;

namespace Usagi.Core.Updates;

/// <summary>
/// A release version as tagged on GitHub ("v1.2.3", "1.2", "v1.3.0-beta.1"), compared the
/// semantic-versioning way: by number, and a pre-release ("-beta.1") before its release.
/// Build metadata ("+abc123", which .NET appends to the informational version) is ignored.
/// </summary>
public sealed record AppVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<AppVersion>
{
    public static bool TryParse(string? text, out AppVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
            value = value[1..];

        var plus = value.IndexOf('+');
        if (plus >= 0)
            value = value[..plus];

        string? preRelease = null;
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            preRelease = value[(dash + 1)..];
            value = value[..dash];
            if (preRelease.Length == 0)
                return false;
        }

        var parts = value.Split('.');
        if (parts.Length is < 2 or > 3)
            return false;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                return false;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], preRelease);
        return true;
    }

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
            return 1;

        var byNumber = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (byNumber != 0)
            return byNumber;

        // A release is newer than any of its pre-releases.
        return (PreRelease, other.PreRelease) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => ComparePreRelease(PreRelease, other.PreRelease)
        };
    }

    // Dot-separated identifiers, left to right: numeric ones by value and before alphanumeric
    // ones, the rest ordinally; a shorter list that matches so far comes first (semver 11.4).
    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.');
        var b = right.Split('.');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var aIsNumber = int.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out var aNumber);
            var bIsNumber = int.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out var bNumber);
            var result = (aIsNumber, bIsNumber) switch
            {
                (true, true) => aNumber.CompareTo(bNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a[i], b[i])
            };
            if (result != 0)
                return Math.Sign(result);
        }

        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() =>
        PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}
