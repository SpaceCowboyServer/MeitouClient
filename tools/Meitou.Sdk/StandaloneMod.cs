using System.IO.Compression;
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
