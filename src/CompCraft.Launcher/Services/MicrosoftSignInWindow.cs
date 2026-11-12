using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using XboxAuthNet.OAuth.CodeFlow;

namespace CompCraft.Launcher.Services;

/// <summary>
/// Microsoft sign-in page shown in a WPF window with its own WebView2 profile.
/// Replaces XboxAuthNet's default WinForms dialog, which runs on a separate STA thread and never appears inside this WPF/WebView2 host.
/// </summary>
public sealed class WpfWebUI : IWebUI
{
    public static string ProfileFolder { get; } = Path.Combine(AppPaths.LauncherData, "WebView2-SignIn");

    public Task<CodeFlowAuthorizationResult> DisplayDialogAndInterceptUri(Uri uri, ICodeFlowUrlChecker uriChecker, CancellationToken cancellationToken) =>
        OnUiThread(() => SignInWindow.ShowAsync(uri, uriChecker, cancellationToken));

    public Task DisplayDialogAndNavigateUri(Uri uri, CancellationToken cancellationToken) =>
        OnUiThread(async () =>
        {
            // Sign-out page: closing (automatically or by the user) is the normal way it ends.
            try { await SignInWindow.ShowAsync(uri, null, cancellationToken); } catch (OperationCanceledException) { }
            return true;
        });

    // The login handler continues on thread-pool threads, so always hop back to the WPF dispatcher.
    private static Task<T> OnUiThread<T>(Func<Task<T>> action) => Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
}

internal sealed class SignInWindow : Window
{
    private readonly WebView2 _browser = new() { DefaultBackgroundColor = System.Drawing.Color.FromArgb(20, 17, 16) };
    private readonly TaskCompletionSource<CodeFlowAuthorizationResult> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ICodeFlowUrlChecker? _checker;

    private SignInWindow(Uri start, ICodeFlowUrlChecker? checker)
    {
        _checker = checker;
        Title = checker is null ? "Signing out — Comp Craft" : "Sign in with Microsoft — Comp Craft";
        Width = 500; Height = 680; MinWidth = 420; MinHeight = 520;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 17, 16));
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        Content = _browser;
        if (Application.Current.MainWindow is { IsVisible: true } owner && owner != this)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;

        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
        Loaded += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, WpfWebUI.ProfileFolder);
                await _browser.EnsureCoreWebView2Async(environment);
                var core = _browser.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); }; // "Create account" etc. stay in this window.
                core.NavigationStarting += OnNavigationStarting;
                if (checker is null) core.NavigationCompleted += async (_, _) => { await Task.Delay(600); Close(); };
                core.Navigate(start.AbsoluteUri);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _result.TrySetException(new InvalidOperationException("The sign-in window could not start: " + ex.Message, ex));
                Close();
            }
        };
        Closed += (_, _) =>
        {
            // Closing the window without finishing is a cancel, which the launcher treats silently.
            _result.TrySetCanceled();
            _browser.Dispose();
            Owner?.Activate();
        };
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_checker is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;
        var result = _checker.GetAuthCodeResult(uri);
        if (result.IsEmpty) return;
        e.Cancel = true; // The redirect page itself is blank; never show it.
        _result.TrySetResult(result);
        Close();
    }

    public static async Task<CodeFlowAuthorizationResult> ShowAsync(Uri start, ICodeFlowUrlChecker? checker, CancellationToken ct)
    {
        var window = new SignInWindow(start, checker);
        using var registration = ct.Register(() => window.Dispatcher.InvokeAsync(window.Close));
        window.Show();
        window.Activate();
        return await window._result.Task;
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
