using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Meitou.Modding;

namespace Meitou.Sdk;

public sealed record StandaloneModValidation(ModContentBundle Bundle, IReadOnlyList<ModIssue> Issues, bool HasDataFile)
{
    public bool Success => Issues.All(issue => issue.Severity != ModIssueSeverity.Error);
    public IReadOnlyList<ModIssue> Errors => [.. Issues.Where(issue => issue.Severity == ModIssueSeverity.Error)];
    public IReadOnlyList<ModIssue> Warnings => [.. Issues.Where(issue => issue.Severity == ModIssueSeverity.Warning)];
}

public sealed class ModValidationException(IReadOnlyList<ModIssue> issues) : Exception(
    issues.Count == 0 ? "The mod is invalid." : $"The mod has {issues.Count(issue => issue.Severity == ModIssueSeverity.Error)} error(s).")
{
    public IReadOnlyList<ModIssue> Issues { get; } = issues;
}

/// <summary>A mod folder as a standalone project, without an install or a resolved load order.</summary>
public sealed class StandaloneMod
{
    StandaloneMod(string folder, string folderName, ModManifest manifest, string? manifestSha256)
    {
        Folder = folder;
        FolderName = folderName;
        Manifest = manifest;
        ManifestSha256 = manifestSha256;
    }

    public string Folder { get; }
    public string FolderName { get; }
    public ModManifest Manifest { get; }
    public string? ManifestSha256 { get; }
    public string Id => Manifest.Id;
    public bool HasDataFile => Directory.EnumerateFiles(Folder, "*.mod", SearchOption.TopDirectoryOnly)
        .Any(file => Path.GetFileName(file) == FolderName + ".mod");

    public static StandaloneMod Load(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string full = Path.GetFullPath(folder);
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Mod folder '{full}' does not exist.");
        string folderName = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folderName.Length == 0) throw new InvalidDataException("The mod folder has no name.");
        if (!ModPath.IsSafeRelative(folderName)) throw new InvalidDataException($"The mod folder name '{folderName}' is not a safe mod name.");
        var read = ModManifest.Read(Path.Combine(full, ModManifest.FileName));
        if (read.Manifest is null)
            throw new InvalidDataException($"'{full}' has no valid {ModManifest.FileName}: {read.Error ?? "the file is missing"}.");
        return new StandaloneMod(full, folderName, read.Manifest, read.ManifestSha256);
    }

    public ModCandidate Candidate() => new(FolderName, Folder, Manifest, ManifestSha256 ?? "");

    public StandaloneModValidation Validate()
    {
        var candidate = Candidate();
        var resolution = new ModResolution([candidate], [], ModApi.CurrentVersion, null);
        var bundle = ModContentBundle.Load(resolution);
        var issues = bundle.Issues.Concat(ModFileDiagnostics.Validate(resolution)).ToList();
        return new StandaloneModValidation(bundle, issues, HasDataFile);
    }

    /// <summary>Writes a <c>.meitoumod</c> zip. A missing <c>&lt;folder&gt;.mod</c> is generated for the package, never in the source.</summary>
    public ModPackageResult Pack(ModPackOptions? options = null)
    {
        options ??= new ModPackOptions();
        if (!options.SkipValidation)
        {
            var validation = Validate();
            if (!validation.Success) throw new ModValidationException(validation.Issues);
        }

        string fileName = $"{Manifest.Id}-{Manifest.Version}.meitoumod";
        string output = Path.GetFullPath(options.Output ?? Path.Combine(Directory.GetCurrentDirectory(), "dist", fileName));
        if (!output.EndsWith(".meitoumod", StringComparison.OrdinalIgnoreCase)) output += ".meitoumod";
        if (File.Exists(output) && !options.Force) throw new IOException($"'{output}' already exists; use --force to replace it.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var dataFiles = Directory.EnumerateFiles(Folder, "*.mod", SearchOption.TopDirectoryOnly).ToList();
        if (dataFiles.Count > 0 && !dataFiles.Any(file => Path.GetFileName(file) == FolderName + ".mod"))
            throw new InvalidDataException($"The mod has a .mod file but not '{FolderName}.mod'; the launcher and the original game cannot discover it.");
        bool generatedMod = dataFiles.Count == 0;
        var payload = EnumeratePayload().ToList();

        string temp = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string file in payload)
                {
                    string relative = Path.GetRelativePath(Folder, file).Replace('\\', '/');
                    AddEntry(zip, FolderName + "/" + relative, file);
                }
                if (generatedMod)
                    AddBytes(zip, FolderName + "/" + FolderName + ".mod", ModScaffold.EmptyModBytes(Manifest.Id));
            }
            File.Move(temp, output, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw;
        }
        return new ModPackageResult(output, new FileInfo(output).Length, generatedMod);
    }

    IEnumerable<string> EnumeratePayload()
    {
        foreach (string file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(Folder, file).Replace('\\', '/');
            if (!ModPath.IsSafeRelative(relative)) throw new InvalidDataException($"Unsafe file path '{relative}'.");
            string[] parts = relative.Split('/');
            if (parts.Any(part => part is "bin" or "obj" or ".git" or ".vs" or "dist")) continue;
            if (file.EndsWith(".meitoumod", StringComparison.OrdinalIgnoreCase)) continue;
            if (Path.GetFileName(file) is ".DS_Store" or "Thumbs.db") continue;
            yield return file;
        }
    }

    static void AddEntry(ZipArchive zip, string name, string path)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var target = entry.Open();
        using var source = File.OpenRead(path);
        source.CopyTo(target);
    }

    static void AddBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var target = entry.Open();
        target.Write(bytes);
    }
}

