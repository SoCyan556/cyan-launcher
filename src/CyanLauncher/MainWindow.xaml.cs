using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CyanLauncher.Core;
using CyanLauncher.Core.Auth;
using CyanLauncher.Core.Pack;
using Microsoft.Win32;

namespace CyanLauncher;

public sealed record ModRow(string Path, string DisplayName, string? Version, bool ClientSide, bool Enabled, string Kind, string FileName);

public partial class MainWindow : Window
{
    readonly LauncherService _svc = new();
    PackManifest? _pack;
    GameSession? _session;
    CancellationTokenSource? _signInCts;
    DeviceCodePrompt? _prompt;
    bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettingsIntoForm();
        RefreshAccount();
        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    // ───────────────────────── Update / install ─────────────────────────

    bool _fromCache;

    /// <summary>
    /// On start: the launcher updates itself automatically; pack changes are shown on the main button
    /// ("Install" / "Update &amp; Play") and applied in one go when it is pressed.
    /// </summary>
    async Task CheckForUpdatesAsync()
    {
        await RunBusyAsync(async () =>
        {
            SetStatus("Checking for updates…", 0);
            var check = await _svc.CheckAsync(CancellationToken.None);
            _pack = check.Pack;
            _fromCache = check.FromCache;

            if (check.LauncherUpdateAvailable)
            {
                Log($"Launcher {check.Pack.Launcher!.Version} is available, updating…");
                SetStatus($"Updating launcher to {check.Pack.Launcher.Version}…", 0);
                if (await SelfUpdater.UpdateAndRestartAsync(check.Pack, CancellationToken.None))
                {
                    Application.Current.Shutdown();
                    return;
                }
            }
            if (check.InstalledPackVersion != null && check.PackUpdateAvailable)
                Log($"Pack update available: {check.InstalledPackVersion} → {check.Pack.PackVersion}. Press Update & Play.");
            SetStatus("Ready", 0);
            RefreshMods();
        });
        RefreshHeader();
    }

    bool PackInstalled => InstanceState.Load(_svc.GameDir).PackVersion != null;

    void RefreshHeader()
    {
        var installed = InstanceState.Load(_svc.GameDir).PackVersion;
        VersionText.Text = $"Pack {installed ?? "not installed"} · Launcher {AppInfo.Version}" + (_fromCache ? " · offline" : "");
        if (_pack is null)
        {
            UpdateText.Text = "Couldn't reach the update server. Check your internet connection and press Check for updates.";
            PlayButton.Content = "Install Cyan Civilization";
            return;
        }
        UpdateText.Text = $"Minecraft {_pack.Minecraft} · Fabric {_pack.FabricLoader} · Pack {_pack.PackVersion} · {_pack.Mods.Count()} mods";
        if (installed is null)
        {
            PlayButton.Content = "Install Cyan Civilization";
            UpdateText.Text += "\nOne click downloads everything: the mods, resource packs, shaders, Cyan settings, Minecraft and Java.";
        }
        else PlayButton.Content = PackInstaller.IsUpToDate(_pack, _svc.GameDir) ? "Play" : "Update & Play";
    }

    /// <summary>Installs/updates the pack and pre-downloads Minecraft, Fabric and Java, all in one go.</summary>
    async Task InstallEverythingAsync()
    {
        if (_pack is null)
        {
            var check = await _svc.CheckAsync(CancellationToken.None);
            _pack = check.Pack;
        }
        Log(PackInstalled ? $"Updating Cyan Civilization to {_pack.PackVersion}…" : $"Installing Cyan Civilization {_pack.PackVersion}…");
        await InstallAsync(resetDefaults: false);
        await _svc.PrepareGameAsync(_pack, ProgressSink(), CancellationToken.None);
        Log("Minecraft, Fabric and Java are ready.");
        RefreshHeader();
    }

    async Task InstallAsync(bool resetDefaults)
    {
        if (_pack is null) throw new InvalidOperationException("No pack information yet. Check for updates first.");
        var report = await _svc.InstallPackAsync(_pack, resetDefaults, ProgressSink(), CancellationToken.None);
        Log($"Pack {_pack.PackVersion}: {report.Written} files written, {report.AlreadyCurrent} already current, {report.Downloaded} downloaded.");
        if (report.Retired.Count > 0) Log($"Moved {report.Retired.Count} removed file(s) to {_svc.Paths.Retired}: {string.Join(", ", report.Retired)}");
        if (report.DefaultsWritten.Count > 0) Log($"Applied {report.DefaultsWritten.Count} Cyan default setting file(s).");
        if (!resetDefaults && report.DefaultsKept.Count > 0) Log($"Kept your existing settings in {report.DefaultsKept.Count} file(s).");
        RefreshMods();
    }

