using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class ModResolverTests
{
    static string[] Ids(ModResolution resolution) => [.. resolution.Order.Select(candidate => candidate.Id)];
    static bool Has(ModResolution resolution, string code) => resolution.Errors.Any(issue => issue.Code == code);

    [Fact]
    public void Resolves_dependencies_and_keeps_user_order_for_independents()
    {
        var revolver = ModFixtures.Manifest("frontier.revolver", "1.2.0");
        var bounties = ModFixtures.Plugin("frontier.bounties", "1.3.0");
        var content = ModFixtures.Script("frontier.bounty.dust_bandit");
        content.Depends["frontier.bounties"] = ">=1.2 <2";
        content.Depends["frontier.revolver"] = ">=1.0 <2";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("DustBandit", content), ModFixtures.Candidate("Bounties", bounties), ModFixtures.Candidate("Revolver", revolver)],
            ["frontier.bounty.dust_bandit", "frontier.bounties", "frontier.revolver"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(issue => issue.Message)));
        Assert.Equal(["frontier.bounties", "frontier.revolver", "frontier.bounty.dust_bandit"], Ids(result));
        Assert.Equal(Ids(result), Ids(ModResolver.Resolve(
            [ModFixtures.Candidate("DustBandit", content), ModFixtures.Candidate("Bounties", bounties), ModFixtures.Candidate("Revolver", revolver)],
            ["frontier.bounty.dust_bandit", "frontier.bounties", "frontier.revolver"])));
    }

    [Fact]
    public void Capability_version_satisfies_a_dependency_instead_of_the_provider_version()
    {
        var provider = ModFixtures.Manifest("frontier.bounties", "5.0.0");
        provider.Provides.Add("frontier.contracts@1");
        var consumer = ModFixtures.Manifest("frontier.contract.consumer");
        consumer.Depends["frontier.contracts"] = ">=1 <2";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Bounties", provider), ModFixtures.Candidate("Consumer", consumer)],
            ["frontier.bounties", "frontier.contract.consumer"]);

        Assert.True(result.Success, string.Join(" | ", result.Errors.Select(issue => issue.Message)));
        Assert.Equal(["frontier.bounties", "frontier.contract.consumer"], Ids(result));
    }

    [Fact]
    public void Capability_version_can_fail_a_dependency()
    {
        var provider = ModFixtures.Manifest("frontier.bounties", "5.0.0");
        provider.Provides.Add("frontier.contracts@1");
        var consumer = ModFixtures.Manifest("frontier.contract.consumer");
        consumer.Depends["frontier.contracts"] = ">=5";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Bounties", provider), ModFixtures.Candidate("Consumer", consumer)],
            ["frontier.bounties", "frontier.contract.consumer"]);

        Assert.True(Has(result, "dependency.version"));
    }

    [Fact]
    public void Missing_dependency_is_an_error()
    {
        var consumer = ModFixtures.Manifest("frontier.consumer");
        consumer.Depends["frontier.missing"] = ">=1";

        var result = ModResolver.Resolve([ModFixtures.Candidate("Consumer", consumer)], ["frontier.consumer"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "dependency.missing"));
    }

    [Fact]
    public void Disabled_dependency_is_an_error()
    {
        var provider = ModFixtures.Manifest("frontier.provider");
        var consumer = ModFixtures.Manifest("frontier.consumer");
        consumer.Depends["frontier.provider"] = ">=1";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Provider", provider), ModFixtures.Candidate("Consumer", consumer)],
            ["frontier.consumer"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "dependency.disabled"));
    }

    [Fact]
    public void Version_mismatch_is_an_error()
    {
        var provider = ModFixtures.Manifest("frontier.provider", "1.0.0");
        var consumer = ModFixtures.Manifest("frontier.consumer");
        consumer.Depends["frontier.provider"] = ">=2";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Provider", provider), ModFixtures.Candidate("Consumer", consumer)],
            ["frontier.provider", "frontier.consumer"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "dependency.version"));
    }

    [Fact]
    public void Optional_dependency_is_a_warning_when_disabled()
    {
        var provider = ModFixtures.Manifest("frontier.provider");
        var consumer = ModFixtures.Manifest("frontier.consumer");
        consumer.Optional["frontier.provider"] = ">=1";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Provider", provider), ModFixtures.Candidate("Consumer", consumer)],
            ["frontier.consumer"]);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, issue => issue.Code == "optional.disabled");
    }

    [Fact]
    public void Optional_dependency_orders_the_provider_when_enabled()
    {
        var provider = ModFixtures.Manifest("frontier.provider");
        var consumer = ModFixtures.Manifest("frontier.consumer");
        consumer.Optional["frontier.provider"] = ">=1";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Consumer", consumer), ModFixtures.Candidate("Provider", provider)],
            ["frontier.consumer", "frontier.provider"]);

        Assert.True(result.Success);
        Assert.Equal(["frontier.provider", "frontier.consumer"], Ids(result));
    }

    [Fact]
    public void Load_after_orders_a_soft_dependency()
    {
        var first = ModFixtures.Manifest("frontier.first");
        var second = ModFixtures.Manifest("frontier.second");
        second.LoadAfter.Add("frontier.first");

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("Second", second), ModFixtures.Candidate("First", first)],
            ["frontier.second", "frontier.first"]);

        Assert.True(result.Success);
        Assert.Equal(["frontier.first", "frontier.second"], Ids(result));
    }

    [Fact]
    public void Conflict_is_an_error()
    {
        var a = ModFixtures.Manifest("frontier.a");
        var b = ModFixtures.Manifest("frontier.b");
        b.Conflicts.Add("frontier.a");

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("A", a), ModFixtures.Candidate("B", b)],
            ["frontier.a", "frontier.b"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "conflict"));
    }

    [Fact]
    public void Duplicate_manifest_id_is_an_error()
    {
        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("First", ModFixtures.Manifest("frontier.same")), ModFixtures.Candidate("Second", ModFixtures.Manifest("frontier.same"))],
            ["frontier.same"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "manifest.duplicate_id"));
    }

    [Fact]
    public void Duplicate_capability_is_an_error()
    {
        var a = ModFixtures.Manifest("frontier.a");
        a.Provides.Add("frontier.shared@1");
        var b = ModFixtures.Manifest("frontier.b");
        b.Provides.Add("frontier.shared@1");

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("A", a), ModFixtures.Candidate("B", b)],
            ["frontier.a", "frontier.b"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "provide.duplicate"));
    }

    [Fact]
    public void Dependency_cycle_is_an_error_and_keeps_all_enabled_mods_for_display()
    {
        var a = ModFixtures.Manifest("frontier.a");
        a.Depends["frontier.b"] = "*";
        var b = ModFixtures.Manifest("frontier.b");
        b.Depends["frontier.a"] = "*";

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("A", a), ModFixtures.Candidate("B", b)],
            ["frontier.a", "frontier.b"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "dependency.cycle"));
        Assert.Equal(["frontier.a", "frontier.b"], Ids(result));
    }

    [Fact]
    public void Api_and_game_version_mismatches_are_errors()
    {
        var api = ModFixtures.Manifest("frontier.api");
        api.ApiVersion = ModApi.CurrentVersion + 1;
        var game = ModFixtures.Manifest("frontier.game");
        game.GameVersion = ">=0.17";

        var apiResult = ModResolver.Resolve([ModFixtures.Candidate("Api", api)], ["frontier.api"]);
        var gameResult = ModResolver.Resolve(
            [ModFixtures.Candidate("Game", game)],
            ["frontier.game"],
            new ModResolveOptions { GameVersion = SemanticVersion.Parse("0.16.5") });

        Assert.True(Has(apiResult, "api.mismatch"));
        Assert.True(Has(gameResult, "game.mismatch"));
    }

    [Fact]
    public void Unknown_enabled_id_is_an_error()
    {
        var result = ModResolver.Resolve([], ["frontier.missing"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "enabled.missing"));
    }

    [Fact]
    public void Self_dependency_is_an_error()
    {
        var manifest = ModFixtures.Manifest("frontier.self");
        manifest.Depends["frontier.self"] = "*";

        var result = ModResolver.Resolve([ModFixtures.Candidate("Self", manifest)], ["frontier.self"]);

        Assert.False(result.Success);
        Assert.True(Has(result, "dependency.self"));
    }

    [Fact]
    public void Duplicate_enabled_id_is_a_warning()
    {
        var manifest = ModFixtures.Manifest("frontier.one");

        var result = ModResolver.Resolve(
            [ModFixtures.Candidate("One", manifest)],
            ["frontier.one", "frontier.one"]);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings, issue => issue.Code == "enabled.duplicate");
    }
}
