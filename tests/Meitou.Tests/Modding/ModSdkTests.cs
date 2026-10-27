using System.IO.Compression;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Modding.Api;
using Meitou.Modding.Plugins;
using Meitou.Modding.Runtime;
using Meitou.Modding.Scripting;
using Meitou.Sdk;
using Meitou.Tests.Simulation;

namespace Meitou.Tests.Modding;

public class ModSdkTests
{
    [Fact]
    public void Scaffold_validates_packs_installs_and_indexes_an_no_asset_mod()
    {
        using var root = new ModTempFolder();
        string source = System.IO.Path.Combine(root.Path, "Hello");
        string created = ModScaffold.Create(new ModScaffoldOptions
        {
            Folder = source,
            Id = "example.hello",
            Name = "Hello",
            Template = ModTemplateKind.Data,
        });

        Assert.Equal(source, created);
        Assert.True(File.Exists(System.IO.Path.Combine(source, "Hello.mod")));
        Assert.True(File.Exists(System.IO.Path.Combine(source, ModManifest.FileName)));
        Assert.True(File.Exists(System.IO.Path.Combine(source, "content", "example.yaml")));

        var mod = StandaloneMod.Load(source);
        var validation = mod.Validate();
        Assert.True(validation.Success);
        Assert.Single(validation.Bundle.Content.Entries);

        string package = System.IO.Path.Combine(root.Path, "Hello.meitoumod");
        var packed = mod.Pack(new ModPackOptions { Output = package });
        Assert.False(packed.GeneratedDataFile);   // the scaffold already wrote Hello.mod
        var info = ModPackageInstaller.ReadInfo(package);
        Assert.Equal("example.hello", info.Manifest.Id);

        string mods = System.IO.Path.Combine(root.Path, "mods");
        string installed = ModPackageInstaller.Install(package, mods);
        Assert.Equal(System.IO.Path.Combine(mods, "Hello"), installed);
        Assert.True(File.Exists(System.IO.Path.Combine(installed, "Hello.mod")));
        Assert.Throws<IOException>(() => ModPackageInstaller.Install(package, mods));
        Assert.Equal(installed, ModPackageInstaller.Install(package, mods, overwrite: true));

        string repo = System.IO.Path.Combine(root.Path, "repo");
        Directory.CreateDirectory(repo);
        File.Copy(package, System.IO.Path.Combine(repo, "Hello.meitoumod"));
        var index = ModRepository.WriteIndex(repo);
        var entry = Assert.Single(index.Mods);
        Assert.Equal("example.hello", entry.Id);
        string repositoryPackage = System.IO.Path.Combine(repo, "Hello.meitoumod");
        Assert.Equal(repositoryPackage, ModRepository.Resolve(repo, "example.hello"));
        Assert.True(File.Exists(System.IO.Path.Combine(repo, ModRepository.IndexFileName)));
    }

    [Fact]
    public void Pack_generates_a_mod_file_for_a_code_only_source()
    {
        using var root = new ModTempFolder();
        string source = System.IO.Path.Combine(root.Path, "NoData");
        Directory.CreateDirectory(source);
        File.WriteAllText(System.IO.Path.Combine(source, ModManifest.FileName), ModFixtures.ManifestJson("example.nodata"));
        Directory.CreateDirectory(System.IO.Path.Combine(source, "scripts"));
        File.WriteAllText(System.IO.Path.Combine(source, "scripts", "main.lua"), "meitou.log.info('ok')");

        var mod = StandaloneMod.Load(source);
        Assert.False(mod.HasDataFile);
        Assert.True(mod.Validate().Success);

        var packed = mod.Pack(new ModPackOptions { Output = System.IO.Path.Combine(root.Path, "NoData.meitoumod") });
        Assert.True(packed.GeneratedDataFile);
        var info = ModPackageInstaller.ReadInfo(packed.Path);
        Assert.Equal("example.nodata", info.Manifest.Id);

        string mods = System.IO.Path.Combine(root.Path, "mods");
        ModPackageInstaller.Install(packed.Path, mods);
        Assert.True(File.Exists(System.IO.Path.Combine(mods, "NoData", "NoData.mod")));
    }

