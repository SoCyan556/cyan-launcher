using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using CompCraft.Launcher.Services;
using CyanLauncher.Core;

namespace CompCraft.Launcher;

public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;
    private HttpClient? _http;

    /// <summary>True while a newer launcher is being downloaded; shown in the UI.</summary>
    public static bool Updating { get; private set; }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        // Same as ELIXR: WPF only draws the host; WebView2 keeps its own GPU pipeline for the UI and animations.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        if (e.Args.Contains("--uninstall")) { RunUninstall(); Shutdown(); return; }

        // Setup mode: the downloaded exe installs itself as an app, opens the installed copy and exits.
        if (!AppSetup.IsInstalledCopy && !AppSetup.IsDevBuild && !e.Args.Contains("--portable"))
        {
            try { AppSetup.Install(); }
            catch (Exception ex) { MessageBox.Show("Setup failed: " + ex.Message, AppSetup.AppName, MessageBoxButton.OK, MessageBoxImage.Error); }
            Shutdown();
            return;
        }

        SelfUpdater.CleanupOldVersion();
        if (AppSetup.IsInstalledCopy)
        {
            try { AppSetup.Register(); } catch (UnauthorizedAccessException) { }
        }

        _instance = new Mutex(true, "Local\\CompCraft.Launcher", out _ownsInstance);
        if (!_ownsInstance)
        {
            var existing = FindWindow(null, "Comp Craft");
            if (existing != IntPtr.Zero) { ShowWindowAsync(existing, 9); SetForegroundWindow(existing); }
            Shutdown();
            return;
        }

        var config = LauncherConfig.Load();
        var settings = new SettingsStore();
        settings.Load();
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"CompCraftLauncher/{AppInfo.Version}");
        var accounts = new AccountService();
        var modpack = new ModpackService(config);
        var game = new GameService(_http, accounts, modpack, settings);

        MainWindow = new MainWindow(_http, config, settings, accounts, modpack, game);
        MainWindow.Show();

        // Background work after the window is visible: restore the saved Microsoft account and check GitHub for updates.
        await accounts.RestoreAsync();
        await modpack.CheckAsync(CancellationToken.None);

        // A newer launcher on the latest release replaces this exe and restarts.
        if (modpack.Pack is { } pack && SelfUpdater.IsNewer(pack))
        {
            Updating = true;
            try
            {
                if (await SelfUpdater.UpdateAndRestartAsync(pack, CancellationToken.None, ReleaseInstanceLock)) { Shutdown(); return; }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // Keep running the current version; the update is retried on the next start.
            }
            Updating = false;
        }
    }

    private static void RunUninstall()
    {
        var answer = MessageBox.Show(
            $"Uninstall {AppSetup.AppName}?\n\nYour worlds and settings in\n{AppPaths.GameRoot}\nare kept unless you choose to delete them next.",
            AppSetup.AppName, MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK) return;
        var wipe = MessageBox.Show("Also delete the game folder (worlds, settings, downloaded files)?\n\nChoose No to keep your worlds.",
            AppSetup.AppName, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        AppSetup.Uninstall(wipe);
        MessageBox.Show($"{AppSetup.AppName} was removed.", AppSetup.AppName);
    }

    private void ReleaseInstanceLock()
    {
        if (!_ownsInstance) return;
        _instance?.ReleaseMutex();
        _ownsInstance = false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _http?.Dispose();
        ReleaseInstanceLock();
        _instance?.Dispose();
        base.OnExit(e);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
}
