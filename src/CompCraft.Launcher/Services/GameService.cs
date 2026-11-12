using System.Diagnostics;
using System.IO;
using System.Net.Http;
using CmlLib.Core;
using CmlLib.Core.ProcessBuilder;

namespace CompCraft.Launcher.Services;

public enum GamePhase { Idle, Preparing, Installing, Mods, Launching, Running, Error }

/// <summary>Install (Fabric → Minecraft + Java → modpack) and launch, with one combined progress state for the UI.</summary>
public sealed class GameService(HttpClient http, AccountService accounts, ModpackService modpack, SettingsStore settings)
{
    private readonly MinecraftPath _path = new(AppPaths.GameRoot);
    private CancellationTokenSource? _cts;
    private Process? _process;

    public GamePhase Phase { get; private set; }
    public string Message { get; private set; } = "";
    public string Detail { get; private set; } = "";
    public double Progress { get; private set; }
    /// <summary>Shown once after a one-click install finishes, e.g. "Comp Craft is installed".</summary>
    public string? Notice { get; private set; }
    public bool CanCancel => Phase is GamePhase.Preparing or GamePhase.Installing or GamePhase.Mods;
    public event EventHandler? Changed;
    public event EventHandler? Launched;

    /// <summary>The one-click install: everything the game needs, without starting it. No sign-in required.</summary>
    public Task InstallAsync(bool resetDefaults = false) => RunAsync(async ct =>
    {
        await PrepareAsync(resetDefaults, ct);
        Notice = resetDefaults ? "Your settings were reset to the pack defaults." : "Everything is installed. Press Play!";
        Set(GamePhase.Idle, "");
    });

    public Task PlayAsync(ServerEntry? server) => RunAsync(async ct =>
    {
        if (accounts.Session is null) { Set(GamePhase.Error, "Sign in with Microsoft to play."); return; }
        var (launcher, version) = await PrepareAsync(false, ct);

        Set(GamePhase.Launching, "Starting Minecraft", .99);
        var s = settings.Current;
        var option = new MLaunchOption
        {
            Session = accounts.Session,
            MaximumRamMb = s.MemoryMb,
            MinimumRamMb = Math.Min(1024, s.MemoryMb),
            ScreenWidth = s.Width,
            ScreenHeight = s.Height,
            FullScreen = s.Fullscreen,
            GameLauncherName = "CompCraft",
            GameLauncherVersion = CyanLauncher.Core.AppInfo.Version,
            ExtraJvmArguments = string.IsNullOrWhiteSpace(s.JvmArguments)
                ? MLaunchOption.DefaultExtraJvmArguments
                : [.. MLaunchOption.DefaultExtraJvmArguments, MArgument.FromCommandLine(s.JvmArguments)],
        };
        if (server is not null) { option.ServerIp = server.Address; option.ServerPort = server.Port; }

        var process = await launcher.BuildProcessAsync(version, option, ct);
        process.EnableRaisingEvents = true;
        var started = DateTime.UtcNow;
        process.Exited += (_, _) =>
        {
            var code = SafeExitCode(process);
            _process = null;
            if (code != 0 && DateTime.UtcNow - started < TimeSpan.FromSeconds(20))
                Set(GamePhase.Error, $"Minecraft closed unexpectedly (code {code}). Open the logs folder for details.");
            else Set(GamePhase.Idle, "");
        };
        process.Start();
        _process = process;
        Set(GamePhase.Running, server is null ? "Minecraft is running" : $"Joining {server.Name}", 1);
        Launched?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Fabric profile, Minecraft + Java (CmlLib re-verifies hashes), then the modpack. Every step is incremental.</summary>
    private async Task<(MinecraftLauncher Launcher, string Version)> PrepareAsync(bool resetDefaults, CancellationToken ct)
    {
        Set(GamePhase.Preparing, "Checking for modpack updates", 0);
        await modpack.CheckAsync(ct);
        var pack = modpack.Pack ?? throw new InvalidOperationException("The modpack list could not be downloaded. Check your connection and try again.");

        Set(GamePhase.Preparing, "Preparing Fabric", 0);
        Directory.CreateDirectory(AppPaths.GameRoot);
        var version = await FabricInstaller.EnsureAsync(http, _path.Versions, pack.Minecraft, pack.FabricLoader, ct);

        var launcher = new MinecraftLauncher(_path);
        await launcher.GetAllVersionsAsync(ct); // Picks up the Fabric profile just written.
        var files = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(e =>
        {
            if (Phase != GamePhase.Installing) return;
            Detail = e.TotalTasks > 0 ? $"{e.ProgressedTasks:N0} / {e.TotalTasks:N0} files" : e.Name ?? "";
            Changed?.Invoke(this, EventArgs.Empty);
        });
        var bytes = new Progress<ByteProgress>(e =>
        {
            if (Phase != GamePhase.Installing || e.TotalBytes <= 0) return;
            Progress = Math.Clamp(e.ToRatio(), 0, 1) * .6;
            Changed?.Invoke(this, EventArgs.Empty);
        });
        Set(GamePhase.Installing, $"Installing Minecraft {pack.Minecraft}", 0);
        await launcher.InstallAsync(version, files, bytes, ct);

        Set(GamePhase.Mods, modpack.Installed ? $"Updating {pack.Name} {pack.PackVersion}" : $"Installing {pack.Name} {pack.PackVersion}", .6);
        await modpack.InstallAsync(new Progress<(int Done, int Total, string Name)>(p =>
        {
            Progress = .6 + .39 * p.Done / Math.Max(1, p.Total);
            Detail = p.Name;
            Changed?.Invoke(this, EventArgs.Empty);
        }), resetDefaults, ct);
        return (launcher, version);
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (Phase is not (GamePhase.Idle or GamePhase.Error)) return;
        Notice = null;
        _cts = new CancellationTokenSource();
        try { await work(_cts.Token); }
        catch (OperationCanceledException) { Set(GamePhase.Idle, ""); }
        catch (Exception ex) { Set(GamePhase.Error, ex is HttpRequestException ? "Download failed. Check your connection and try again." : ex.Message); }
        finally { _cts?.Dispose(); _cts = null; }
    }

    public void Cancel() => _cts?.Cancel();

    public void ForceStop()
    {
        try { _process?.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }

    public void DismissError()
    {
        Notice = null;
        if (Phase == GamePhase.Error) Set(GamePhase.Idle, "");
        else Changed?.Invoke(this, EventArgs.Empty);
    }

    private static int SafeExitCode(Process p) { try { return p.ExitCode; } catch (InvalidOperationException) { return 0; } }

    private void Set(GamePhase phase, string message, double? progress = null)
    {
        Phase = phase; Message = message; Detail = "";
        if (progress is { } value) Progress = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
