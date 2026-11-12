using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyanLauncher.Core;
using CyanLauncher.Core.Pack;

// CyanPack — builds pack.json (and the blobs to upload) from a prepared .minecraft folder.
//
//   CyanPack build --source <.minecraft> --defaults <defaults dir> --pack-version 1.0.0 --out <dir>
//                  [--minecraft 1.21.1] [--fabric 0.19.5] [--java 21] [--memory 6144]
//                  [--overrides pack-overrides.json] [--blob-base-url <url>] [--modrinth]
//                  [--launcher-exe <CyanLauncher.exe> --launcher-version 0.1.0]
//
// Only mods/, resourcepacks/ and shaderpacks/ are read from --source. Settings come only from
// --defaults. Personal data (saves, accounts, waypoints, caches...) is refused either way.

return await Cli.RunAsync(args);

static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine("Usage: CyanPack build --source <.minecraft> --defaults <dir> --pack-version <v> --out <dir> [options]");
            Console.WriteLine("See README.md (Releasing) for every option.");
            return 0;
        }
        if (args[0] is not ("build" or "try")) { Console.Error.WriteLine($"Unknown command {args[0]}"); return 2; }
        var o = Parse(args.Skip(1).ToArray());
        try
        {
            if (args[0] == "build") await BuildAsync(o);
            else return await TryAsync(o);
            return 0;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException or HttpRequestException)
        {
            Console.Error.WriteLine("error: " + e.Message);
            return 1;
        }
    }

    static Dictionary<string, string?> Parse(string[] a)
    {
        var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument {a[i]}");
            var key = a[i][2..];
            if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) d[key] = a[++i];
            else d[key] = null; // flag
        }
        return d;
    }

    static string Req(Dictionary<string, string?> o, string k) =>
        o.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v! : throw new ArgumentException($"--{k} is required");

    static string? Opt(Dictionary<string, string?> o, string k) => o.TryGetValue(k, out var v) ? v : null;

    sealed class Overrides
    {
        /// <summary>Mod ids or pack paths ("mods/x.jar") that players may turn on/off.</summary>
        public List<string> ClientSide { get; set; } = new();
        /// <summary>Client-side mods that start disabled on a fresh install.</summary>
        public List<string> DefaultDisabled { get; set; } = new();
        /// <summary>Display-name overrides keyed by mod id or path.</summary>
        public Dictionary<string, string> Names { get; set; } = new();
    }

    static async Task BuildAsync(Dictionary<string, string?> o)
    {
        var source = Path.GetFullPath(Req(o, "source"));
        var defaults = Opt(o, "defaults") is { } dd ? Path.GetFullPath(dd) : null;
        var outDir = Path.GetFullPath(Req(o, "out"));
        var blobBase = Opt(o, "blob-base-url") ?? $"https://github.com/{ReleaseChannel.GitHubRepo}/releases/download/files/";
        if (!blobBase.EndsWith('/')) blobBase += "/";
        var overrides = Opt(o, "overrides") is { } ov && File.Exists(ov)
            ? JsonSerializer.Deserialize<Overrides>(File.ReadAllText(ov), PackManifest.JsonOptions) ?? new()
            : new Overrides();

        var pack = new PackManifest
        {
            PackVersion = Req(o, "pack-version"),
            Minecraft = Opt(o, "minecraft") ?? "1.21.1",
            FabricLoader = Opt(o, "fabric") ?? "0.19.5",
            JavaMajor = int.Parse(Opt(o, "java") ?? "21"),
            RecommendedMemoryMb = int.Parse(Opt(o, "memory") ?? "6144"),
        };

        var local = new Dictionary<PackFile, string>(); // file -> local source path
        void Add(string full, string rel, PackFileKind kind)
        {
            if (ExcludedPaths.IsExcluded(rel)) { Console.WriteLine($"  skip (personal/generated): {rel}"); return; }
            SafePath.EnsureRelative(rel);
            var f = new PackFile { Path = rel, Kind = kind, Sha256 = Hashing.Sha256File(full), Size = new FileInfo(full).Length };
            pack.Files.Add(f);
            local[f] = full;
        }

        // Mods
        var modsDir = Path.Combine(source, "mods");
        foreach (var jar in Directory.EnumerateFiles(modsDir, "*.jar").Order(StringComparer.OrdinalIgnoreCase))
            Add(jar, "mods/" + Path.GetFileName(jar), PackFileKind.Mod);
        foreach (var dis in Directory.EnumerateFiles(modsDir, "*.disabled"))
            Console.WriteLine($"  warning: {Path.GetFileName(dis)} is disabled in the source and was not included.");
        // Resource packs (zips and folder packs) and shader packs
        foreach (var (dir, kind) in new[] { ("resourcepacks", PackFileKind.ResourcePack), ("shaderpacks", PackFileKind.ShaderPack) })
        {
            var root = Path.Combine(source, dir);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var rel = SafePath.ToRelative(source, file);
                // Shader option .txt files next to shader zips are player settings, not pack content.
                if (kind == PackFileKind.ShaderPack && rel.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                Add(file, rel, kind);
            }
        }
        // Cyan defaults
        if (defaults != null)
        {
            foreach (var file in Directory.EnumerateFiles(defaults, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                Add(file, SafePath.ToRelative(defaults, file), PackFileKind.Default);
        }

        // Mod metadata + client-side marks
        var infos = pack.Mods.ToDictionary(m => m, m => FabricModInfo.Read(local[m]));
        var providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (m, info) in infos)
        {
            if (info is null) continue;
            providers[info.Id] = info.Id;
            foreach (var p in info.Provides) providers.TryAdd(p, info.Id);
        }
        bool Listed(List<string> list, PackFile m, FabricModInfo? i) =>
            list.Contains(m.Path, StringComparer.OrdinalIgnoreCase) || (i != null && list.Contains(i.Id, StringComparer.OrdinalIgnoreCase));
        foreach (var (m, info) in infos)
        {
            m.ModId = info?.Id;
            m.ModVersion = info?.Version;
            m.Name = (info != null && overrides.Names.TryGetValue(info.Id, out var n)) || overrides.Names.TryGetValue(m.Path, out n) ? n : info?.Name;
            m.DeclaredEnvironment = info?.Environment;
            m.Depends = info?.Depends.Where(providers.ContainsKey).Select(d => providers[d]).Distinct().ToList();
            if (m.Depends is { Count: 0 }) m.Depends = null;
            m.ClientSide = Listed(overrides.ClientSide, m, info);
            m.DefaultEnabled = !Listed(overrides.DefaultDisabled, m, info);
        }

        // Download URLs: Modrinth CDN when the exact file is published there, else our own blob.
        var upstream = o.ContainsKey("modrinth")
            ? await LookupModrinthAsync(pack.Files.Where(f => f.Kind is PackFileKind.Mod or PackFileKind.ResourcePack or PackFileKind.ShaderPack).ToList(), local)
            : new();
        Directory.CreateDirectory(outDir);
        var blobs = Path.Combine(outDir, "blobs");
        Directory.CreateDirectory(blobs);
        var uploads = new List<string>();
        foreach (var f in pack.Files)
        {
            if (upstream.TryGetValue(f, out var url)) { f.Url = url; continue; }
            f.Url = blobBase + f.Sha256;
            var blob = Path.Combine(blobs, f.Sha256);
            if (!File.Exists(blob)) File.Copy(local[f], blob);
            uploads.Add(f.Sha256);
        }

        if (Opt(o, "launcher-exe") is { } exe)
        {
            var sha = Hashing.Sha256File(exe);
            File.Copy(exe, Path.Combine(blobs, sha), true);
            uploads.Add(sha);
            pack.Launcher = new LauncherRelease
            {
                Version = Req(o, "launcher-version"), Sha256 = sha, Size = new FileInfo(exe).Length, Url = blobBase + sha,
            };
        }

        pack.Validate();
        await File.WriteAllTextAsync(Path.Combine(outDir, "pack.json"), pack.ToJson());
        await File.WriteAllLinesAsync(Path.Combine(outDir, "blobs-to-upload.txt"), uploads.Distinct());

        Console.WriteLine();
        Console.WriteLine($"pack.json {pack.PackVersion}: {pack.Files.Count} files");
        foreach (var g in pack.Files.GroupBy(f => f.Kind)) Console.WriteLine($"  {g.Key,-13} {g.Count(),4}");
        Console.WriteLine($"  from Modrinth {upstream.Count,4}, own blobs {uploads.Distinct().Count(),4}");
        var marked = pack.Mods.Count(m => m.ClientSide);
        Console.WriteLine(marked == 0
            ? "  client-side mods: none marked yet (add ids to \"clientSide\" in pack-overrides.json)"
            : $"  client-side mods: {marked} marked");
        var declared = pack.Mods.Where(m => m.DeclaredEnvironment == "client" && !m.ClientSide).Select(m => m.ModId ?? m.Path).ToList();
        if (declared.Count > 0)
            Console.WriteLine($"  (informational) mods whose own metadata says client-only: {string.Join(", ", declared)}");
        Console.WriteLine($"Wrote {Path.Combine(outDir, "pack.json")}");
    }


    /// <summary>
    /// try --pack pack.json --data &lt;scratch .minecraft&gt;
    /// Installs a pack exactly as the launcher does into a throwaway folder, twice (the second run must change nothing).
    /// </summary>
    static async Task<int> TryAsync(Dictionary<string, string?> o)
    {
        var pack = PackManifest.Parse(await File.ReadAllTextAsync(Req(o, "pack")));
        var data = Path.GetFullPath(Req(o, "data"));
        var gameDir = Path.Combine(data, ".minecraft");
        var installer = new PackInstaller(Path.Combine(data, "cache"), Path.Combine(data, "retired"));
        var last = "";
        var progress = new Progress<(string m, double f)>(p => { if (p.m != last) { last = p.m; Console.WriteLine("  " + p.m); } });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await installer.InstallAsync(pack, gameDir, false, progress);
        Console.WriteLine($"install: written {r.Written}, current {r.AlreadyCurrent}, downloaded {r.Downloaded}, defaults {r.DefaultsWritten.Count}, retired {r.Retired.Count} ({sw.Elapsed.TotalSeconds:F1}s)");
        Console.WriteLine($"up to date: {PackInstaller.IsUpToDate(pack, gameDir)}");
        sw.Restart();
        var r2 = await installer.InstallAsync(pack, gameDir, false);
        Console.WriteLine($"second run (no changes): written {r2.Written}, current {r2.AlreadyCurrent} ({sw.Elapsed.TotalSeconds:F1}s)");
        return r2.Written == 0 ? 0 : 1;
    }

    /// <summary>
    /// Finds exact-file matches on Modrinth by SHA-1 so files download from Modrinth's CDN, and fills in each
    /// project's description, icon and page for the launcher's Modpack page.
    /// </summary>
    static async Task<Dictionary<PackFile, string>> LookupModrinthAsync(List<PackFile> files, Dictionary<PackFile, string> local)
    {
        // Identical files can appear at several paths; one lookup serves all of them.
        var bySha1 = files.GroupBy(m => Hashing.Sha1File(local[m]), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.modrinth.com/v2/version_files")
        {
            Content = JsonContent.Create(new { hashes = bySha1.Keys.ToArray(), algorithm = "sha1" }),
        };
        using var resp = await Downloader.Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync())!.AsObject();
        var result = new Dictionary<PackFile, string>();
        var byProject = new Dictionary<string, List<PackFile>>();
        foreach (var (sha1, version) in json)
        {
            if (!bySha1.TryGetValue(sha1, out var same) || version is null) continue;
            var file = version["files"]!.AsArray().FirstOrDefault(f => string.Equals((string?)f!["hashes"]?["sha1"], sha1, StringComparison.OrdinalIgnoreCase));
            if (file?["url"] is not JsonNode url) continue;
            foreach (var m in same) result[m] = (string)url!;
            var projectId = (string?)version["project_id"];
            if (projectId is null) continue;
            if (!byProject.TryGetValue(projectId, out var list)) byProject[projectId] = list = new();
            list.AddRange(same);
        }

        // Project details (title, description, icon) in batches.
        foreach (var chunk in byProject.Keys.Chunk(80))
        {
            var ids = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(chunk));
            var projects = JsonNode.Parse(await Downloader.Http.GetStringAsync($"https://api.modrinth.com/v2/projects?ids={ids}"))!.AsArray();
            foreach (var p in projects)
            {
                if (p is null || !byProject.TryGetValue((string)p["id"]!, out var targets)) continue;
                var type = (string?)p["project_type"] ?? "mod";
                foreach (var t in targets)
                {
                    t.Description ??= (string?)p["description"];
                    t.IconUrl ??= (string?)p["icon_url"];
                    t.ProjectUrl ??= $"https://modrinth.com/{type}/{(string?)p["slug"]}";
                    if (t.Kind != PackFileKind.Mod || t.Name is null) t.Name ??= (string?)p["title"];
                }
            }
        }
        return result;
    }
}
