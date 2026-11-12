using System.Diagnostics;
using System.Security.Cryptography;
using CyanLauncher.Core.Pack;

namespace CyanLauncher.Core;

/// <summary>
/// Updates the launcher exe in place. Windows lets a running exe be renamed (not deleted), so the
/// new build is downloaded and verified, the running exe is renamed to *.old, the new one takes its
/// name, and the launcher restarts. The *.old file is removed on the next start.
/// </summary>
public static class SelfUpdater
{
    public static bool IsNewer(PackManifest pack) =>
        pack.Launcher is { } l && Hashing.IsSha256(l.Sha256) && AppInfo.CompareVersions(l.Version, AppInfo.Version) > 0;

    public static void CleanupOldVersion()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        var old = exe + ".old";
        try { if (File.Exists(old)) File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Returns true when the new version was installed and started; the caller should exit.</summary>
    public static async Task<bool> UpdateAndRestartAsync(PackManifest pack, CancellationToken ct, Action? beforeRestart = null)
    {
        if (!IsNewer(pack)) return false;
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the launcher's own path.");
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return false; // running under `dotnet run` / tests: never replace the host

        var l = pack.Launcher!;
        var incoming = exe + ".new";
        await Downloader.DownloadVerifiedAsync(l.Url, incoming, l.Sha256, HashAlgorithmName.SHA256, ct);

        var old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(incoming, exe);
        }
        catch
        {
            File.Move(old, exe); // roll back
            throw;
        }
        beforeRestart?.Invoke(); // e.g. release the single-instance lock so the new version can start
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
        return true;
    }
}
