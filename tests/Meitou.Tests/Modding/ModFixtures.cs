using Meitou.Modding;

namespace Meitou.Tests.Modding;

static class ModFixtures
{
    public static ModManifest Manifest(string id, string version = "1.0.0") => new()
    {
        Id = id,
        Version = version,
    };

    public static ModManifest Plugin(string id, string version = "1.0.0")
    {
        var manifest = Manifest(id, version);
        manifest.Trust = ModTrust.Plugin;
        manifest.Assemblies.Add("lib/" + id + ".dll");
        manifest.Entry = "Mod.Entry";
        return manifest;
    }

    public static ModManifest Script(string id, string version = "1.0.0")
    {
        var manifest = Manifest(id, version);
        manifest.Trust = ModTrust.Script;
        manifest.Entry = "scripts/main.lua";
        return manifest;
    }

    public static ModCandidate Candidate(string folder, ModManifest manifest) =>
        new(folder, "/mods/" + folder, manifest, new string('a', 64));

    public static ModCandidate CandidateAt(string folder, string fullPath, ModManifest manifest) =>
        new(folder, fullPath, manifest, new string('a', 64));

    public static string ManifestJson(string id, string version = "1.0.0", string extra = "") => $$"""
        {
          "id": "{{id}}",
          "version": "{{version}}"{{extra}}
        }
        """;
}
