using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CompCraft.Launcher.Services;

/// <summary>
/// The Vue UI is compiled into the exe as ui.zip, so the launcher is one file that can replace itself on update.
/// It is unpacked once per UI build into the launcher data folder and served from there by WebView2.
/// </summary>
public static class UiAssets
{
    public static string EnsureExtracted()
    {
        using var resource = typeof(UiAssets).Assembly.GetManifestResourceStream("ui.zip")
            ?? throw new FileNotFoundException("The launcher UI is missing from this build. Rebuild the project so Vite can produce it.");
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var id = Convert.ToHexString(SHA256.HashData(buffer.ToArray()))[..16].ToLowerInvariant();

        var root = Path.Combine(AppPaths.LauncherData, "ui");
        var target = Path.Combine(root, id);
        if (!File.Exists(Path.Combine(target, "index.html")))
        {
            var staging = target + ".tmp";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            buffer.Position = 0;
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true)) zip.ExtractToDirectory(staging);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(staging, target);
        }

        // Older UI builds are no longer needed.
        foreach (var old in Directory.GetDirectories(root).Where(d => !string.Equals(d, target, StringComparison.OrdinalIgnoreCase)))
        {
            try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return target;
    }
}
