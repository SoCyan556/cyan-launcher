using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyanLauncher.Core.Pack;

/// <summary>Reads id, name, version, environment and dependencies from a jar's fabric.mod.json.</summary>
public sealed record FabricModInfo(string Id, string? Name, string? Version, string? Environment, IReadOnlyList<string> Depends, IReadOnlyList<string> Provides)
{
    static readonly HashSet<string> PlatformIds = new(StringComparer.OrdinalIgnoreCase)
        { "minecraft", "java", "fabricloader", "fabric-loader", "fabric" };

    public static FabricModInfo? Read(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry("fabric.mod.json");
            if (entry is null) return null;
            using var r = new StreamReader(entry.Open());
            var json = JsonNode.Parse(r.ReadToEnd(), documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            })!;
            var depends = (json["depends"] as JsonObject)?.Select(kv => kv.Key).Where(k => !PlatformIds.Contains(k)).ToList() ?? new();
            var provides = (json["provides"] as JsonArray)?.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList() ?? new();
            return new FabricModInfo((string)json["id"]!, (string?)json["name"], (string?)json["version"], (string?)json["environment"], depends, provides);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