    async void Check_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    async void Repair_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(() => InstallAsync(resetDefaults: false));
        RefreshHeader();
    }

    async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "This replaces your options, keybinds and mod configs with the Cyan defaults.\n\nWorlds, waypoints, accounts and other personal data are not touched.\n\nReset to Cyan defaults?",
            "Reset to Cyan defaults", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await RunBusyAsync(() => InstallAsync(resetDefaults: true));
    }

    // ───────────────────────── Play ─────────────────────────

    async void Play_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async () =>
        {
            // First press installs everything; later presses apply any pack update, then play.
            if (!PackInstalled)
            {
                await InstallEverythingAsync();
                SetStatus("Cyan Civilization is installed. Press Play!", 1);
                return;
            }
            if (_pack is null || !PackInstaller.IsUpToDate(_pack, _svc.GameDir))
            {
                var check = await _svc.CheckAsync(CancellationToken.None);
                _pack = check.Pack;
                if (check.PackUpdateAvailable) await InstallAsync(resetDefaults: false);
                RefreshHeader();
            }

            var session = await GetSessionAsync();
            if (session is null) return;

            Log($"Starting Minecraft {_pack!.Minecraft} as {session.Username}{(session.IsOffline ? " (offline)" : "")}…");
            var proc = await _svc.LaunchAsync(_pack, session, ProgressSink(), CancellationToken.None);
            Log($"Minecraft is running. Game log: {Path.Combine(_svc.Paths.Logs, "game-latest.log")}");
            SetStatus("Minecraft is running", 1);
            if (_svc.Settings.CloseOnLaunch) { Application.Current.Shutdown(); return; }
            proc.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                Log($"Minecraft exited (code {proc.ExitCode}).");
                SetStatus("Ready", 0);
            });
        });
    }

    async Task<GameSession?> GetSessionAsync()
    {
        if (_session is { IsOffline: false }) return _session;
        if (!string.IsNullOrWhiteSpace(_svc.Settings.EffectiveClientId))
        {
            var auth = _svc.CreateAuth();
            SetStatus("Signing in…", 0);
            _session = await auth.TrySilentAsync(CancellationToken.None) ?? await InteractiveSignInAsync(auth);
            RefreshAccount();
            if (_session != null) return _session;
        }
        if (!string.IsNullOrWhiteSpace(_svc.Settings.OfflineUsername))
            return GameSession.Offline(_svc.Settings.OfflineUsername!);

        MessageBox.Show(this,
            string.IsNullOrWhiteSpace(_svc.Settings.EffectiveClientId)
                ? "Microsoft sign-in isn't set up in this launcher build yet.\n\nFor testing, set an offline name in Settings."
                : "Sign in with your Microsoft account to play.",
            "Cyan Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    // ───────────────────────── Account ─────────────────────────

    async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_svc.Settings.EffectiveClientId))
        {
            MessageBox.Show(this, "Microsoft sign-in isn't set up in this launcher build yet (no client ID).", "Cyan Launcher");
            return;
        }
        await RunBusyAsync(async () =>
        {
            var auth = _svc.CreateAuth();
            _session = await auth.TrySilentAsync(CancellationToken.None) ?? await InteractiveSignInAsync(auth);
            RefreshAccount();
        });
    }

    async Task<GameSession?> InteractiveSignInAsync(MicrosoftAuth auth)
    {
        _signInCts = new CancellationTokenSource();
        try
        {
            return await auth.SignInInteractiveAsync(p => Dispatcher.Invoke(() =>
            {
                _prompt = p;
                DeviceCodeText.Text = $"Go to {p.VerificationUri} and enter the code {p.UserCode}";
                DeviceCodePanel.Visibility = Visibility.Visible;
                Tabs.SelectedIndex = 0;
            }), _signInCts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("Sign-in cancelled.");
            return null;
        }
        finally
        {
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            _signInCts = null;
        }
    }

    void OpenDeviceLink_Click(object sender, RoutedEventArgs e)
    {
        if (_prompt is null) return;
        Clipboard.SetText(_prompt.UserCode);
        Process.Start(new ProcessStartInfo(_prompt.VerificationUri) { UseShellExecute = true });
    }

    void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_prompt != null) Clipboard.SetText(_prompt.UserCode);
    }

    void CancelSignIn_Click(object sender, RoutedEventArgs e) => _signInCts?.Cancel();

    void SignOut_Click(object sender, RoutedEventArgs e)
    {
        new AccountStore(_svc.Paths.AccountFile).Clear();
        _session = null;
        RefreshAccount();
    }

    void RefreshAccount()
    {
        var name = _session?.IsOffline == false ? _session.Username : _svc.SavedAccount?.Username;
        AccountText.Text = name is null ? "" : $"Signed in as {name}";
        SignInButton.Visibility = name is null ? Visibility.Visible : Visibility.Collapsed;
        SignOutButton.Visibility = name is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ───────────────────────── Mods tab ─────────────────────────

    void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && Tabs.SelectedIndex == 1) RefreshMods();
    }

    void RefreshMods()
    {
        if (_pack is null)
        {
            ModsHint.Text = "Mod list not loaded yet.";
            ModsGrid.ItemsSource = null;
            return;
        }
        var rows = ModManager.List(_pack, _svc.GameDir)
            .Select(m => new ModRow(m.Path, m.DisplayName, m.Version, m.ClientSide, m.Enabled,
                m.ClientSide ? "Client-side (optional)" : "Required", Path.GetFileName(m.Path)))
            .OrderByDescending(r => r.ClientSide).ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        ModsGrid.ItemsSource = rows;
        var optional = rows.Count(r => r.ClientSide);
        ModsHint.Text = optional == 0
            ? $"{rows.Count} mods. No mods are marked client-side in this pack version yet, so none can be turned off."
            : $"{rows.Count} mods. {optional} client-side mods can be turned on or off; the rest are required by the pack. Changes apply the next time you press Play.";
    }

    void ModToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not string path || _pack is null) return;
        try
        {
            ModManager.SetEnabled(_pack, _svc.GameDir, path, cb.IsChecked == true);
        }
        catch (Exception ex) when (ex is ModToggleException or IOException)
        {
            MessageBox.Show(this, ex.Message, "Can't change this mod", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        RefreshMods();
    }

    // ───────────────────────── Settings ─────────────────────────

    void LoadSettingsIntoForm()
    {
        var s = _svc.Settings;
        GameDirBox.Text = _svc.GameDir;
        MemoryBox.Text = s.MemoryMb?.ToString() ?? "";
        JavaBox.Text = s.JavaPath ?? "";
        ManifestBox.Text = s.ManifestUrl;
        ClientIdBox.Text = s.MicrosoftClientId ?? "";
        OfflineBox.Text = s.OfflineUsername ?? "";
        CloseOnLaunchBox.IsChecked = s.CloseOnLaunch;
    }

    void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        int? memory = null;
        if (!string.IsNullOrWhiteSpace(MemoryBox.Text))
        {
            if (!int.TryParse(MemoryBox.Text, out var mb) || mb < 1024 || mb > 65536)
            {
                MessageBox.Show(this, "Memory must be a number of MB between 1024 and 65536.", "Settings");
                return;
            }
            memory = mb;
        }
        var offline = OfflineBox.Text.Trim();
        if (offline.Length > 0 && (offline.Length > 16 || !offline.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            MessageBox.Show(this, "Offline names are 1–16 letters, digits or underscores.", "Settings");
            return;
        }
        var gameDir = GameDirBox.Text.Trim();
        _svc.SaveSettings(new LauncherSettings
        {
            GameDir = string.Equals(gameDir, _svc.Paths.DefaultGameDir, StringComparison.OrdinalIgnoreCase) || gameDir.Length == 0 ? null : gameDir,
            MemoryMb = memory,
            JavaPath = string.IsNullOrWhiteSpace(JavaBox.Text) ? null : JavaBox.Text.Trim(),
            ManifestUrl = string.IsNullOrWhiteSpace(ManifestBox.Text) ? ReleaseChannel.DefaultManifestUrl : ManifestBox.Text.Trim(),
            MicrosoftClientId = string.IsNullOrWhiteSpace(ClientIdBox.Text) ? null : ClientIdBox.Text.Trim(),
            OfflineUsername = offline.Length == 0 ? null : offline,
            CloseOnLaunch = CloseOnLaunchBox.IsChecked == true,
        });
        SetStatus("Settings saved", 0);
        RefreshMods();
    }

    void BrowseGameDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose the game folder (.minecraft)" };
        if (dlg.ShowDialog(this) == true) GameDirBox.Text = dlg.FolderName;
    }

    void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "javaw.exe|javaw.exe", Title = "Choose javaw.exe" };
        if (dlg.ShowDialog(this) == true) JavaBox.Text = dlg.FileName;
    }

    void OpenGameDir_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_svc.GameDir);
        Process.Start(new ProcessStartInfo(_svc.GameDir) { UseShellExecute = true });
    }

    // ───────────────────────── Helpers ─────────────────────────

    async Task RunBusyAsync(Func<Task> work)
    {
        if (_busy) return;
        _busy = true;
        PlayButton.IsEnabled = CheckButton.IsEnabled = RepairButton.IsEnabled = ResetButton.IsEnabled = false;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log("Error: " + ex.Message);
            SetStatus("Something went wrong — see the log on the Play tab.", 0);
        }
        finally
        {
            _busy = false;
            PlayButton.IsEnabled = CheckButton.IsEnabled = RepairButton.IsEnabled = ResetButton.IsEnabled = true;
        }
    }

    IProgress<(string, double)> ProgressSink() => new Progress<(string message, double fraction)>(p => SetStatus(p.message, p.fraction));

    void SetStatus(string text, double fraction)
    {
        StatusText.Text = text;
        Progress.Value = Math.Clamp(fraction, 0, 1);
    }

    void Log(string line)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
