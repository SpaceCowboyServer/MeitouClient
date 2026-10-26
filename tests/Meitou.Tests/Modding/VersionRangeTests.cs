using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class VersionRangeTests
{
    [Theory]
    [InlineData(">=1.2 <2", "1.2.0", true)]
    [InlineData(">=1.2 <2", "1.9.9", true)]
    [InlineData(">=1.2 <2", "2.0.0", false)]
    [InlineData(">=1.2 <2", "1.1.9", false)]
    [InlineData(">=1.2, <2", "1.5.0", true)]
    [InlineData("<2", "1.9.9", true)]
    [InlineData("<2", "2.0.0", false)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("*", "0.0.0", true)]
    [InlineData("", "9.9.9", true)]
    public void Ranges_are_conjunctions_of_comparators(string text, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(text, out var range));
        Assert.Equal(expected, range.IsSatisfiedBy(SemanticVersion.Parse(version)));
    }

    [Fact]
    public void Prerelease_comparators_work()
    {
        Assert.True(VersionRange.TryParse(">=1.0.0-alpha <1.0.0", out var range));
        Assert.True(range.IsSatisfiedBy(SemanticVersion.Parse("1.0.0-alpha")));
        Assert.True(range.IsSatisfiedBy(SemanticVersion.Parse("1.0.0-beta")));
        Assert.False(range.IsSatisfiedBy(SemanticVersion.Parse("1.0.0")));
    }

    [Theory]
    [InlineData("^1.2")]
    [InlineData("~1.2")]
    [InlineData("1.2 || 2.0")]
    [InlineData(">=")]
    [InlineData("!=1.2.3")]
    [InlineData("* >=1.0.0")]
    public void Rejects_unsupported_range_syntax(string text) =>
        Assert.False(VersionRange.TryParse(text, out _));

    [Fact]
    public void ToString_uses_normalized_comparators()
    {
        Assert.True(VersionRange.TryParse(">=1.2, <2", out var range));
        Assert.Equal(">=1.2.0 <2.0.0", range.ToString());
        Assert.Equal("*", VersionRange.Any.ToString());
    }
}
