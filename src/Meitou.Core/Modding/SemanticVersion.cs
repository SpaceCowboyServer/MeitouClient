using System.Globalization;

namespace Meitou.Modding;

/// <summary>
/// A semantic version (semver 2.0.0). Build metadata is kept for display but ignored by precedence, as the specification says.
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    readonly string prerelease;
    readonly string build;

    public SemanticVersion(int major, int minor = 0, int patch = 0, string prerelease = "", string build = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        string pre = prerelease ?? "";
        string meta = build ?? "";
        if (!IsValidPrerelease(pre)) throw new ArgumentException("Invalid semantic-version prerelease.", nameof(prerelease));
        if (!IsValidBuild(meta)) throw new ArgumentException("Invalid semantic-version build metadata.", nameof(build));
        Major = major;
        Minor = minor;
        Patch = patch;
        this.prerelease = pre;
        this.build = meta;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string Prerelease => prerelease ?? "";
    public string Build => build ?? "";

    public bool IsPrerelease => Prerelease.Length > 0;

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException($"'{text}' is not a semantic version.");

    /// <summary>Parses <c>major.minor.patch</c> with optional prerelease/build metadata.</summary>
    public static bool TryParse(string? text, out SemanticVersion version) => TryParseCore(text, allowPartial: false, out version);

    /// <summary>
    /// Parses a partial version such as <c>1</c> or <c>1.2</c>, filling missing numeric parts with zero. Used by version ranges and
    /// capability declarations, where writing <c>&gt;=1.2</c> is convenient; a mod's own version still needs all three parts.
    /// </summary>
    public static bool TryParsePartial(string? text, out SemanticVersion version) => TryParseCore(text, allowPartial: true, out version);

    public int CompareTo(SemanticVersion other)
    {
        int compare = Major.CompareTo(other.Major);
        if (compare != 0) return compare;
        compare = Minor.CompareTo(other.Minor);
        if (compare != 0) return compare;
        compare = Patch.CompareTo(other.Patch);
        if (compare != 0) return compare;
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(SemanticVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Major);
        hash.Add(Minor);
        hash.Add(Patch);
        hash.Add(Prerelease, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        string text = $"{Major}.{Minor}.{Patch}";
        if (Prerelease.Length > 0) text += "-" + Prerelease;
        if (Build.Length > 0) text += "+" + Build;
        return text;
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
    public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
    public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

    static bool TryParseCore(string? text, bool allowPartial, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        string build = "";
        int plus = text.IndexOf('+');
        if (plus >= 0)
        {
            build = text[(plus + 1)..];
            text = text[..plus];
            if (build.Length == 0) return false;
        }

        string prerelease = "";
        int dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..];
            text = text[..dash];
            if (prerelease.Length == 0) return false;
        }

        string[] parts = text.Split('.');
        if (parts.Length is < 1 or > 3 || (!allowPartial && parts.Length != 3)) return false;

        int[] numbers = [0, 0, 0];
        for (int i = 0; i < parts.Length; i++)
            if (!TryParseNumber(parts[i], out numbers[i])) return false;

        if (!IsValidPrerelease(prerelease) || !IsValidBuild(build)) return false;
        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], prerelease, build);
        return true;
    }

    static bool TryParseNumber(string text, out int number)
    {
        number = 0;
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0')) return false;
        foreach (char c in text)
            if (!char.IsAsciiDigit(c)) return false;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    static bool IsValidPrerelease(string text)
    {
        if (text.Length == 0) return true;
        foreach (string identifier in text.Split('.'))
        {
            if (!IsValidIdentifier(identifier, allowLeadingZero: false)) return false;
            if (IsNumeric(identifier) && identifier.Length > 1 && identifier[0] == '0') return false;
        }
        return true;
    }

    static bool IsValidBuild(string text) =>
        text.Length == 0 || text.Split('.').All(identifier => IsValidIdentifier(identifier, allowLeadingZero: true));

    static bool IsValidIdentifier(string text, bool allowLeadingZero)
    {
        if (text.Length == 0) return false;
        foreach (char c in text)
            if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
        return allowLeadingZero || !IsNumeric(text) || text.Length == 1 || text[0] != '0';
    }

    static bool IsNumeric(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);

    static int ComparePrerelease(string left, string right)
    {
        if (left.Length == 0) return right.Length == 0 ? 0 : 1;
        if (right.Length == 0) return -1;

        string[] a = left.Split('.');
        string[] b = right.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            int compare = CompareIdentifier(a[i], b[i]);
            if (compare != 0) return compare;
        }
        return a.Length.CompareTo(b.Length);
    }

    static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = IsNumeric(left), rightNumeric = IsNumeric(right);
        if (leftNumeric && rightNumeric)
            return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }
}
