using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CyanLauncher.Core.Game;

/// <summary>
/// Finds Java for the game. A user-set path wins; otherwise the launcher keeps its own Eclipse Temurin
/// JRE (downloaded from the Adoptium API and checksum-verified) so nothing depends on system Java.
/// </summary>
public sealed class JavaRuntime
{
    readonly string _javaRoot;
    public JavaRuntime(string javaRoot) => _javaRoot = javaRoot;

    public async Task<string> EnsureAsync(int major, string? overridePath,
        IProgress<(string message, double fraction)>? progress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (!File.Exists(overridePath)) throw new FileNotFoundException("The Java path in Settings does not exist.", overridePath);
            return overridePath;
        }

        var dir = Path.Combine(_javaRoot, major.ToString());
        var javaw = Path.Combine(dir, "bin", "javaw.exe");
        if (File.Exists(javaw)) return javaw;

        progress?.Report(($"Downloading Java {major}", 0));
        var api = $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
        var list = JsonNode.Parse(await Downloader.GetStringAsync(api, ct))!.AsArray();
        var pkg = list.Select(x => x!["binary"]!["package"]!)
                      .FirstOrDefault(p => ((string)p["name"]!).EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidDataException($"Adoptium has no Windows x64 JRE {major} zip.");

        var zip = Path.Combine(_javaRoot, (string)pkg["name"]!);
        await Downloader.DownloadVerifiedAsync((string)pkg["link"]!, zip, (string)pkg["checksum"]!, HashAlgorithmName.SHA256, ct);

        progress?.Report(($"Unpacking Java {major}", 0.9));
        var staging = Path.Combine(_javaRoot, $"{major}-staging");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        ZipFile.ExtractToDirectory(zip, staging);
        var home = Directory.GetDirectories(staging).FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "javaw.exe")))
                   ?? throw new InvalidDataException("The Java download did not contain bin/javaw.exe.");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(home, dir);
        Directory.Delete(staging, true);
        File.Delete(zip);
        return javaw;
    }
}
