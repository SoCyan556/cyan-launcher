namespace CyanLauncher.Core.Pack;

public static class SafePath
{
    /// <summary>Rejects absolute paths, drive letters, "..", empty segments and backslashes.</summary>
    public static void EnsureRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new InvalidDataException("Empty path.");
        if (relative.Contains('\\') || relative.Contains(':') || relative.StartsWith('/'))
            throw new InvalidDataException($"Unsafe path: {relative}");
        foreach (var seg in relative.Split('/'))
        {
            if (seg.Length == 0 || seg == "." || seg == "..")
                throw new InvalidDataException($"Unsafe path: {relative}");
            if (seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Unsafe path: {relative}");
        }
    }

    /// <summary>Combines root and a relative pack path, guaranteeing the result stays inside root.</summary>
    public static string Combine(string root, string relative)
    {
        EnsureRelative(relative);
        var baseFull = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(baseFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes the game folder: {relative}");
        return full;
    }

    public static string ToRelative(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        EnsureRelative(rel);
        return rel;
    }
}

/// <summary>
/// Personal or generated data that must never ship in a pack or be overwritten by the launcher:
/// accounts, saves, waypoints, explored maps, schematics, lyric caches, per-player voice settings.
/// </summary>
public static class ExcludedPaths
{
    static readonly string[] Prefixes =
    {
        "saves/", "screenshots/", "logs/", "crash-reports/", "schematics/", "xaero/", "journeymap/",
        "XaeroWaypoints", "XaeroWorldMap", "backups/", "debug/", ".fabric/", ".mixin.out/",
        "config/lyricsplus/cache/", "config/litematica/litematica_", "config/Veinminer/update/",
        "config/obscuria/cache/", "essential/", "data/", "baritone/",
    };

    static readonly string[] ExactNames =
    {
        "accounts.json", "launcher_accounts.json", "launcher_profiles.json", "usercache.json",
        "usernamecache.json", "servers.dat", "servers.dat_old", "command_history.txt",
        "config/voicechat/player-volumes.properties", "config/voicechat/category-volumes.properties",
        "config/voicechat/username-cache.json", "config/jade/usernamecache.json",
        "config/sodium-fingerprint.json", "config/xaeropatreon.txt",
        "config/jamlib/known_suspicious_jars.txt",
    };

    public static bool IsExcluded(string relative)
    {
        var r = relative.Replace('\\', '/');
        foreach (var p in Prefixes)
            if (r.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var n in ExactNames)
            if (r.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
        if (r.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
