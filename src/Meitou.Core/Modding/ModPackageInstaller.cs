using System.IO.Compression;
using System.Security.Cryptography;

namespace Meitou.Modding;

public sealed record ModPackageInfo(string FolderName, ModManifest Manifest, string? ManifestSha256);

/// <summary>
/// AOT-safe reader and installer for <c>.meitoumod</c> packages. The launcher uses this without referencing the JIT content/validation projects;
/// packing and deep validation remain in <c>Meitou.Sdk</c> / <c>Meitou.Tools</c>.
/// </summary>
public static class ModPackageInstaller
{
    const int MaxEntries = 10_000;
    const long MaxBytes = 512L * 1024 * 1024;
    const int MaxManifestBytes = 1 << 20;

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
        CopyLimited(stream, buffer, MaxManifestBytes, "The package manifest is larger than 1 MiB.");
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

    static void CopyLimited(Stream source, Stream destination, int limit, string error)
    {
        byte[] buffer = new byte[8192];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            destination.Write(buffer, 0, read);
            if (destination.Length > limit) throw new InvalidDataException(error);
        }
    }

    static string Normalize(string name)
    {
        string normalized = name.Replace('\\', '/');
        if (!ModPath.IsSafeRelative(normalized)) throw new InvalidDataException($"Unsafe package entry '{name}'.");
        return normalized.TrimEnd('/');
    }
}
