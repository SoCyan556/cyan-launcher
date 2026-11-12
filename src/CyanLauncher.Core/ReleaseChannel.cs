namespace CyanLauncher.Core;

/// <summary>Where the launcher looks for pack and launcher updates.</summary>
public static class ReleaseChannel
{
    /// <summary>GitHub "owner/repo" that hosts releases.</summary>
    public const string GitHubRepo = "SoCyan556/cyan-launcher";

    /// <summary>GitHub always redirects this URL to the pack.json on the newest (non-prerelease) release.</summary>
    public static string DefaultManifestUrl => $"https://github.com/{GitHubRepo}/releases/latest/download/pack.json";
}
