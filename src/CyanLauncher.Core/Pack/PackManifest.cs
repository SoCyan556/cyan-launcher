using System.Text.Json;
using System.Text.Json.Serialization;

namespace CyanLauncher.Core.Pack;

/// <summary>
/// pack.json — published as an asset on every GitHub release. The launcher reads the copy on the
/// latest release to decide whether the modpack or the launcher itself needs updating.
/// </summary>
public sealed class PackManifest
{
    public const int CurrentFormat = 1;

    public int FormatVersion { get; set; } = CurrentFormat;
    public string Name { get; set; } = "Cyan Civilization";
    /// <summary>Version of the modpack contents. Any change to the file list must bump this.</summary>
    public string PackVersion { get; set; } = "0.0.0";
    public string Minecraft { get; set; } = "1.21.1";
    public string FabricLoader { get; set; } = "0.19.5";
    public int JavaMajor { get; set; } = 21;
    public int RecommendedMemoryMb { get; set; } = 6144;
    /// <summary>Latest launcher build. Null when the release does not ship a launcher.</summary>
    public LauncherRelease? Launcher { get; set; }
    public List<PackFile> Files { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static PackManifest Parse(string json)
    {
        var m = JsonSerializer.Deserialize<PackManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("pack.json is empty.");
        m.Validate();
        return m;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public void Validate()
    {
        if (FormatVersion > CurrentFormat)
            throw new InvalidDataException(
                $"pack.json format {FormatVersion} is newer than this launcher understands. Update the launcher.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Files)
        {
            SafePath.EnsureRelative(f.Path);
            if (!seen.Add(f.Path)) throw new InvalidDataException($"Duplicate path in pack.json: {f.Path}");
            if (!Hashing.IsSha256(f.Sha256)) throw new InvalidDataException($"Bad sha256 for {f.Path}");
            if (string.IsNullOrWhiteSpace(f.Url)) throw new InvalidDataException($"Missing url for {f.Path}");
            if (ExcludedPaths.IsExcluded(f.Path))
                throw new InvalidDataException($"pack.json may not contain personal data: {f.Path}");
        }
    }

    public IEnumerable<PackFile> ManagedFiles => Files.Where(f => f.Kind != PackFileKind.Default);
    public IEnumerable<PackFile> DefaultFiles => Files.Where(f => f.Kind == PackFileKind.Default);
    public IEnumerable<PackFile> Mods => Files.Where(f => f.Kind == PackFileKind.Mod);
}

public sealed class LauncherRelease
{
    public string Version { get; set; } = "0.0.0";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public enum PackFileKind
{
    /// <summary>A mod jar in mods/. Always kept in sync with the pack.</summary>
    Mod,
    ResourcePack,
    ShaderPack,
    /// <summary>Other pack-owned content kept in sync (e.g. Cyan compatibility files).</summary>
    Other,
    /// <summary>
    /// A Cyan default setting (options.txt, config/...). Only written when the file is missing,
    /// or when the player explicitly chooses "Reset to Cyan defaults". Never overwrites on update.
    /// </summary>
    Default,
}

public sealed class PackFile
{
    /// <summary>Path relative to .minecraft, forward slashes.</summary>
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public PackFileKind Kind { get; set; }

    /// <summary>Display name (mod name from fabric.mod.json when known).</summary>
    public string? Name { get; set; }
    public string? ModId { get; set; }
    public string? ModVersion { get; set; }
    /// <summary>Mod ids (inside this pack) this mod needs. Used by the Mods tab dependency guard.</summary>
    public List<string>? Depends { get; set; }
    /// <summary>The "environment" the mod's own fabric.mod.json declares. Informational only.</summary>
    public string? DeclaredEnvironment { get; set; }
    /// <summary>Short description for the Modpack page (from Modrinth when the file is published there).</summary>
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    /// <summary>Project page (https only), e.g. the Modrinth page.</summary>
    public string? ProjectUrl { get; set; }

    /// <summary>
    /// True when the pack maintainer marks this mod as client-side. Only client-side mods can be
    /// turned on/off in the launcher's Mods tab. Defaults to false: nothing is toggleable until marked.
    /// </summary>
    public bool ClientSide { get; set; }

    /// <summary>For client-side mods: whether a fresh install starts with it enabled.</summary>
    public bool DefaultEnabled { get; set; } = true;
}
