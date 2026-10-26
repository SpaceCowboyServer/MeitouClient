using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class ModLockFileTests
{
    static ModResolution Resolution()
    {
        var data = ModFixtures.Manifest("frontier.revolver", "1.2.0");
        var plugin = ModFixtures.Plugin("frontier.bounties", "1.3.0");
        var script = ModFixtures.Script("frontier.bounty.dust_bandit");
        script.Depends["frontier.revolver"] = ">=1 <2";
        script.Depends["frontier.bounties"] = ">=1 <2";
        return ModResolver.Resolve(
            [ModFixtures.Candidate("Revolver", data), ModFixtures.Candidate("Bounties", plugin), ModFixtures.Candidate("DustBandit", script)],
            ["frontier.revolver", "frontier.bounties", "frontier.bounty.dust_bandit"],
            new ModResolveOptions { GameVersion = SemanticVersion.Parse("0.16.5") });
    }

    [Fact]
    public void Writes_resolved_order_versions_trust_and_manifest_hashes()
    {
        var resolution = Resolution();
        Assert.True(resolution.Success, string.Join(" | ", resolution.Errors.Select(issue => issue.Message)));

        var lockFile = ModLockFile.FromResolution(resolution);
        string json = lockFile.ToJson();

        Assert.Equal(ModLockFile.CurrentFormat, lockFile.Format);
        Assert.Equal("0.16.5", lockFile.GameVersion);
        Assert.Equal(["frontier.revolver", "frontier.bounties", "frontier.bounty.dust_bandit"], lockFile.Mods.Select(entry => entry.Id));
        Assert.Contains("\"trust\": \"plugin\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trust\": \"script\"", json, StringComparison.Ordinal);
        Assert.Contains("\"manifestSha256\": \"aaaaaaaa", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Round_trips_through_disk()
    {
        using var folder = new ModTempFolder();
        string path = System.IO.Path.Combine(folder.Path, ModLockFile.FileName);
        var lockFile = ModLockFile.FromResolution(Resolution());

        lockFile.Write(path);
        var read = ModLockFile.Read(path);

        Assert.NotNull(read);
        Assert.Equal(lockFile.Format, read.Format);
        Assert.Equal(lockFile.ApiVersion, read.ApiVersion);
        Assert.Equal(lockFile.GameVersion, read.GameVersion);
        Assert.Equal(lockFile.Mods.Select(entry => entry.Id), read.Mods.Select(entry => entry.Id));
        Assert.Equal(ModTrust.Plugin, read.Mods[1].Trust);
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        using var folder = new ModTempFolder();
        Assert.Null(ModLockFile.Read(System.IO.Path.Combine(folder.Path, ModLockFile.FileName)));
    }

    [Fact]
    public void Newer_format_returns_null()
    {
        using var folder = new ModTempFolder();
        string path = folder.Write(ModLockFile.FileName, $$"""
            { "format": {{ModLockFile.CurrentFormat + 1}}, "apiVersion": 1, "mods": [] }
            """);

        Assert.Null(ModLockFile.Read(path));
    }

    [Fact]
    public void Invalid_entry_returns_null()
    {
        using var folder = new ModTempFolder();
        string path = folder.Write(ModLockFile.FileName, """
            {
              "format": 1,
              "apiVersion": 1,
              "mods": [{ "id": "bad", "version": "not-a-version", "folder": "Bad", "trust": "data", "manifestSha256": "" }]
            }
            """);

        Assert.Null(ModLockFile.Read(path));
    }
}
