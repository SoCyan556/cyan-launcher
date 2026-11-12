using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CompCraft.Launcher.Services;

/// <summary>
/// One-click, per-user install (no admin rights). The same exe is both the setup and the app:
/// run from anywhere else, it copies itself to %LOCALAPPDATA%\Programs\CompCraft, creates Start menu and
/// desktop shortcuts, registers in Settings → Apps (with Uninstall) and starts the installed copy.
/// "--uninstall" reverses it.
/// </summary>
public static class AppSetup
{
    public const string AppName = "Comp Craft";
    const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CompCraftLauncher";

    public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CompCraft");
    public static string InstalledExe => Path.Combine(InstallDir, "CompCraft.exe");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
    static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk");

    static string? CurrentExe => Environment.ProcessPath;

    public static bool IsInstalledCopy =>
        CurrentExe is { } exe && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>True under `dotnet run`/the debugger/build output, where setup must not kick in.</summary>
    public static bool IsDevBuild =>
        CurrentExe is null || Debugger.IsAttached ||
        CurrentExe.Contains(Path.Combine("bin", "Debug"), StringComparison.OrdinalIgnoreCase) ||
        CurrentExe.Contains(Path.Combine("bin", "Release"), StringComparison.OrdinalIgnoreCase);

    /// <summary>Copies this exe into place, creates shortcuts and the Apps entry, and starts the installed copy.</summary>
    public static void Install()
    {
        var source = CurrentExe ?? throw new InvalidOperationException("Cannot find the setup file's path.");
        Directory.CreateDirectory(InstallDir);

        // An already-running copy can't be overwritten; close it first.
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InstalledExe)))
        {
            try
            {
                if (p.Id != Environment.ProcessId && string.Equals(p.MainModule?.FileName, InstalledExe, StringComparison.OrdinalIgnoreCase))
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(5000)) p.Kill();
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }

        var temp = InstalledExe + ".setup";
        File.Copy(source, temp, true);
        File.Move(temp, InstalledExe, true);

        CreateShortcut(StartMenuLink);
        CreateShortcut(DesktopLink);
        Register();

        Process.Start(new ProcessStartInfo(InstalledExe, "--installed") { UseShellExecute = false, WorkingDirectory = InstallDir });
    }

    /// <summary>Writes/refreshes the Settings → Apps entry (also on every start, so the version stays current after self-updates).</summary>
    public static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", CyanLauncher.Core.AppInfo.Version);
        key.SetValue("Publisher", "Cyan");
        key.SetValue("DisplayIcon", InstalledExe + ",0");
        key.SetValue("InstallLocation", InstallDir);
        key.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        if (File.Exists(InstalledExe))
            key.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
    }

    /// <summary>Removes the app. The game folder (worlds, settings) is kept unless removeGameData is true.</summary>
    public static void Uninstall(bool removeGameData)
    {
        foreach (var link in new[] { StartMenuLink, DesktopLink })
            if (File.Exists(link)) File.Delete(link);
        Registry.CurrentUser.DeleteSubKeyTree(RegKey, false);

        var targets = new List<string> { InstallDir };
        if (removeGameData) targets.Add(AppPaths.GameRoot);
        // The running exe can't delete itself: a short-lived cmd removes the folders after we exit.
        var cmd = "/c timeout /t 2 /nobreak >nul & " + string.Join(" & ", targets.Select(t => $"rmdir /s /q \"{t}\""));
        Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    static void CreateShortcut(string linkPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic link = shell.CreateShortcut(linkPath);
            link.TargetPath = InstalledExe;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = InstalledExe + ",0";
            link.Description = "Install, update and play the Comp Craft modpack";
            link.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
