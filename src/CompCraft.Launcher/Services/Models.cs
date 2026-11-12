using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CompCraft.Launcher.Services;

public static class AppPaths
{
    /// <summary>Everything Minecraft needs (versions, libraries, assets, Java runtime, mods, saves).</summary>
    /// <summary>COMPCRAFT_HOME relocates everything (used to test a build without touching a real install).</summary>
    public static string GameRoot { get; } = Environment.GetEnvironmentVariable("COMPCRAFT_HOME") is { Length: > 0 } home
        ? Path.GetFullPath(home)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".compcraft");
    /// <summary>Launcher-only data: settings, accounts and the WebView2 profile.</summary>
    public static string LauncherData { get; } = Path.Combine(GameRoot, "launcher");
    public static string ConfigDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "config");
    public static string Mods => Path.Combine(GameRoot, "mods");
    /// <summary>Verified pack downloads, named by SHA-256, so repairs and updates never re-download unchanged files.</summary>
    public static string PackCache => Path.Combine(LauncherData, "cache", "pack");
    /// <summary>Files removed from the pack are moved here instead of being deleted.</summary>
    public static string Retired => Path.Combine(LauncherData, "retired");
    /// <summary>Last pack.json downloaded from GitHub, used when offline.</summary>
    public static string CachedManifest => Path.Combine(LauncherData, "pack.json");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task WriteAtomicAsync(string path, string contents, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, contents, ct);
        File.Move(temp, path, overwrite: true);
    }
}


public sealed record LauncherConfig
{
    public string Name { get; init; } = "Comp Craft";
    public string Tagline { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>pack.json of the latest release. The modpack (mods, versions, settings) is defined there, not here.</summary>
    public string ManifestUrl { get; init; } = CyanLauncher.Core.ReleaseChannel.DefaultManifestUrl;

    /// <summary>launcher.json next to the exe wins (for testing); otherwise the copy built into the exe.</summary>
    public static LauncherConfig Load()
    {
        var json = EmbeddedConfig.Read("launcher.json");
        return json is null ? new() : JsonSerializer.Deserialize<LauncherConfig>(json, AppPaths.Json) ?? new();
    }
}

/// <summary>Config files are compiled into the exe so the single-file app and its self-updates stay consistent.</summary>
public static class EmbeddedConfig
{
    public static string? Read(string name)
    {
        var loose = Path.Combine(AppPaths.ConfigDirectory, name);
        if (File.Exists(loose)) return File.ReadAllText(loose);
        using var stream = typeof(EmbeddedConfig).Assembly.GetManifestResourceStream("config." + name);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

public sealed record ServerEntry(string Id, string Name, string Address, int Port, string? Description);

public sealed record LauncherSettings
{
    public int MemoryMb { get; init; } = 6144;
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Fullscreen { get; init; }
    /// <summary>keep | minimize | close</summary>
    public string AfterLaunch { get; init; } = "minimize";
    public string JvmArguments { get; init; } = "";
    public bool ReduceMotion { get; init; }
}

public sealed class SettingsStore
{
    private readonly string _file = Path.Combine(AppPaths.LauncherData, "settings.json");
    public LauncherSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(_file)) Current = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(_file), AppPaths.Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Current = new(); }
    }

    public async Task SaveAsync(LauncherSettings value)
    {
        Current = value;
        await AppPaths.WriteAtomicAsync(_file, JsonSerializer.Serialize(value, AppPaths.Json));
    }

    /// <summary>Applies one validated key from the UI. Unknown keys and out-of-range values are ignored.</summary>
    public LauncherSettings? With(string key, JsonElement value, int systemMemoryMb) => key switch
    {
        "memoryMb" when value.TryGetInt32(out var mb) => Current with { MemoryMb = Math.Clamp(mb, 1024, Math.Max(2048, systemMemoryMb - 1024)) },
        "width" when value.TryGetInt32(out var w) => Current with { Width = Math.Clamp(w, 640, 7680) },
        "height" when value.TryGetInt32(out var h) => Current with { Height = Math.Clamp(h, 360, 4320) },
        "fullscreen" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => Current with { Fullscreen = value.GetBoolean() },
        "reduceMotion" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => Current with { ReduceMotion = value.GetBoolean() },
        "afterLaunch" when value.GetString() is "keep" or "minimize" or "close" => Current with { AfterLaunch = value.GetString()! },
        "jvmArguments" when value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= 2000 => Current with { JvmArguments = value.GetString()!.Trim() },
        _ => null,
    };
}
