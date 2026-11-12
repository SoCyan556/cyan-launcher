using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CyanLauncher.Core.Game;

/// <summary>Everything needed to start a resolved Minecraft + Fabric version.</summary>
public sealed class ResolvedGame
{
    public required string VersionId { get; init; }
    public required string MainClass { get; init; }
    public required List<string> Classpath { get; init; }
    public required JsonObject VanillaJson { get; init; }
    public required JsonObject FabricJson { get; init; }
    public required string AssetsRoot { get; init; }
    public required string AssetIndexId { get; init; }
    public required string LibrariesDir { get; init; }
    public required string NativesDir { get; init; }
    public string? LoggingArgument { get; init; }
}

/// <summary>
/// Downloads vanilla Minecraft (client jar, libraries, assets) from Mojang and the Fabric loader
/// profile from meta.fabricmc.net into a shared game directory. Every file is hash-checked.
/// </summary>
public sealed class GameFiles
{
    const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    const string AssetBaseUrl = "https://resources.download.minecraft.net/";

    readonly string _root;
    public GameFiles(string root) => _root = root;

    string VersionsDir => Path.Combine(_root, "versions");
    string LibrariesDir => Path.Combine(_root, "libraries");
    string AssetsDir => Path.Combine(_root, "assets");

    public async Task<ResolvedGame> PrepareAsync(string minecraft, string fabricLoader,
        IProgress<(string message, double fraction)>? progress, CancellationToken ct)
    {
        progress?.Report(("Reading Minecraft version list", 0));
        var vanilla = await LoadVanillaJsonAsync(minecraft, ct);

        // Client jar
        var client = vanilla["downloads"]!["client"]!;
        var clientJar = Path.Combine(VersionsDir, minecraft, minecraft + ".jar");
        progress?.Report(("Downloading Minecraft " + minecraft, 0.02));
        await Downloader.DownloadVerifiedAsync((string)client["url"]!, clientJar, (string)client["sha1"]!, HashAlgorithmName.SHA1, ct);

        // Libraries (vanilla)
        var classpath = new List<string>();
        var vanillaLibs = new List<(string key, string path, string url, string sha1)>();
        foreach (var lib in vanilla["libraries"]!.AsArray())
        {
            if (lib is null || !Rules.Allowed(lib["rules"] as JsonArray)) continue;
            var art = lib["downloads"]?["artifact"];
            if (art is null) continue;
            var path = Path.Combine(LibrariesDir, ((string)art["path"]!).Replace('/', Path.DirectorySeparatorChar));
            vanillaLibs.Add((LibraryKey((string)lib["name"]!), path, (string)art["url"]!, (string)art["sha1"]!));
        }

        // Fabric loader profile
        progress?.Report(("Reading Fabric loader " + fabricLoader, 0.05));
        var fabric = await LoadFabricJsonAsync(minecraft, fabricLoader, ct);
        var fabricLibs = new List<(string key, string path, string url, string? sha1)>();
        foreach (var lib in fabric["libraries"]!.AsArray())
        {
            var name = (string)lib!["name"]!;
            var rel = MavenPath(name);
            var baseUrl = ((string?)lib["url"] ?? "https://maven.fabricmc.net/").TrimEnd('/') + "/";
            fabricLibs.Add((LibraryKey(name), Path.Combine(LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar)), baseUrl + rel, (string?)lib["sha1"]));
        }

        // Fabric versions win over vanilla ones for the same artifact (e.g. ASM).
        var fabricKeys = fabricLibs.Select(l => l.key).ToHashSet();
        var allLibs = fabricLibs.Select(l => (l.path, l.url, l.sha1))
            .Concat(vanillaLibs.Where(l => !fabricKeys.Contains(l.key)).Select(l => (l.path, l.url, sha1: (string?)l.sha1)))
            .ToList();

        int n = 0;
        await Downloader.ForEachParallelAsync(allLibs, 8, async l =>
        {
            if (l.sha1 is not null)
                await Downloader.DownloadVerifiedAsync(l.url, l.path, l.sha1, HashAlgorithmName.SHA1, ct);
            else
                await DownloadWithMavenSha1Async(l.url, l.path, ct);
            var i = Interlocked.Increment(ref n);
            progress?.Report(($"Libraries ({i}/{allLibs.Count})", 0.05 + 0.25 * i / allLibs.Count));
        }, ct);
        classpath.AddRange(allLibs.Select(l => l.path));
        classpath.Add(clientJar);

