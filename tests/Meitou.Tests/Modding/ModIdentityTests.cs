using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class ModIdentityTests
{
    [Theory]
    [InlineData("frontier.revolver")]
    [InlineData("a.b")]
    [InlineData("a.b_c-d")]
    [InlineData("Frontier.Revolver")]
    public void Accepts_namespaced_ids(string id) => Assert.True(ModId.IsValid(id));

    [Theory]
    [InlineData("")]
    [InlineData("frontier")]
    [InlineData(".frontier")]
    [InlineData("frontier.")]
    [InlineData("frontier..revolver")]
    [InlineData("frontier/revolver")]
    [InlineData("frontier revolver")]
    [InlineData("frontier:revolver")]
    public void Rejects_invalid_ids(string id) => Assert.False(ModId.IsValid(id));

    [Fact]
    public void Parses_capabilities_with_optional_versions()
    {
        Assert.True(ModCapability.TryParse("frontier.contracts@1", out var versioned));
        Assert.Equal("frontier.contracts", versioned.Id);
        Assert.Equal(SemanticVersion.Parse("1.0.0"), versioned.Version);
        Assert.Equal("frontier.contracts@1.0.0", versioned.ToString());

        Assert.True(ModCapability.TryParse("frontier.contracts", out var unversioned));
        Assert.Null(unversioned.Version);
        Assert.Equal("frontier.contracts", unversioned.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("@1")]
    [InlineData("frontier@")]
    [InlineData("frontier@x")]
    [InlineData("frontier")]
    public void Rejects_invalid_capabilities(string text) => Assert.False(ModCapability.TryParse(text, out _));
}

public class ModPathTests
{
    [Theory]
    [InlineData("content/")]
    [InlineData("assets/foo.png")]
    [InlineData("assets/ui/fonts")]
    [InlineData(@"assets\ui\fonts")]
    public void Accepts_paths_inside_the_mod_folder(string path) => Assert.True(ModPath.IsSafeRelative(path));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../x")]
    [InlineData("content/../x")]
    [InlineData("/absolute")]
    [InlineData("C:\\absolute")]
    [InlineData("content//x")]
    [InlineData("content/./x")]
    [InlineData("content/..")]
    [InlineData("foo:bar")]
    public void Rejects_paths_that_escape_or_are_ambiguous(string path) => Assert.False(ModPath.IsSafeRelative(path));
}
