using System.Text.Json;
using CyanLauncher.Core.Pack;

namespace CyanLauncher.Core;

/// <summary>Release channel defaults. Change these before publishing the first release.</summary>
public static class ReleaseChannel
{
    /// <summary>GitHub "owner/repo" that hosts releases. </summary>
    public const string GitHubRepo = "SoCyan556/cyan-launcher";

    /// <summary>GitHub always redirects this URL to the pack.json on the newest (non-prerelease) release.</summary>
    public static string DefaultManifestUrl => $"https://github.com/{GitHubRepo}/releases/latest/download/pack.json";

    /// <summary>
    /// Azure (Entra ID) app registration client ID used for Microsoft sign-in. Empty until registered
    /// and approved for the Minecraft services API. Can also be set in Settings.
    /// </summary>
    public const string MicrosoftClientId = "";
}

public sealed class LauncherSettings
{
    public string ManifestUrl { get; set; } = ReleaseChannel.DefaultManifestUrl;
    public string? GameDir { get; set; }
    public int? MemoryMb { get; set; }
    /// <summary>Leave empty to let the launcher manage Java itself.</summary>
    public string? JavaPath { get; set; }
    public string? MicrosoftClientId { get; set; }
    public string? OfflineUsername { get; set; }
    public bool CloseOnLaunch { get; set; }

    public static LauncherSettings Load(LauncherPaths paths)
    {
        if (!File.Exists(paths.SettingsFile)) return new LauncherSettings();
        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(paths.SettingsFile), PackManifest.JsonOptions) ?? new();
        }
        catch (JsonException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, PackManifest.JsonOptions));
    }

    public string EffectiveClientId => string.IsNullOrWhiteSpace(MicrosoftClientId) ? ReleaseChannel.MicrosoftClientId : MicrosoftClientId!;
}

/// <summary>Folder layout under %LOCALAPPDATA%\CyanLauncher.</summary>
public sealed class LauncherPaths
{
    public LauncherPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyanLauncher");
    }

    public string Root { get; }
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string AccountFile => Path.Combine(Root, "account.dat");
    public string PackCache => Path.Combine(Root, "cache", "pack");
    public string CachedManifest => Path.Combine(Root, "cache", "pack.json");
    public string Retired => Path.Combine(Root, "retired");
    public string GameFiles => Path.Combine(Root, "game");
    public string Java => Path.Combine(Root, "java");
    public string Logs => Path.Combine(Root, "logs");
    public string Temp => Path.Combine(Root, "temp");
    public string DefaultGameDir => Path.Combine(Root, "instance", ".minecraft");
}
