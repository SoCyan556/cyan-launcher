using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CompCraft.Launcher.Services;
using Microsoft.Web.WebView2.Core;

namespace CompCraft.Launcher;

/// <summary>
/// Frameless host for the Vue UI (WebView2), following the ELIXR layout: C# owns every service,
/// the page only sends small typed commands over postMessage and receives a single state snapshot.
/// </summary>
public partial class MainWindow : Window
{
    private const string Host = "app.compcraft.local";
    private const string Origin = "https://" + Host;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private readonly HttpClient _http;
    private readonly LauncherConfig _config;
    private readonly SettingsStore _settings;
    private readonly AccountService _accounts;
    private readonly ModpackService _modpack;
    private readonly GameService _game;
    private readonly List<ServerEntry> _servers;
    private readonly Dictionary<string, ServerStatus> _serverStatus = [];
    private readonly int _systemMemoryMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
    private bool _ready, _pushQueued, _pinging;

    public MainWindow(HttpClient http, LauncherConfig config, SettingsStore settings, AccountService accounts, ModpackService modpack, GameService game)
    {
        InitializeComponent();
        (_http, _config, _settings, _accounts, _modpack, _game) = (http, config, settings, accounts, modpack, game);
        _servers = ReadConfigList<ServerEntry>("servers.json");

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            int dark = 1, round = 2; // DWMWCP_ROUND: native Windows 11 rounded corners on the frameless window.
            DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
        };
        StateChanged += (_, _) =>
        {
            // A maximized WindowChrome window overhangs the screen by its resize border; inset the page to compensate.
            Browser.Margin = WindowState == WindowState.Maximized ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
            QueuePush();
        };
        Loaded += InitializeAsync;

        _accounts.Changed += (_, _) => QueuePush();
        _modpack.Changed += (_, _) => QueuePush();
        _game.Changed += (_, _) => QueuePush();
        _game.Launched += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (_settings.Current.AfterLaunch == "minimize") WindowState = WindowState.Minimized;
            else if (_settings.Current.AfterLaunch == "close") Close();
        });
    }

    private async void InitializeAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = UiAssets.EnsureExtracted();
            CoreWebView2EnvironmentOptions? options = null;
#if DEBUG
            // Debug builds only: COMPCRAFT_DEVTOOLS_PORT exposes the page to local DevTools for UI checks and screenshots.
            if (int.TryParse(Environment.GetEnvironmentVariable("COMPCRAFT_DEVTOOLS_PORT"), out var devtools))
                options = new CoreWebView2EnvironmentOptions($"--remote-debugging-port={devtools}");
#endif
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.LauncherData, "WebView2"), options);
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsNonClientRegionSupportEnabled = true; // CSS app-region: drag acts as the title bar (snap, double-click maximize).
#if !DEBUG
            core.Settings.AreDevToolsEnabled = false;
