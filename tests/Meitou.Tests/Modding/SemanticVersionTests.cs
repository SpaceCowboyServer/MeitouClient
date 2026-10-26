using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class SemanticVersionTests
{
    [Fact]
    public void Default_is_a_safe_zero_version()
    {
        Assert.Equal("0.0.0", default(SemanticVersion).ToString());
        Assert.Equal(new SemanticVersion(0, 0, 0), default);
        Assert.False(default(SemanticVersion).IsPrerelease);
    }

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.9.0", "2.0.0")]
    public void Versions_order_by_semver_precedence(string lower, string higher)
    {
        Assert.True(SemanticVersion.Parse(lower) < SemanticVersion.Parse(higher));
        Assert.True(SemanticVersion.Parse(higher) > SemanticVersion.Parse(lower));
    }

    [Fact]
    public void Numeric_prerelease_identifiers_compare_as_numbers()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-2") < SemanticVersion.Parse("1.0.0-10"));
        Assert.True(SemanticVersion.Parse("1.0.0-2") < SemanticVersion.Parse("1.0.0-alpha"));
    }

    [Fact]
    public void Build_metadata_is_ignored_for_precedence_but_kept_for_display()
    {
        var one = SemanticVersion.Parse("1.2.3+one");
        var two = SemanticVersion.Parse("1.2.3+two");

        Assert.Equal(one, two);
        Assert.Equal(one.GetHashCode(), two.GetHashCode());
        Assert.Equal("1.2.3+one", one.ToString());
        Assert.Equal("1.2.3+two", two.ToString());
    }

    [Fact]
    public void Partial_versions_fill_missing_parts_with_zero()
    {
        Assert.True(SemanticVersion.TryParsePartial("1", out var major));
        Assert.Equal("1.0.0", major.ToString());
        Assert.True(SemanticVersion.TryParsePartial("1.2", out var minor));
        Assert.Equal("1.2.0", minor.ToString());
        Assert.False(SemanticVersion.TryParse("1.2", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.03")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-alpha..1")]
    [InlineData("1.2.3-alpha_1")]
    public void Rejects_invalid_semantic_versions(string text) =>
        Assert.False(SemanticVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.0-x.7.z.92")]
    [InlineData("1.0.0-rc.1+build-1")]
    [InlineData("1.0.0+20130313144700")]
    public void Accepts_valid_prerelease_and_build_identifiers(string text) =>
        Assert.True(SemanticVersion.TryParse(text, out _));
}
