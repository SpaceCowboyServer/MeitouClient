namespace Meitou.Modding;

/// <summary>
/// A conjunction of version comparators, for example <c>&gt;=1.2 &lt;2</c>. An empty range or <c>*</c> accepts any version.
/// Numeric parts missing from a comparator are zero, so <c>&lt;2</c> means <c>&lt;2.0.0</c>. Alternatives (<c>||</c>), caret and tilde
/// ranges are not part of the manifest language.
/// </summary>
public sealed class VersionRange
{
    public static VersionRange Any { get; } = new([]);

    readonly VersionComparator[] comparators;

    VersionRange(VersionComparator[] comparators) => this.comparators = comparators;

    public IReadOnlyList<VersionComparator> Comparators => comparators;

    public static VersionRange Parse(string text) =>
        TryParse(text, out var range) ? range : throw new FormatException($"'{text}' is not a version range.");

    public static bool TryParse(string? text, out VersionRange range)
    {
        range = Any;
        if (string.IsNullOrWhiteSpace(text)) return true;
        string trimmed = text.Trim();
        if (trimmed == "*") return true;

        string[] tokens = trimmed.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return true;

        var comparators = new List<VersionComparator>(tokens.Length);
        foreach (string token in tokens)
        {
            var op = VersionOperator.Equal;
            string versionText = token;
            if (token.StartsWith(">=", StringComparison.Ordinal)) { op = VersionOperator.GreaterOrEqual; versionText = token[2..]; }
            else if (token.StartsWith("<=", StringComparison.Ordinal)) { op = VersionOperator.LessOrEqual; versionText = token[2..]; }
            else if (token.StartsWith('>')) { op = VersionOperator.Greater; versionText = token[1..]; }
            else if (token.StartsWith('<')) { op = VersionOperator.Less; versionText = token[1..]; }
            else if (token.StartsWith('=')) { versionText = token[1..]; }

            if (!SemanticVersion.TryParsePartial(versionText, out var version)) return false;
            comparators.Add(new VersionComparator(op, version));
        }

        range = new VersionRange([.. comparators]);
        return true;
    }

    public bool IsSatisfiedBy(SemanticVersion version) => comparators.All(comparator => comparator.Operator switch
    {
        VersionOperator.Equal => version == comparator.Version,
        VersionOperator.Greater => version > comparator.Version,
        VersionOperator.GreaterOrEqual => version >= comparator.Version,
        VersionOperator.Less => version < comparator.Version,
        VersionOperator.LessOrEqual => version <= comparator.Version,
        _ => false,
    });

    public override string ToString() => comparators.Length == 0 ? "*" : string.Join(" ", comparators.Select(c => c.ToString()));
}

public enum VersionOperator { Equal, Greater, GreaterOrEqual, Less, LessOrEqual }

public readonly record struct VersionComparator(VersionOperator Operator, SemanticVersion Version)
{
    public override string ToString() => Operator switch
    {
        VersionOperator.Equal => Version.ToString(),
        VersionOperator.Greater => ">" + Version,
        VersionOperator.GreaterOrEqual => ">=" + Version,
        VersionOperator.Less => "<" + Version,
        VersionOperator.LessOrEqual => "<=" + Version,
        _ => Version.ToString(),
    };
}
