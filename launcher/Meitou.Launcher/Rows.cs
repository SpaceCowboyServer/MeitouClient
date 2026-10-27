using System.ComponentModel;
using System.Runtime.CompilerServices;
using Meitou.Content;
using Meitou.Localisation;
using Meitou.Modding;

namespace Meitou.Launcher;

/// <summary>A line of the Mods page.</summary>
public sealed class ModRow(ModInfo mod, bool enabled) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public ModInfo Mod { get; } = mod;
    public string Name => Mod.Name;
    public string Detail
    {
        get
        {
            var parts = new List<string> { LauncherText.Translate(Mod.Source == ModSource.Workshop ? "Steam workshop" : "Local mod") };
            if (Mod.Header is { Author.Length: > 0 } header) parts.Add(header.Author);
            if (Mod.Manifest is { } manifest) parts.Add($"{manifest.Id} {manifest.Version}");
            return string.Join(" · ", parts);
        }
    }

    public string Notes
    {
        get
        {
            if (Mod.ManifestError is { } error)
                return Loc.Format(LauncherText.Translate("Invalid meitou.json: {1}"), error);
            if (Mod.HasNativePlugin)
                return LauncherText.Translate("RE_Kenshi plugin: its DLLs are ignored in Meitou");
            if (Mod.Manifest is { Trust: ModTrust.Plugin } plugin)
                return Loc.Format(LauncherText.Translate("Meitou C# mod: {1} assembly(ies)"), plugin.Assemblies.Count);
            if (Mod.Manifest is { Trust: ModTrust.Script } script)
                return Loc.Format(LauncherText.Translate("Meitou Lua mod: {1}"), script.Entry ?? "");
            return "";
        }
    }

    public bool HasNotes => Notes.Length > 0;

    /// <summary>Only local mod folders can be deleted here; Workshop content belongs to Steam.</summary>
    public bool CanDelete => Mod.Source == ModSource.Local;

    public bool Enabled
    {
        get;
        set { if (field != value) { field = value; Changed(); } }
    } = enabled;

    /// <summary>The launcher's language changed: the translated texts are read again.</summary>
    public void LanguageChanged()
    {
        Changed(nameof(Detail));
        Changed(nameof(Notes));
    }

    void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A line of the Versions page.</summary>
public sealed class ReleaseRow(Release release, bool installed, bool played)
{
    public Release Release { get; } = release;
    public bool Installed { get; } = installed;
    public string Title => Release.Title;
    public string Detail => string.Join(" · ", new[]
    {
        Release.Published.ToString("yyyy-MM-dd"),
        Release.Prerelease ? LauncherText.Translate("nightly") : null,
        Release.Asset is null ? Loc.Format(LauncherText.Translate("no {1} build"), Releases.Rid) : null,
    }.OfType<string>());
    /// <summary>The version Play starts.</summary>
    public bool Played { get; } = played;
    public string State => Played ? LauncherText.Translate("Installed · Play") : Installed ? LauncherText.Translate("Installed") : "";
}
