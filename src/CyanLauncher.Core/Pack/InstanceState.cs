using System.Text.Json;

namespace CyanLauncher.Core.Pack;

/// <summary>
/// What the launcher installed into a game folder, stored as .minecraft/cyan-launcher-state.json.
/// It lets updates remove only files the launcher put there, and remembers which client mods the
/// player turned off so updates do not turn them back on.
/// </summary>
public sealed class InstanceState
{
    public const string FileName = "cyan-launcher-state.json";

    public string? PackVersion { get; set; }
    /// <summary>Pack-owned (non-default) files installed last time: path → sha256.</summary>
    public Dictionary<string, string> Managed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Client-side mods the player disabled (pack paths, e.g. "mods/x.jar").</summary>
    public HashSet<string> DisabledMods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Client-side mods whose DefaultEnabled=false was already applied once.</summary>
    public HashSet<string> SeenClientMods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTimeOffset? LastInstalled { get; set; }

    public static InstanceState Load(string gameDir)
    {
        var p = Path.Combine(gameDir, FileName);
        if (!File.Exists(p)) return new InstanceState();
        var s = JsonSerializer.Deserialize<InstanceState>(File.ReadAllText(p), PackManifest.JsonOptions) ?? new InstanceState();
        // Re-create collections with case-insensitive comparers after deserialization.
        s.Managed = new Dictionary<string, string>(s.Managed, StringComparer.OrdinalIgnoreCase);
        s.DisabledMods = new HashSet<string>(s.DisabledMods, StringComparer.OrdinalIgnoreCase);
        s.SeenClientMods = new HashSet<string>(s.SeenClientMods, StringComparer.OrdinalIgnoreCase);
        return s;
    }

    public void Save(string gameDir)
    {
        Directory.CreateDirectory(gameDir);
        var p = Path.Combine(gameDir, FileName);
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, PackManifest.JsonOptions));
        File.Move(tmp, p, true);
    }
}