#endif
            core.SetVirtualHostNameToFolderMapping(Host, content, CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, args) => { if (!IsTrusted(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.WebMessageReceived += OnMessage;
            core.Navigate(Origin + "/index.html");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            var missingRuntime = ex is WebView2RuntimeNotFoundException;
            MessageBox.Show(missingRuntime
                ? "Comp Craft needs the Microsoft Edge WebView2 Runtime. Install it from Microsoft and start the launcher again."
                : "The launcher could not start.\n\n" + ex.Message, "Comp Craft", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsTrusted(e.Source) || e.WebMessageAsJson.Length > 8192) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    _ready = true; Push();
                    _ = PingServersAsync();
                    break;
                case "window.minimize": WindowState = WindowState.Minimized; break;
                case "window.maximize": WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; break;
                case "window.close": Close(); break;
                case "window.resize" when root.TryGetProperty("edge", out var edge): BeginResize(edge.GetString()); break;
                case "auth.signIn": await _accounts.SignInAsync(); break;
                case "auth.signOut": await _accounts.SignOutAsync(); break;
                case "auth.dismiss": _accounts.DismissError(); break;
                case "game.play": await _game.PlayAsync(_servers.FirstOrDefault(s => s.Id == id)); break;
                case "game.install":
                case "game.repair": await _game.InstallAsync(); break;
                case "modpack.toggle" when root.TryGetProperty("path", out var modPath) && root.TryGetProperty("enabled", out var on):
                    try { _modpack.SetEnabled(modPath.GetString() ?? "", on.GetBoolean()); }
                    catch (CyanLauncher.Core.Pack.ModToggleException ex) { Post(new { type = "notice", message = ex.Message }); }
                    Push();
                    break;
                case "modpack.resetDefaults":
                    if (MessageBox.Show(this,
                            "Replace your game settings, keybinds and mod configs with the pack defaults?\n\nWorlds, waypoints and accounts are not touched.",
                            "Reset to pack defaults", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes)
                        await _game.InstallAsync(resetDefaults: true);
                    break;
                case "game.cancel": _game.Cancel(); break;
                case "game.stop": _game.ForceStop(); break;
                case "game.dismiss": _game.DismissError(); break;
                case "modpack.refresh": await _modpack.CheckAsync(CancellationToken.None); break;
                case "servers.refresh": await PingServersAsync(); break;
                case "folder.open": OpenFolder(id); break;
                case "link.open" when root.TryGetProperty("url", out var url): OpenLink(url.GetString()); break;
                case "settings.set" when root.TryGetProperty("key", out var key) && root.TryGetProperty("value", out var value):
                    if (_settings.With(key.GetString() ?? "", value, _systemMemoryMb) is { } updated) await _settings.SaveAsync(updated);
                    Push();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Post(new { type = "notice", message = "That action could not be completed." });
        }
    }

    private void QueuePush()
    {
        // Download progress fires many times per second; coalesce into one snapshot per frame-ish.
        Dispatcher.BeginInvoke(() =>
        {
            if (_pushQueued) return;
            _pushQueued = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
            timer.Tick += (_, _) => { timer.Stop(); _pushQueued = false; Push(); };
            timer.Start();
        });
    }

    private void Push() => Post(new
    {
        type = "state",
        payload = new
        {
            pack = new
            {
                _config.Name, _config.Tagline, _config.Description,
                PackName = _modpack.Pack?.Name,
                ModpackVersion = _modpack.Pack?.PackVersion ?? _modpack.InstalledVersion ?? "",
                MinecraftVersion = _modpack.Pack?.Minecraft ?? "1.21.1",
                Loader = "Fabric " + (_modpack.Pack?.FabricLoader ?? ""),
            },
            settings = _settings.Current,
            system = new { MemoryMb = _systemMemoryMb },
            account = new { SignedIn = _accounts.Session is not null, _accounts.Session?.Username, Uuid = _accounts.Session?.UUID, _accounts.Busy, _accounts.Error },
            game = new { _game.Phase, _game.Message, _game.Detail, _game.Progress, _game.CanCancel, _game.Notice },
            modpack = new { _modpack.Status, _modpack.Mods, _modpack.Installed, _modpack.InstalledVersion, _modpack.UpdateAvailable },
            launcher = new { Version = CyanLauncher.Core.AppInfo.Version, Updating = App.Updating },
            servers = _servers.Select(s => new { s.Id, s.Name, s.Address, s.Port, s.Description, Status = _serverStatus.GetValueOrDefault(s.Id) }),
            serversBusy = _pinging,
            window = new { Maximized = WindowState == WindowState.Maximized },
        },
    });

    private void Post(object payload)
    {
        if (_ready && Browser.CoreWebView2 is { } core && IsTrusted(core.Source)) core.PostWebMessageAsJson(JsonSerializer.Serialize(payload, _json));
    }

    private async Task PingServersAsync()
    {
        if (_pinging) return;
        _pinging = true; Push();
        try
        {
            foreach (var result in await Task.WhenAll(_servers.Select(s => ServerPing.PingAsync(s, CancellationToken.None))))
                _serverStatus[result.Id] = result;
        }
        finally { _pinging = false; Push(); }
    }

    private static void OpenFolder(string? which)
    {
        var path = which switch
        {
            "game" => AppPaths.GameRoot,
            "mods" => AppPaths.Mods,
            "logs" => Path.Combine(AppPaths.GameRoot, "logs"),
            "screenshots" => Path.Combine(AppPaths.GameRoot, "screenshots"),
            "resourcepacks" => Path.Combine(AppPaths.GameRoot, "resourcepacks"),
            "shaderpacks" => Path.Combine(AppPaths.GameRoot, "shaderpacks"),
            _ => null,
        };
        if (path is null) return;
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private static void OpenLink(string? url)
    {
        // Only real web pages are handed to the default browser.
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private void BeginResize(string? edge)
    {
        var hit = edge switch { "l" => 10, "r" => 11, "t" => 12, "tl" => 13, "tr" => 14, "b" => 15, "bl" => 16, "br" => 17, _ => 0 };
        if (hit == 0 || WindowState != WindowState.Normal) return;
        var handle = new WindowInteropHelper(this).Handle;
        ReleaseCapture();
        SendMessage(handle, 0x00A1 /* WM_NCLBUTTONDOWN */, hit, IntPtr.Zero);
    }

    private static List<T> ReadConfigList<T>(string name)
    {
        try
        {
            var json = EmbeddedConfig.Read(name);
            return json is null ? [] : JsonSerializer.Deserialize<List<T>>(json, AppPaths.Json) ?? [];
        }
        catch (JsonException) { return []; }
    }

    private static bool IsTrusted(string uri) => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.GetLeftPart(UriPartial.Authority) == Origin;

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, int wParam, IntPtr lParam);
}
