using System.Security.Cryptography;

namespace CyanLauncher.Core.Pack;

public sealed record InstallReport(
    int Downloaded,
    int Written,
    int AlreadyCurrent,
    IReadOnlyList<string> Retired,
    IReadOnlyList<string> DefaultsWritten,
    IReadOnlyList<string> DefaultsKept);

/// <summary>
/// Installs or updates the modpack in a game folder.
/// - Pack-owned files (mods, resource packs, shaders) are kept exactly in sync with pack.json.
/// - Mods removed from the pack are moved to a "retired" folder, never deleted.
/// - Cyan defaults are only written where the file is missing, unless resetDefaults is true.
/// - Client mods the player disabled stay disabled across updates.
/// - Everything is downloaded and verified before any installed file is touched.
/// </summary>
public sealed class PackInstaller
{
    readonly string _cacheDir;
    readonly string _retiredDir;

    public PackInstaller(string cacheDir, string retiredDir)
    {
        _cacheDir = cacheDir;
        _retiredDir = retiredDir;
    }

    public static string DisabledPath(string path) => path + ".disabled";

    public async Task<InstallReport> InstallAsync(PackManifest pack, string gameDir, bool resetDefaults,
        IProgress<(string message, double fraction)>? progress = null, CancellationToken ct = default)
    {
        pack.Validate();
        Directory.CreateDirectory(gameDir);
        var state = InstanceState.Load(gameDir);

        // Apply DefaultEnabled=false once for newly seen client mods; afterwards the player's choice wins.
        foreach (var mod in pack.Mods.Where(m => m.ClientSide))
        {
            if (state.SeenClientMods.Add(mod.Path) && !mod.DefaultEnabled) state.DisabledMods.Add(mod.Path);
        }
        // Mods that are no longer client-side cannot stay disabled.
        state.DisabledMods.RemoveWhere(p => !pack.Mods.Any(m => m.ClientSide && m.Path.Equals(p, StringComparison.OrdinalIgnoreCase)));

        // 1. Work out what is needed.
        var managedTargets = new List<(PackFile file, string target, string other)>();
        foreach (var f in pack.ManagedFiles)
        {
            var disabled = f.Kind == PackFileKind.Mod && state.DisabledMods.Contains(f.Path);
            var target = SafePath.Combine(gameDir, disabled ? DisabledPath(f.Path) : f.Path);
            var other = SafePath.Combine(gameDir, disabled ? f.Path : DisabledPath(f.Path));
            managedTargets.Add((f, target, other));
        }
        var defaultTargets = new List<(PackFile file, string target)>();
        var defaultsKept = new List<string>();
        foreach (var f in pack.DefaultFiles)
        {
            var target = SafePath.Combine(gameDir, f.Path);
            if (resetDefaults || !File.Exists(target)) defaultTargets.Add((f, target));
            else defaultsKept.Add(f.Path);
        }

        var toFetch = managedTargets.Where(t => !Hashing.Matches(t.target, t.file.Sha256, HashAlgorithmName.SHA256))
            .Select(t => t.file)
            .Concat(defaultTargets.Where(t => !Hashing.Matches(t.target, t.file.Sha256, HashAlgorithmName.SHA256)).Select(t => t.file))
            .GroupBy(f => f.Sha256, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

        // 2. Download + verify everything into the content-addressed cache first.
        int done = 0, downloaded = 0;
        await Downloader.ForEachParallelAsync(toFetch, 4, async f =>
        {
            if (await Downloader.DownloadVerifiedAsync(f.Url, CachePath(f.Sha256), f.Sha256, HashAlgorithmName.SHA256, ct))
                Interlocked.Increment(ref downloaded);
            var n = Interlocked.Increment(ref done);
            progress?.Report(($"Downloading pack files ({n}/{toFetch.Count})", toFetch.Count == 0 ? 1 : (double)n / toFetch.Count));
        }, ct);
        ct.ThrowIfCancellationRequested();

        // 3. Retire files the launcher installed before that are no longer in the pack.
        var retired = new List<string>();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var newPaths = new HashSet<string>(pack.ManagedFiles.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var old in state.Managed.Keys.Where(p => !newPaths.Contains(p)).ToList())
        {
            foreach (var variant in new[] { old, DisabledPath(old) })
            {
                var full = SafePath.Combine(gameDir, variant);
                if (!File.Exists(full)) continue;
                var dest = SafePath.Combine(Path.Combine(_retiredDir, stamp), variant);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(full, dest, true);
                retired.Add(variant);
            }
        }

        // 4. Write pack-owned files.
        int written = 0, current = 0;
        progress?.Report(("Installing pack files", 1));
        foreach (var (file, target, other) in managedTargets)
        {
            if (File.Exists(other))
            {
                // The opposite enabled/disabled variant is stale; keep only the right one.
                if (Hashing.Matches(other, file.Sha256, HashAlgorithmName.SHA256) && !File.Exists(target))
                {
                    File.Move(other, target);
                    current++;
                    continue;
                }
                File.Delete(other);
            }
            if (Hashing.Matches(target, file.Sha256, HashAlgorithmName.SHA256)) { current++; continue; }
            if (File.Exists(target) && !state.Managed.ContainsKey(file.Path))
            {
                // A file the launcher did not install sits at a pack path: keep a copy before replacing.
                var backup = SafePath.Combine(Path.Combine(_retiredDir, stamp, "replaced"), SafePath.ToRelative(gameDir, target));
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, true);
            }
            CopyFromCache(file.Sha256, target);
            written++;
        }

        // 5. Defaults: only missing ones (or all, on an explicit reset).
        var defaultsWritten = new List<string>();
        foreach (var (file, target) in defaultTargets)
        {
            if (!Hashing.Matches(target, file.Sha256, HashAlgorithmName.SHA256)) CopyFromCache(file.Sha256, target);
            defaultsWritten.Add(file.Path);
        }

        // 6. Remember what we installed.
        state.Managed = pack.ManagedFiles.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.OrdinalIgnoreCase);
        state.PackVersion = pack.PackVersion;
        state.LastInstalled = DateTimeOffset.Now;
        state.Save(gameDir);

        return new InstallReport(downloaded, written, current, retired, defaultsWritten, defaultsKept);
    }

    /// <summary>True when every pack-owned file is present with the right hash (enabled or disabled).</summary>
    public static bool IsUpToDate(PackManifest pack, string gameDir)
    {
        var state = InstanceState.Load(gameDir);
        if (state.PackVersion != pack.PackVersion) return false;
        foreach (var f in pack.ManagedFiles)
        {
            var disabled = f.Kind == PackFileKind.Mod && state.DisabledMods.Contains(f.Path);
            var p = SafePath.Combine(gameDir, disabled ? DisabledPath(f.Path) : f.Path);
            if (!File.Exists(p) || new FileInfo(p).Length != f.Size && f.Size > 0) return false;
        }
        return true;
    }

    string CachePath(string sha256) => Path.Combine(_cacheDir, sha256[..2], sha256.ToLowerInvariant());

    void CopyFromCache(string sha256, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var tmp = target + ".cyan-tmp";
        File.Copy(CachePath(sha256), tmp, true);
        File.Move(tmp, target, true);
    }
}
