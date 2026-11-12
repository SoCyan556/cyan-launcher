using System.Diagnostics;
using CyanLauncher.Core.Auth;
using CyanLauncher.Core.Game;
using CyanLauncher.Core.Pack;

namespace CyanLauncher.Core;

public sealed record UpdateCheck(PackManifest Pack, bool FromCache, bool PackUpdateAvailable, bool LauncherUpdateAvailable, string? InstalledPackVersion);

/// <summary>Ties the pieces together for the UI: update check, install, game files, Java, launch.</summary>
public sealed class LauncherService
{
    public LauncherPaths Paths { get; }
    public LauncherSettings Settings { get; private set; }

    public LauncherService(LauncherPaths? paths = null)
    {
        // CYANLAUNCHER_HOME relocates all launcher data (used for testing a build without touching the real install).
        Paths = paths ?? new LauncherPaths(Environment.GetEnvironmentVariable("CYANLAUNCHER_HOME") is { Length: > 0 } home ? home : null);
        Settings = LauncherSettings.Load(Paths);
    }

    public string GameDir => string.IsNullOrWhiteSpace(Settings.GameDir) ? Paths.DefaultGameDir : Settings.GameDir!;

    public void SaveSettings(LauncherSettings s)
    {
        s.Save(Paths);
        Settings = s;
    }

    /// <summary>Fetches the latest pack.json; falls back to the last downloaded copy when offline.</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        PackManifest pack;
        var fromCache = false;
        try
        {
            var json = await Downloader.GetStringAsync(Settings.ManifestUrl, ct);
            pack = PackManifest.Parse(json);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.CachedManifest)!);
            await File.WriteAllTextAsync(Paths.CachedManifest, json, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException && File.Exists(Paths.CachedManifest))
        {
            if (!File.Exists(Paths.CachedManifest)) throw;
            pack = PackManifest.Parse(await File.ReadAllTextAsync(Paths.CachedManifest, ct));
            fromCache = true;
        }
        var installed = InstanceState.Load(GameDir).PackVersion;
        var packUpdate = installed is null || !PackInstaller.IsUpToDate(pack, GameDir);
        return new UpdateCheck(pack, fromCache, packUpdate, SelfUpdater.IsNewer(pack), installed);
    }

    public PackInstaller CreateInstaller() => new(Paths.PackCache, Paths.Retired);

    public Task<InstallReport> InstallPackAsync(PackManifest pack, bool resetDefaults, IProgress<(string, double)>? progress, CancellationToken ct) =>
        CreateInstaller().InstallAsync(pack, GameDir, resetDefaults, progress, ct);

    /// <summary>Downloads Minecraft, Fabric and Java (everything except the pack itself). Safe to repeat.</summary>
    public async Task<(ResolvedGame game, string java)> PrepareGameAsync(PackManifest pack, IProgress<(string, double)>? progress, CancellationToken ct)
    {
        var game = await new GameFiles(Paths.GameFiles).PrepareAsync(pack.Minecraft, pack.FabricLoader, progress, ct);
        var java = await new JavaRuntime(Paths.Java).EnsureAsync(pack.JavaMajor, Settings.JavaPath, progress, ct);
        return (game, java);
    }

    public async Task<Process> LaunchAsync(PackManifest pack, GameSession session, IProgress<(string, double)>? progress, CancellationToken ct)
    {
        var (game, java) = await PrepareGameAsync(pack, progress, ct);
        progress?.Report(("Starting Minecraft", 1));
        var options = new LaunchOptions(java, GameDir, Settings.MemoryMb ?? pack.RecommendedMemoryMb, Paths.Temp,
            Path.Combine(Paths.Logs, "game-latest.log"));
        return GameLauncher.Launch(game, options, session);
    }

    public MicrosoftAuth CreateAuth() => new(Settings.EffectiveClientId, new AccountStore(Paths.AccountFile));
    public SavedAccount? SavedAccount => new AccountStore(Paths.AccountFile).Load();
}
