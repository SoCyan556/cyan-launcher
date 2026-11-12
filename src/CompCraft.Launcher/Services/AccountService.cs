using System.IO;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Authenticators;
using XboxAuthNet.Game.OAuth;
using XboxAuthNet.OAuth.CodeFlow.Parameters;

namespace CompCraft.Launcher.Services;

/// <summary>
/// Microsoft / Xbox Live sign-in for Minecraft: Java Edition via CmlLib.Core.Auth.Microsoft.
/// Refresh tokens are kept by the library's account manager in the launcher data folder; no password ever touches the launcher.
/// </summary>
public sealed class AccountService
{
    private readonly JELoginHandler _handler = new JELoginHandlerBuilder()
        .WithAccountManager(Path.Combine(AppPaths.LauncherData, "accounts.json"))
        .WithOAuthProvider(new WpfOAuthProvider(JELoginHandler.DefaultMicrosoftOAuthClientInfo))
        .Build();

    public MSession? Session { get; private set; }
    public bool Busy { get; private set; }
    public string? Error { get; set; }
    public event EventHandler? Changed;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        await Run(async () =>
        {
            try { Session = await _handler.AuthenticateSilently(ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { Session = null; } // No saved account or it expired: stay signed out quietly.
        });
    }

    /// <summary>Opens Microsoft's sign-in page in a launcher window, then exchanges it for a Minecraft session.</summary>
    public async Task SignInAsync(CancellationToken ct = default)
    {
        await Run(async () =>
        {
            try { Session = await _handler.AuthenticateInteractively(ct); }
            catch (OperationCanceledException) { } // Window closed by the player.
            catch (Exception ex) { Error = Describe(ex); }
        });
    }

    public async Task SignOutAsync()
    {
        await Run(async () =>
        {
            try { await _handler.Signout(); } catch (Exception) { /* Local session is cleared regardless. */ }
            Session = null;
        });
    }

    public void DismissError() { Error = null; Changed?.Invoke(this, EventArgs.Empty); }

    private async Task Run(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Error = null; Changed?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        finally { Busy = false; Changed?.Invoke(this, EventArgs.Empty); }
    }

    private static string Describe(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var text = e.Message ?? "";
            if (text.Contains("NOT_OWNED", StringComparison.OrdinalIgnoreCase) || text.Contains("does not own", StringComparison.OrdinalIgnoreCase) || text.Contains("ownership", StringComparison.OrdinalIgnoreCase))
                return "This Microsoft account does not own Minecraft: Java Edition.";
            if (text.Contains("2148916233")) return "This Microsoft account has no Xbox profile yet. Sign in once at minecraft.net, then try again.";
            if (text.Contains("2148916235")) return "Xbox Live is not available in your country/region.";
            if (text.Contains("2148916238")) return "This is a child account. An adult needs to add it to a Microsoft family first.";
            if (e is System.Net.Http.HttpRequestException) return "Could not reach Microsoft's sign-in servers. Check your connection.";
        }
        return "Sign-in failed: " + ex.Message;
    }
}

/// <summary>Same as XboxAuthNet's MicrosoftOAuthCodeFlowProvider, but with the launcher's own WPF sign-in window and an account picker.</summary>
internal sealed class WpfOAuthProvider(MicrosoftOAuthClientInfo clientInfo) : IAuthenticationProvider
{
    private readonly MicrosoftOAuthBuilder _oauth = new(clientInfo);
    private static readonly WpfWebUI WebUI = new();

    public IAuthenticator Authenticate() => _oauth.CodeFlow(b => b.WithWebUI(WebUI));
    public IAuthenticator AuthenticateInteractively() => _oauth.Interactive(b => b.WithWebUI(WebUI), new CodeFlowAuthorizationParameter { Prompt = "select_account" });
    public IAuthenticator AuthenticateSilently() => _oauth.Silent();
    public IAuthenticator ClearSession() => _oauth.Signout();
    public IAuthenticator Signout() => _oauth.SignoutWithBrowser(b => b.WithWebUI(WebUI));
    public ISessionValidator CreateSessionValidator() => _oauth.Validator();
}