    [Fact]
    public void Package_reader_rejects_an_entry_that_escapes_the_mod_folder()
    {
        using var root = new ModTempFolder();
        string package = System.IO.Path.Combine(root.Path, "bad.meitoumod");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("../evil.txt").Open()))
                writer.Write("no");
            using (var writer = new StreamWriter(zip.CreateEntry("Bad/meitou.json").Open()))
                writer.Write(ModFixtures.ManifestJson("example.bad"));
        }

        Assert.Throws<InvalidDataException>(() => ModPackageInstaller.ReadInfo(package));
    }

    [Fact]
    public void The_frontier_framework_and_content_register_through_one_shared_api()
    {
        string root = RepoRoot();
        var framework = SampleCandidate(root, "frontier.contracts", "FrontierContracts.dll");
        var content = SampleCandidate(root, "frontier.bounties", "FrontierBounties.dll");
        Assert.SkipWhen(framework is null || content is null, "The sample plugins have not been built.");

        var log = new List<string>();
        var api = new ModApiRuntime(log.Add);
        var host = ModPluginHost.Load(new ModResolution([framework!, content!], [], ModApi.CurrentVersion, null), new GameDatabase(), api, log.Add);

        Assert.Equal(2, host.PluginCount);
        Assert.Empty(api.Contracts.Definitions);
        Assert.True(host.Services.TryGet(ModServiceKeys.Contracts, out var service));
        Assert.IsAssignableFrom<IContractFramework>(service);
        Assert.Contains(log, line => line.Contains("contract-giver framework ready", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("registered 3 Dust Bandit contract templates", StringComparison.Ordinal));
    }

    [Fact]
    public void The_frontier_content_refuses_to_initialize_without_its_framework()
    {
        string root = RepoRoot();
        var content = SampleCandidate(root, "frontier.bounties", "FrontierBounties.dll");
        Assert.SkipWhen(content is null, "The sample plugin has not been built.");

        var log = new List<string>();
        var api = new ModApiRuntime(log.Add);
        var host = ModPluginHost.Load(new ModResolution([content!], [], ModApi.CurrentVersion, null), new GameDatabase(), api, log.Add);

        Assert.Equal(0, host.PluginCount);
        Assert.Contains(log, line => line.Contains("frontier.contracts is required", StringComparison.Ordinal));
    }

    static ModCandidate? SampleCandidate(string root, string folderName, string assemblyName)
    {
        string folder = System.IO.Path.Combine(root, "sdk", "samples", folderName);
        if (!File.Exists(System.IO.Path.Combine(folder, "lib", assemblyName))) return null;
        var read = ModManifest.Read(System.IO.Path.Combine(folder, ModManifest.FileName));
        var manifest = Assert.IsType<ModManifest>(read.Manifest);
        return new ModCandidate(manifest.Id, folder, manifest, read.ManifestSha256 ?? "");
    }

    [Fact]
    public void A_plugin_and_lua_register_into_one_shared_api()
    {
        using var root = new ModTempFolder();
        string folder = System.IO.Path.Combine(root.Path, "Provider");
        Directory.CreateDirectory(folder);
        File.Copy(typeof(BountyProviderMod).Assembly.Location, System.IO.Path.Combine(folder, "Meitou.Tests.dll"));
        var manifest = ModFixtures.Manifest("frontier.provider");
        manifest.Trust = ModTrust.Plugin;
        manifest.Assemblies.Add("Meitou.Tests.dll");
        manifest.Entry = "Meitou.Tests.Modding.BountyProviderMod";
        var plugin = ModFixtures.CandidateAt("Provider", folder, manifest);

        string scriptFolder = System.IO.Path.Combine(root.Path, "Script");
        Directory.CreateDirectory(System.IO.Path.Combine(scriptFolder, "scripts"));
        File.WriteAllText(System.IO.Path.Combine(scriptFolder, "scripts", "main.lua"), """
            meitou.contracts.register { id = "lua", kind = "faction", faction = "20-t", title = "Lua contract", reward = 2 }
            """);
        var script = ModFixtures.CandidateAt("Script", scriptFolder, ModFixtures.Script("frontier.script"));

        var log = new List<string>();
        var api = new ModApiRuntime(log.Add);
        var resolution = new ModResolution([plugin, script], [], ModApi.CurrentVersion, null);
        var host = ModPluginHost.Load(resolution, new GameDatabase(), api, log.Add);
        Assert.True(host.Services.TryGet("contract-id", out var id));
        Assert.Equal("frontier.provider:bounty/plugin", id);
        Assert.Single(api.Contracts.Definitions);

        _ = LuaModHost.Load(resolution, SyntheticTown.Database(), api, log.Add);
        Assert.Equal(2, api.Contracts.Definitions.Count);
        Assert.Contains(api.Contracts.Definitions, definition => definition.Id == "frontier.script:bounty/lua");
    }

    static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(System.IO.Path.Combine(directory.FullName, "MeitouClient.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Repository root not found above the test output.");
    }
}
