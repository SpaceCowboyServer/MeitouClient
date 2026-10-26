namespace Meitou.Tests.Modding;

/// <summary>A unique folder under the system temp directory, removed after the test.</summary>
sealed class ModTempFolder : IDisposable
{
    public ModTempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "meitou-modding-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Writes a file below the folder, creating its parent directories; returns the file's full path.</summary>
    public string Write(string relativePath, string contents)
    {
        string path = System.IO.Path.Combine(Path, relativePath);
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (directory is not null) Directory.CreateDirectory(directory);
        File.WriteAllText(path, contents);
        return path;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