        // Assets
        var assetIndex = vanilla["assetIndex"]!;
        var indexId = (string)assetIndex["id"]!;
        var indexPath = Path.Combine(AssetsDir, "indexes", indexId + ".json");
        await Downloader.DownloadVerifiedAsync((string)assetIndex["url"]!, indexPath, (string)assetIndex["sha1"]!, HashAlgorithmName.SHA1, ct);
        var objects = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, ct))!["objects"]!.AsObject()
            .Select(kv => (string)kv.Value!["hash"]!).Distinct().ToList();
        int a = 0;
        await Downloader.ForEachParallelAsync(objects, 16, async hash =>
        {
            var p = Path.Combine(AssetsDir, "objects", hash[..2], hash);
            if (!File.Exists(p)) // asset objects are content-addressed by sha1; verify on download
                await Downloader.DownloadVerifiedAsync(AssetBaseUrl + hash[..2] + "/" + hash, p, hash, HashAlgorithmName.SHA1, ct);
            var i = Interlocked.Increment(ref a);
            if (i % 50 == 0 || i == objects.Count)
                progress?.Report(($"Assets ({i}/{objects.Count})", 0.3 + 0.7 * i / objects.Count));
        }, ct);

        // Logging config
        string? loggingArg = null;
        var logging = vanilla["logging"]?["client"];
        if (logging is not null)
        {
            var file = logging["file"]!;
            var logPath = Path.Combine(AssetsDir, "log_configs", (string)file["id"]!);
            await Downloader.DownloadVerifiedAsync((string)file["url"]!, logPath, (string)file["sha1"]!, HashAlgorithmName.SHA1, ct);
            loggingArg = ((string)logging["argument"]!).Replace("${path}", logPath);
        }

        var natives = Path.Combine(VersionsDir, minecraft, "natives");
        Directory.CreateDirectory(natives);

        return new ResolvedGame
        {
            VersionId = minecraft,
            MainClass = (string)fabric["mainClass"]!,
            Classpath = classpath,
            VanillaJson = vanilla,
            FabricJson = fabric,
            AssetsRoot = AssetsDir,
            AssetIndexId = indexId,
            LibrariesDir = LibrariesDir,
            NativesDir = natives,
            LoggingArgument = loggingArg,
        };
    }

    async Task<JsonObject> LoadVanillaJsonAsync(string id, CancellationToken ct)
    {
        var local = Path.Combine(VersionsDir, id, id + ".json");
        try
        {
            var manifest = JsonNode.Parse(await Downloader.GetStringAsync(VersionManifestUrl, ct))!;
            var entry = manifest["versions"]!.AsArray().FirstOrDefault(v => (string?)v!["id"] == id)
                        ?? throw new InvalidDataException($"Minecraft {id} is not in Mojang's version list.");
            await Downloader.DownloadVerifiedAsync((string)entry["url"]!, local, (string)entry["sha1"]!, HashAlgorithmName.SHA1, ct);
        }
        catch (HttpRequestException) when (File.Exists(local))
        {
            // Offline: use the cached copy.
        }
        return JsonNode.Parse(await File.ReadAllTextAsync(local, ct))!.AsObject();
    }

    async Task<JsonObject> LoadFabricJsonAsync(string mc, string loader, CancellationToken ct)
    {
        var id = $"fabric-loader-{loader}-{mc}";
        var local = Path.Combine(VersionsDir, id, id + ".json");
        try
        {
            var json = await Downloader.GetStringAsync($"https://meta.fabricmc.net/v2/versions/loader/{mc}/{loader}/profile/json", ct);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);
            await File.WriteAllTextAsync(local, json, ct);
        }
        catch (HttpRequestException) when (File.Exists(local)) { }
        return JsonNode.Parse(await File.ReadAllTextAsync(local, ct))!.AsObject();
    }

    /// <summary>Fabric profile entries without a sha1 are checked against the Maven .sha1 file.</summary>
    static async Task DownloadWithMavenSha1Async(string url, string path, CancellationToken ct)
    {
        var sha1Path = path + ".sha1";
        string expected;
        if (File.Exists(sha1Path)) expected = (await File.ReadAllTextAsync(sha1Path, ct)).Trim().Split(' ')[0];
        else
        {
            expected = (await Downloader.GetStringAsync(url + ".sha1", ct)).Trim().Split(' ')[0];
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(sha1Path, expected, ct);
        }
        await Downloader.DownloadVerifiedAsync(url, path, expected, HashAlgorithmName.SHA1, ct);
    }

    /// <summary>"group:artifact:version[:classifier]" → "group/path/artifact/version/artifact-version[-classifier].jar"</summary>
    public static string MavenPath(string coords)
    {
        var parts = coords.Split(':');
        if (parts.Length < 3) throw new InvalidDataException($"Bad maven coordinates: {coords}");
        var (g, a, v) = (parts[0], parts[1], parts[2]);
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{g.Replace('.', '/')}/{a}/{v}/{a}-{v}{classifier}.jar";
    }

    static string LibraryKey(string coords)
    {
        var p = coords.Split(':');
        return p.Length > 3 ? $"{p[0]}:{p[1]}:{p[3]}" : $"{p[0]}:{p[1]}";
    }
}

/// <summary>Mojang library/argument rules, evaluated for Windows x64 with no optional features.</summary>
public static class Rules
{
    public static bool Allowed(JsonArray? rules, IReadOnlyDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0) return true;
        var allowed = false;
        foreach (var r in rules)
        {
            if (r is null) continue;
            var matches = true;
            if (r["os"] is JsonObject os)
            {
                if (os["name"] is JsonNode name && (string)name! != "windows") matches = false;
                if (os["arch"] is JsonNode arch && (string)arch! == "x86") matches = false; // we only run on x64
            }
            if (r["features"] is JsonObject fs)
            {
                foreach (var kv in fs)
                {
                    var want = (bool)kv.Value!;
                    var have = features != null && features.TryGetValue(kv.Key, out var v) && v;
                    if (want != have) matches = false;
                }
            }
            if (matches) allowed = (string)r["action"]! == "allow";
        }
        return allowed;
    }
}