public sealed record ModPackOptions
{
    public string? Output { get; init; }
    public bool Force { get; init; }
    public bool SkipValidation { get; init; }
}

public sealed record ModPackageResult(string Path, long Bytes, bool GeneratedDataFile);

public sealed record ModPackageInfo(string FolderName, ModManifest Manifest, string? ManifestSha256);

/// <summary>Safe pack/install helpers for the <c>.meitoumod</c> zip format.</summary>
public static class ModPackage
{
    const int MaxEntries = 10_000;
    const long MaxBytes = 512L * 1024 * 1024;

    public static ModPackageInfo ReadInfo(string package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(package);
        using var archive = ZipFile.OpenRead(Path.GetFullPath(package));
        var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToList();
        if (entries.Count == 0 || entries.Count > MaxEntries) throw new InvalidDataException("The package has an invalid number of files.");
        string? root = null;
        ZipArchiveEntry? manifestEntry = null;
        long total = 0;
        foreach (var entry in entries)
        {
            string name = Normalize(entry.FullName);
            string[] parts = name.Split('/');
            if (parts.Length < 2) throw new InvalidDataException($"Package entry '{entry.FullName}' is outside a mod folder.");
            string candidateRoot = parts[0];
            if (root is null) root = candidateRoot;
            else if (root != candidateRoot) throw new InvalidDataException("A package must contain exactly one top-level mod folder.");
            if (parts is [_, "meitou.json"]) manifestEntry = entry;
            total += entry.Length;
            if (total > MaxBytes) throw new InvalidDataException("The package expands to more than 512 MiB.");
        }
        if (root is null || manifestEntry is null) throw new InvalidDataException("The package has no <mod>/meitou.json.");
        if (!ModPath.IsSafeRelative(root)) throw new InvalidDataException("The package's top-level folder name is unsafe.");

        using var stream = manifestEntry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        if (buffer.Length > 1 << 20) throw new InvalidDataException("The package manifest is larger than 1 MiB.");
        byte[] bytes = buffer.ToArray();
        var read = ModManifest.Parse(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        if (read.Manifest is null) throw new InvalidDataException($"The package manifest is invalid: {read.Error}");
        return new ModPackageInfo(root, read.Manifest, read.ManifestSha256);
    }

    /// <summary>Extracts a package into <paramref name="modsDirectory"/>/&lt;folder&gt;. Existing folders are kept unless <paramref name="overwrite"/>.</summary>
    public static string Install(string package, string modsDirectory, bool overwrite = false)
    {
        string fullPackage = Path.GetFullPath(package);
        var info = ReadInfo(fullPackage);
        string mods = Path.GetFullPath(modsDirectory);
        Directory.CreateDirectory(mods);
        string target = Path.Combine(mods, info.FolderName);
        if (Directory.Exists(target) && !overwrite)
            throw new IOException($"'{target}' already exists; use --force to replace it.");

        // Keep the staging folder on the mods volume: Directory.Move cannot cross volumes on Windows.
        string temp = Path.Combine(mods, ".meitou-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            using (var archive = ZipFile.OpenRead(fullPackage))
            {
                long total = 0;
                int files = 0;
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string name = Normalize(entry.FullName);
                    if (++files > MaxEntries) throw new InvalidDataException("The package has too many files.");
                    total += entry.Length;
                    if (total > MaxBytes) throw new InvalidDataException("The package expands to more than 512 MiB.");
                    string[] parts = name.Split('/');
                    if (parts[0] != info.FolderName) throw new InvalidDataException("A package must contain exactly one top-level mod folder.");
                    string relative = string.Join('/', parts.Skip(1));
                    if (!ModPath.IsSafeRelative(relative)) throw new InvalidDataException($"Unsafe package entry '{entry.FullName}'.");
                    string destination = Path.Combine(temp, info.FolderName, relative.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var source = entry.Open();
                    using var targetFile = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    source.CopyTo(targetFile);
                }
            }

            string extracted = Path.Combine(temp, info.FolderName);
            var read = ModManifest.Read(Path.Combine(extracted, ModManifest.FileName));
            if (read.Manifest is null) throw new InvalidDataException($"The extracted package has no valid manifest: {read.Error}");
            if (!Directory.EnumerateFiles(extracted, "*.mod", SearchOption.TopDirectoryOnly)
                    .Any(file => Path.GetFileName(file) == info.FolderName + ".mod"))
                throw new InvalidDataException($"The package has no '{info.FolderName}.mod', so the launcher and the original game cannot discover it.");
            if (Directory.Exists(target))
            {
                string backup = target + ".old-" + Guid.NewGuid().ToString("N");
                Directory.Move(target, backup);
                try
                {
                    Directory.Move(extracted, target);
                    Directory.Delete(backup, recursive: true);
                }
                catch
                {
                    if (!Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
                    throw;
                }
            }
            else
            {
                Directory.Move(extracted, target);
            }
            return target;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch (IOException) { }
        }
    }

    static string Normalize(string name)
    {
        string normalized = name.Replace('\\', '/');
        if (!ModPath.IsSafeRelative(normalized)) throw new InvalidDataException($"Unsafe package entry '{name}'.");
        return normalized.TrimEnd('/');
    }
}
