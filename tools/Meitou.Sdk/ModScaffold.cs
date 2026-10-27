using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meitou.Data.Fcs;
using Meitou.Modding;

namespace Meitou.Sdk;

public enum ModTemplateKind { Data, Script, Plugin }

public sealed record ModScaffoldOptions
{
    public required string Folder { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public ModTemplateKind Template { get; init; } = ModTemplateKind.Data;
    public bool Force { get; init; }
}

/// <summary>
/// Creates a minimal, valid Meitou mod folder: a manifest, a small empty FCS <c>.mod</c> (so the launcher discovers it and the original game can ignore it),
/// and one starter of the chosen kind. A plugin starter is source-only; it cannot be validated until its assembly is built.
/// </summary>
public static class ModScaffold
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Create(ModScaffoldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Folder)) throw new ArgumentException("A mod folder is required.", nameof(options));

        string folder = Path.GetFullPath(options.Folder);
        string folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folderName.Length == 0) throw new ArgumentException("The mod folder needs a name.", nameof(options));
        if (!ModPath.IsSafeRelative(folderName)) throw new ArgumentException($"The folder name '{folderName}' is not a safe mod name.", nameof(options));

        string id = options.Id ?? "example." + Slug(folderName);
        if (!ModId.IsValid(id)) throw new ArgumentException($"'{id}' is not a namespaced mod ID such as 'author.example'.", nameof(options));
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() && !options.Force)
            throw new IOException($"'{folder}' is not empty; use --force to add the starter files.");
        Directory.CreateDirectory(folder);

        string name = options.Name ?? folderName;
        string modFile = Path.Combine(folder, folderName + ".mod");
        if (File.Exists(modFile) && !options.Force) throw new IOException($"'{modFile}' already exists; use --force to replace it.");

        var manifest = new ModManifest
        {
            Id = id,
            Version = "0.1.0",
            ApiVersion = ModApi.CurrentVersion,
            Trust = options.Template switch
            {
                ModTemplateKind.Script => ModTrust.Script,
                ModTemplateKind.Plugin => ModTrust.Plugin,
                _ => ModTrust.Data,
            },
        };
        switch (options.Template)
        {
            case ModTemplateKind.Data:
                manifest.Content = ["content/"];
                break;
            case ModTemplateKind.Script:
                manifest.Entry = "scripts/main.lua";
                break;
            case ModTemplateKind.Plugin:
                string assembly = Pascal(folderName);
                manifest.Assemblies = ["lib/" + assembly + ".dll"];
                manifest.Entry = "Example." + assembly + ".Plugin";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Template, "Unknown template.");
        }

        File.WriteAllText(Path.Combine(folder, ModManifest.FileName), JsonSerializer.Serialize(manifest, Json), new UTF8Encoding(false));
        WriteEmptyMod(modFile, id);

        switch (options.Template)
        {
            case ModTemplateKind.Data:
                Directory.CreateDirectory(Path.Combine(folder, "content"));
                File.WriteAllText(Path.Combine(folder, "content", "example.yaml"), """
                    id: example
                    type: faction
                    name: Example Faction
                    description: Replace this with your own content.
                    """.ReplaceLineEndings("\n"), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "README.md"), Readme(folderName, name, id, "data"), new UTF8Encoding(false));
                break;
            case ModTemplateKind.Script:
                Directory.CreateDirectory(Path.Combine(folder, "scripts"));
                File.WriteAllText(Path.Combine(folder, "scripts", "main.lua"), """
                    meitou.log.info("hello from " .. meitou.mod_id .. " " .. meitou.version)

                    -- meitou.contracts.register { id = "example", kind = "faction", faction = "Dust Bandits", title = "Example contract" }
                    """.ReplaceLineEndings("\n"), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "README.md"), Readme(folderName, name, id, "script"), new UTF8Encoding(false));
                break;
            case ModTemplateKind.Plugin:
                Directory.CreateDirectory(Path.Combine(folder, "src"));
                File.WriteAllText(Path.Combine(folder, "src", "Plugin.cs"), PluginSource(Pascal(folderName)), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "Plugin.csproj"), PluginProject(Pascal(folderName)), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(folder, "README.md"), Readme(folderName, name, id, "plugin"), new UTF8Encoding(false));
                break;
        }
        return folder;
    }

    /// <summary>Writes a valid empty v17 <c>.mod</c>; it carries no records, so a code/content-only mod is discovered and still has no game-data effect.</summary>
    public static void WriteEmptyMod(string path, string id) => File.WriteAllBytes(path, EmptyModBytes(id));

    /// <summary>The bytes <see cref="WriteEmptyMod"/> writes, for a package entry that is made without touching the source folder.</summary>
    public static byte[] EmptyModBytes(string id)
    {
        var file = new FcsFile
        {
            FileType = FcsFileType.V17,
            Version = 1,
            Author = "Meitou SDK",
            Description = "Scaffold for " + id,
        };
        using var stream = new MemoryStream();
        FcsWriter.Write(file, stream);
        return stream.ToArray();
    }

    static string Readme(string folderName, string name, string id, string kind) => $$"""
        # {{name}}

        Starter {{kind}} mod for Meitou, in the folder `{{folderName}}`. ID: `{{id}}`.
        {{(kind == "plugin" ? "\nSet `MEITOU_API_DIR` to the folder with the game's Meitou API assemblies before building.\n" : "")}}
        ```bash
        # from the MeitouClient repository root
        dotnet run --project tools/Meitou.Tools -- mod validate <path-to-this-mod>
        dotnet run --project tools/Meitou.Tools -- mod pack <path-to-this-mod>
        ```

        Copy `dist/{{id}}-0.1.0.meitoumod` into Meitou's local repository, or install it with `mod install`, then enable `{{folderName}}` in the
        launcher.
        """.ReplaceLineEndings("\n") + "\n";

    static string PluginSource(string assembly) => $$"""
        using Meitou.Modding.Api;

        namespace Example.{{assembly}};

        public sealed class Plugin : IMod
        {
            public void Initialize(IModContext context)
            {
                context.Log.Info($"hello from {context.Id} {context.Version}");
            }
        }
        """.ReplaceLineEndings("\n") + "\n";

    // The API is shipped beside the game. A mod author points MEITOU_API_DIR at the folder with Meitou.Modding.Api.dll and the other Meitou assemblies.
    static string PluginProject(string assembly) => $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <AssemblyName>{{assembly}}</AssemblyName>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <MeitouApiDir Condition="'$(MeitouApiDir)' == ''">$(MEITOU_API_DIR)</MeitouApiDir>
          </PropertyGroup>
          <ItemGroup>
            <Reference Include="Meitou.Modding.Api"><HintPath>$(MeitouApiDir)/Meitou.Modding.Api.dll</HintPath></Reference>
            <Reference Include="Meitou.Simulation"><HintPath>$(MeitouApiDir)/Meitou.Simulation.dll</HintPath></Reference>
            <Reference Include="Meitou.Data"><HintPath>$(MeitouApiDir)/Meitou.Data.dll</HintPath></Reference>
            <Reference Include="Meitou.Core"><HintPath>$(MeitouApiDir)/Meitou.Core.dll</HintPath></Reference>
          </ItemGroup>
          <Target Name="CopyModAssembly" AfterTargets="Build">
            <MakeDir Directories="$(MSBuildProjectDirectory)/lib" />
            <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(MSBuildProjectDirectory)/lib" />
          </Target>
        </Project>
        """.ReplaceLineEndings("\n") + "\n";

    static string Slug(string text)
    {
        var builder = new StringBuilder();
        foreach (char c in text.ToLowerInvariant())
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        string slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "mod" : slug;
    }

    static string Pascal(string text)
    {
        var builder = new StringBuilder();
        bool upper = true;
        foreach (char c in text)
        {
            if (!char.IsAsciiLetterOrDigit(c)) { upper = true; continue; }
            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }
        if (builder.Length == 0) return "Example";
        if (!char.IsLetter(builder[0])) builder.Insert(0, "Mod");
        return builder.ToString();
    }
}
