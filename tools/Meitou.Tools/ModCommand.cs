using Meitou.Content;
using Meitou.Modding;
using Meitou.Sdk;

/// <summary>The authoring commands behind <c>meitou-tools mod ...</c> (docs/modding-sdk.md).</summary>
static class ModCommand
{
    public static int Init(string folder, string[] args)
    {
        try
        {
            var options = new ModScaffoldOptions { Folder = folder };
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--id": options = options with { Id = Next(args, ref i) }; break;
                    case "--name": options = options with { Name = Next(args, ref i) }; break;
                    case "--template":
                        options = options with
                        {
                            Template = Next(args, ref i) switch
                            {
                                "data" => ModTemplateKind.Data,
                                "script" => ModTemplateKind.Script,
                                "plugin" => ModTemplateKind.Plugin,
                                var value => throw new ArgumentException($"Unknown template '{value}' (data, script or plugin)."),
                            },
                        };
                        break;
                    case "--force": options = options with { Force = true }; break;
                    default: throw new ArgumentException($"Unknown mod init option '{args[i]}'.");
                }
            }

            string root = ModScaffold.Create(options);
            Console.WriteLine($"Created {options.Template.ToString().ToLowerInvariant()} mod in {root}");
            Console.WriteLine("Next: build (plugin), then 'meitou-tools mod validate <folder>' and 'meitou-tools mod pack <folder>'.");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"mod init: {e.Message}");
            return 1;
        }
    }

    public static int Validate(string folder)
    {
        try
        {
            var mod = StandaloneMod.Load(folder);
            var result = mod.Validate();
            Console.WriteLine($"mod: {mod.Id} {mod.Manifest.Version} ({mod.Manifest.Trust.ToString().ToLowerInvariant()}), folder {mod.FolderName}");
            Console.WriteLine($"     {result.Bundle.Vfs.Assets.Count} assets, {result.Bundle.Vfs.ContentFiles.Count} content files, {result.Bundle.Content.Entries.Count} definitions");
            PrintIssues(result.Issues);
            if (result.Success)
            {
                if (!result.HasDataFile) Console.WriteLine("     no <folder>.mod in the source; pack will generate an empty one.");
                Console.WriteLine("mod: OK");
                return 0;
            }
            Console.Error.WriteLine($"mod: {result.Errors.Count} error(s)");
            return 1;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"mod validate: {e.Message}");
            return 1;
        }
    }

    public static int Pack(string folder, string[] args)
    {
        try
        {
            var options = new ModPackOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out": options = options with { Output = Next(args, ref i) }; break;
                    case "--force": options = options with { Force = true }; break;
                    case "--skip-validation": options = options with { SkipValidation = true }; break;
                    default: throw new ArgumentException($"Unknown mod pack option '{args[i]}'.");
                }
            }

            var mod = StandaloneMod.Load(folder);
            var result = mod.Pack(options);
            Console.WriteLine($"Packed {result.Path} ({result.Bytes} bytes){(result.GeneratedDataFile ? ", generated an empty .mod" : "")}");
            return 0;
        }
        catch (ModValidationException e)
        {
            PrintIssues(e.Issues);
            Console.Error.WriteLine($"mod pack: {e.Message}");
            return 1;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"mod pack: {e.Message}");
            return 1;
        }
    }

    public static int Install(string packageOrId, string[] args)
    {
        try
        {
            string? mods = null, repo = null, version = null;
            bool force = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--mods": mods = Next(args, ref i); break;
                    case "--repo": repo = Next(args, ref i); break;
                    case "--version": version = Next(args, ref i); break;
                    case "--force": force = true; break;
                    default: throw new ArgumentException($"Unknown mod install option '{args[i]}'.");
                }
            }

            string package = File.Exists(packageOrId) ? packageOrId
                : repo is not null ? ModRepository.Resolve(repo, packageOrId, version)
                : throw new FileNotFoundException($"No package file or --repo for '{packageOrId}'.");
            if (mods is null)
            {
                var install = GameInstall.Locate() ?? throw new DirectoryNotFoundException(
                    $"Kenshi not found; set {GameInstall.EnvironmentVariable}, create {GameInstall.LocalConfigFile}, or pass --mods <dir>.");
                mods = install.ModsDirectory;
            }
            string target = ModPackage.Install(package, mods, force);
            Console.WriteLine($"Installed {package} -> {target}");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"mod install: {e.Message}");
            return 1;
        }
    }

    public static int Repo(string folder, string[] args)
    {
        try
        {
            string? index = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--index": index = Next(args, ref i); break;
                    default: throw new ArgumentException($"Unknown mod repo option '{args[i]}'.");
                }
            }
            var result = ModRepository.WriteIndex(folder, index);
            Console.WriteLine($"Repository {Path.GetFullPath(folder)}: {result.Mods.Count} package(s)");
            foreach (var entry in result.Mods)
                Console.WriteLine($"  {entry.Id,-34} {entry.Version,-12} {entry.File}");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"mod repo: {e.Message}");
            return 1;
        }
    }

    public static int Graph(string[] args)
    {
        if (args.Length == 0)
        {
            var install = GameInstall.Locate();
            if (install is null)
            {
                Console.Error.WriteLine($"Kenshi install not found. Set {GameInstall.EnvironmentVariable} or create {GameInstall.LocalConfigFile}.");
                return 1;
            }
            var resolution = ModDiscovery.Resolve(install);
            foreach (var candidate in resolution.Order)
            {
                var edges = candidate.Manifest.Depends.Keys.Concat(candidate.Manifest.LoadAfter);
                Console.WriteLine($"{candidate.Id}{(edges.Any() ? " -> " + string.Join(", ", edges) : "")}");
            }
            PrintIssues(resolution.Issues);
            return resolution.Success ? 0 : 1;
        }

        try
        {
            var mod = StandaloneMod.Load(args[0]);
            Console.WriteLine($"{mod.Id} {mod.Manifest.Version}");
            foreach (var (id, range) in mod.Manifest.Depends.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                Console.WriteLine($"  depends {id} {range}");
            foreach (var (id, range) in mod.Manifest.Optional.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                Console.WriteLine($"  optional {id} {range}");
            foreach (string id in mod.Manifest.LoadAfter) Console.WriteLine($"  after {id}");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"mod graph: {e.Message}");
            return 1;
        }
    }

    static string Next(string[] args, ref int i)
    {
        if (++i >= args.Length) throw new ArgumentException($"Option '{args[i - 1]}' needs a value.");
        return args[i];
    }

    static void PrintIssues(IReadOnlyList<ModIssue> issues)
    {
        foreach (var issue in issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.File, StringComparer.Ordinal))
            Console.WriteLine($"  {issue.Severity.ToString().ToUpperInvariant(),-7} {issue.Code,-24} {issue.File ?? issue.FolderName ?? ""}: {issue.Message}");
    }
}
