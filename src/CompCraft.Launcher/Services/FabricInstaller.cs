using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CompCraft.Launcher.Services;

/// <summary>
/// Installs Fabric by writing its launcher profile (from meta.fabricmc.net) into the versions folder.
/// CmlLib then resolves the profile's inheritsFrom, libraries and assets like any other version.
/// </summary>
public static class FabricInstaller
{
    private const string Meta = "https://meta.fabricmc.net/v2/versions/loader/";

    public static async Task<string> EnsureAsync(HttpClient http, string versionsDirectory, string minecraft, string loader, CancellationToken ct)
    {
        if (string.Equals(loader, "latest", StringComparison.OrdinalIgnoreCase))
            loader = await LatestStableAsync(http, minecraft, versionsDirectory, ct);

        var id = $"fabric-loader-{loader}-{minecraft}";
        var file = Path.Combine(versionsDirectory, id, id + ".json");
        if (File.Exists(file)) return id;

        var profile = await http.GetStringAsync($"{Meta}{Uri.EscapeDataString(minecraft)}/{Uri.EscapeDataString(loader)}/profile/json", ct);
        using (JsonDocument.Parse(profile)) { } // Refuse to write anything that is not JSON.
        await AppPaths.WriteAtomicAsync(file, profile, ct);
        return id;
    }

    private static async Task<string> LatestStableAsync(HttpClient http, string minecraft, string versionsDirectory, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await http.GetStringAsync(Meta + Uri.EscapeDataString(minecraft), ct));
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var loader = entry.GetProperty("loader");
                if (loader.GetProperty("stable").GetBoolean()) return loader.GetProperty("version").GetString()!;
            }
        }
        catch (HttpRequestException)
        {
            // Offline: reuse any Fabric profile already installed for this Minecraft version.
            var suffix = "-" + minecraft;
            var existing = Directory.Exists(versionsDirectory)
                ? Directory.GetDirectories(versionsDirectory, "fabric-loader-*" + suffix).Select(Path.GetFileName).OrderDescending().FirstOrDefault()
                : null;
            if (existing is not null) return existing["fabric-loader-".Length..^suffix.Length];
            throw;
        }
        throw new InvalidOperationException("No stable Fabric Loader is available for Minecraft " + minecraft + ".");
    }
}
