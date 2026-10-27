using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Modding;
using Meitou.Modding.Api;
using Meitou.Modding.Plugins;
using Meitou.Modding.Runtime;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;
using Meitou.Simulation.Dialogue;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

public class BartenderSampleTests
{
    [Fact]
    public void The_frontier_sample_adds_a_bartender_batch_and_accepts_through_the_dialogue_source()
    {
        string root = RepoRoot();
        var framework = SampleCandidate(root, "frontier.contracts", "FrontierContracts.dll");
        var content = SampleCandidate(root, "frontier.bounties", "FrontierBounties.dll");
        Assert.SkipWhen(framework is null || content is null, "The sample plugins have not been built.");

        var api = new ModApiRuntime();
        var plugins = ModPluginHost.Load(new ModResolution([framework!, content!], [], ModApi.CurrentVersion, null), new GameDatabase(), api);
        Assert.Equal(2, plugins.PluginCount);
        Assert.True(plugins.Services.TryGet(ModServiceKeys.Contracts, out var service));
        Assert.IsAssignableFrom<IContractFramework>(service);

        var factions = new List<FactionData>
        {
            new() { Id = "player", Name = "Player" },
            new() { Id = "200-gamedata.base", Name = "Dust Bandits", FundamentalType = FundamentalType.Bandit },
        };
        var relations = FactionRelations.Build(factions);
        relations.PlayerFaction = 0;
        var data = new PopulationData { Db = SyntheticTown.Database(), Sites = [], Factions = factions, PlayerFaction = 0, Relations = relations };
        var board = new BountyBoardSystem(data, new BountySettings
        {
            Contracts = api.Contracts,
            OfferRefreshTicks = 1,
            ViewRefreshTicks = 1,
            OfferSpecific = false,
            OfferAnimals = false,
            OfferSpawned = false,
        });
        using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 0));
        api.AttachBoard(board);
        api.AttachWorld(world);
        var situation = new DialogueSituation(CharacterId.None, CharacterId.None, "Barman", "barman-record", "hub", "The Hub", 1,
            IsTrader: true, IsBartender: true, AtRoot: true);

        Assert.Contains(api.Dialogue.Options(world, situation), option => option.Text == "Ask about work");
        Assert.True(api.Dialogue.Select(world, situation, "frontier.contracts:ask"));
        var giver = situation.GiverId;
        Assert.Equal(3, api.Board.OffersFor(giver).Count);

        var offer = api.Board.OffersFor(giver)[0];
        Assert.True(api.Dialogue.Select(world, situation, "frontier.contracts:slot-0"));
        Assert.Single(api.Board.ActiveFor(giver));
        Assert.Equal(offer.Id, api.Board.ActiveFor(giver)[0].Id);
        var journal = Assert.Single(api.Board.Journal);
        Assert.Equal(offer.Id, journal.Id);
        Assert.Equal("Barman", journal.GiverName);
        Assert.Equal(BountyStatus.Accepted, journal.Status);
        Assert.True(journal.ExpiresAtGameHours > journal.AcceptedAtGameHours);
        Assert.Contains(api.Dialogue.Options(world, situation), option => option.Text?.StartsWith("In progress:", StringComparison.Ordinal) == true);

        // An active slot stays on the next day; a finished slot is replaced individually.
        api.EventBus.Publish(new WorldDayChanged(2, 1, 48));
        Assert.Single(api.Board.ActiveFor(giver));
        Assert.Equal(2, api.Board.OffersFor(giver).Count);
        var finished = Assert.Single(api.Board.ActiveFor(giver));
        Assert.Equal(offer.Id, finished.Id);
        var contract = Assert.Single(board.Contracts);
        contract.Progress = contract.Required;
        contract.State = BountyState.ReadyToTurnIn;
        Assert.True(api.Board.TurnIn(contract.Id));
        api.EventBus.Publish(new WorldDayChanged(3, 2, 72));
        Assert.Empty(api.Board.ActiveFor(giver));
        Assert.Single(api.Board.CompletedFor(giver));
        Assert.Equal(3, api.Board.OffersFor(giver).Count);

        using var save = new ModTempFolder();
        plugins.WriteSidecars(save.Path, plugins.CaptureSidecars());
        var restoredApi = new ModApiRuntime();
        var restored = ModPluginHost.Load(new ModResolution([framework!, content!], [], ModApi.CurrentVersion, null), new GameDatabase(), restoredApi);
        restored.ReadSidecars(save.Path);
        Assert.Equal(3, restoredApi.Contracts.Definitions.Count);
    }

    static ModCandidate? SampleCandidate(string root, string folderName, string assemblyName)
    {
        string folder = Path.Combine(root, "sdk", "samples", folderName);
        if (!File.Exists(Path.Combine(folder, "lib", assemblyName))) return null;
        var read = ModManifest.Read(Path.Combine(folder, ModManifest.FileName));
        var manifest = Assert.IsType<ModManifest>(read.Manifest);
        return new ModCandidate(manifest.Id, folder, manifest, read.ManifestSha256 ?? "");
    }

    static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "MeitouClient.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found above the test output.");
    }
}
