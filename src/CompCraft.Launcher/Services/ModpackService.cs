using System.IO;
using System.Net.Http;
using System.Text.Json;
using CyanLauncher.Core;
using CyanLauncher.Core.Pack;

namespace CompCraft.Launcher.Services;

public sealed record ModInfo(string Path, string Title, string Description, string? IconUrl, string? Version, string? ProjectUrl,
    bool ClientSide, bool Enabled, bool Installed);

/// <summary>
/// The modpack comes from pack.json on the latest GitHub release (see CyanLauncher.Core). This service checks for
/// updates, installs/updates the pack into the game folder (keeping the player's settings), and backs the
/// Modpack page's client-side mod toggles.
/// </summary>
public sealed class ModpackService(LauncherConfig config)
{
    public PackManifest? Pack { get; private set; }
    /// <summary>idle | checking | ready | offline | unavailable</summary>
    public string Status { get; private set; } = "idle";
    public string? InstalledVersion => (_installedVersion ??= InstanceState.Load(AppPaths.GameRoot).PackVersion ?? "") is { Length: > 0 } v ? v : null;
    public bool Installed => !string.IsNullOrEmpty(InstalledVersion);
    public bool UpdateAvailable => _updateAvailable ??= Pack is not null && Installed && !PackInstaller.IsUpToDate(Pack, AppPaths.GameRoot);
    public event EventHandler? Changed;

    // The UI asks for this state many times a second during downloads; recompute only after something changed.
    private IReadOnlyList<ModInfo>? _mods;
    private string? _installedVersion;
    private bool? _updateAvailable;

    private void Notify()
    {
        _mods = null; _installedVersion = null; _updateAvailable = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<ModInfo> Mods => _mods ??= ListMods();

    private IReadOnlyList<ModInfo> ListMods()
    {
        {
            if (Pack is null) return [];
            var mods = Pack.Mods.ToDictionary(m => m.Path, StringComparer.OrdinalIgnoreCase);
            return ModManager.List(Pack, AppPaths.GameRoot).Select(e =>
            {
                var f = mods[e.Path];
                var installed = File.Exists(Path.Combine(AppPaths.GameRoot, e.Path)) || File.Exists(Path.Combine(AppPaths.GameRoot, PackInstaller.DisabledPath(e.Path)));
                return new ModInfo(e.Path, e.DisplayName, f.Description ?? "", f.IconUrl, e.Version, f.ProjectUrl, e.ClientSide, e.Enabled, installed);
            })
            .OrderByDescending(m => m.ClientSide).ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Downloads the latest pack.json. Falls back to the last downloaded copy when offline.</summary>
    public async Task CheckAsync(CancellationToken ct)
    {
        Status = "checking"; Notify();
        try
        {
            var json = await Downloader.GetStringAsync(config.ManifestUrl, ct);
            Pack = PackManifest.Parse(json);
            await AppPaths.WriteAtomicAsync(AppPaths.CachedManifest, json, ct);
            Status = "ready";
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or IOException or JsonException or InvalidDataException) && !ct.IsCancellationRequested)
        {
            Status = LoadCached() ? "offline" : "unavailable";
        }
        finally { Notify(); }
    }

    /// <summary>Installs or updates every pack file. Cyan defaults only fill in missing settings unless resetDefaults is true.</summary>
    public async Task<InstallReport> InstallAsync(IProgress<(int Done, int Total, string Name)> progress, bool resetDefaults, CancellationToken ct)
    {
        if (Pack is null || Status != "ready") await CheckAsync(ct);
        if (Pack is null) throw new InvalidOperationException("The modpack list could not be downloaded. Check your connection and try again.");
        RetireOldCompCraftMods();

        var installer = new PackInstaller(AppPaths.PackCache, AppPaths.Retired);
        var report = await installer.InstallAsync(Pack, AppPaths.GameRoot, resetDefaults, new Progress<(string message, double fraction)>(p =>
            progress.Report(((int)(p.fraction * 1000), 1000, p.message))), ct);
        Notify();
        return report;
    }

    public void SetEnabled(string path, bool enabled)
    {
        if (Pack is null) throw new InvalidOperationException("The modpack list is not loaded yet.");
        ModManager.SetEnabled(Pack, AppPaths.GameRoot, path, enabled);
        Notify();
    }

    private bool LoadCached()
    {
        try
        {
            if (!File.Exists(AppPaths.CachedManifest)) return false;
            Pack = PackManifest.Parse(File.ReadAllText(AppPaths.CachedManifest));
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException) { return false; }
    }

    /// <summary>
    /// Earlier Comp Craft builds synced a Modrinth mod list and tracked it in mods/.compcraft-managed.json.
    /// Those jars would clash with the pack's own versions, so they are moved to the retired folder once.
    /// </summary>
    private static void RetireOldCompCraftMods()
    {
        var managed = Path.Combine(AppPaths.Mods, ".compcraft-managed.json");
        var oldLock = Path.Combine(AppPaths.GameRoot, "modpack-lock.json");
        try
        {
            if (File.Exists(managed))
            {
                var dest = Path.Combine(AppPaths.Retired, "compcraft-modrinth-mods");
                foreach (var name in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(managed)) ?? [])
                {
                    var file = Path.Combine(AppPaths.Mods, Path.GetFileName(name));
                    if (!File.Exists(file)) continue;
                    Directory.CreateDirectory(dest);
                    File.Move(file, Path.Combine(dest, Path.GetFileName(name)), overwrite: true);
                }
                File.Delete(managed);
            }
            if (File.Exists(oldLock)) File.Delete(oldLock);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* Retried on the next install. */ }
    }
}
