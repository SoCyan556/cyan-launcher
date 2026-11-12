using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CyanLauncher.Core.Auth;

/// <summary>A signed-in Minecraft session. The access token lives only in memory.</summary>
public sealed record GameSession(string Username, string Uuid, string AccessToken, string UserType, string Xuid)
{
    public bool IsOffline => UserType == "legacy";

    /// <summary>Offline profile for local testing only. It cannot join online-mode servers.</summary>
    public static GameSession Offline(string username)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
        return new GameSession(username, Convert.ToHexString(hash).ToLowerInvariant(), "0", "legacy", "");
    }
}

public sealed record DeviceCodePrompt(string UserCode, string VerificationUri, string Message);

/// <summary>
/// Microsoft account → Xbox Live → XSTS → Minecraft services sign-in using the OAuth device-code flow.
/// Requires an Azure (Entra ID) app registration client ID that Microsoft/Mojang have approved for the
/// Minecraft services API. Without that, Minecraft login returns 403 even with a valid Microsoft account.
/// </summary>
public sealed class MicrosoftAuth
{
    const string Scope = "XboxLive.signin offline_access";
    const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";

    readonly string _clientId;
    readonly AccountStore _store;
    static HttpClient Http => Downloader.Http;

    public MicrosoftAuth(string clientId, AccountStore store)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("No Microsoft client ID is configured. See README: Microsoft sign-in setup.");
        _clientId = clientId;
        _store = store;
    }

    /// <summary>Uses the saved refresh token if there is one; returns null when interactive sign-in is needed.</summary>
    public async Task<GameSession?> TrySilentAsync(CancellationToken ct)
    {
        var saved = _store.Load();
        if (saved?.RefreshToken is null) return null;
        try
        {
            var tok = await PostFormAsync(Authority + "token", new()
            {
                ["client_id"] = _clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = saved.RefreshToken,
                ["scope"] = Scope,
            }, ct);
            return await CompleteAsync(tok, ct);
        }
        catch (AuthException)
        {
            return null;
        }
    }

    public async Task<GameSession> SignInInteractiveAsync(Action<DeviceCodePrompt> showPrompt, CancellationToken ct)
    {
        var dc = await PostFormAsync(Authority + "devicecode", new() { ["client_id"] = _clientId, ["scope"] = Scope }, ct);
        showPrompt(new DeviceCodePrompt((string)dc["user_code"]!, (string)dc["verification_uri"]!, (string)dc["message"]!));
        var interval = TimeSpan.FromSeconds((int?)dc["interval"] ?? 5);
        var expires = DateTime.UtcNow.AddSeconds((int?)dc["expires_in"] ?? 900);
        while (DateTime.UtcNow < expires)
        {
            await Task.Delay(interval, ct);
            try
            {
                var tok = await PostFormAsync(Authority + "token", new()
                {
                    ["client_id"] = _clientId,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = (string)dc["device_code"]!,
                }, ct);
                return await CompleteAsync(tok, ct);
            }
            catch (AuthException e) when (e.Code == "authorization_pending") { }
            catch (AuthException e) when (e.Code == "slow_down") { interval += TimeSpan.FromSeconds(5); }
        }
        throw new AuthException("expired_token", "The sign-in code expired. Try again.");
    }

    public void SignOut() => _store.Clear();

    async Task<GameSession> CompleteAsync(JsonNode msToken, CancellationToken ct)
    {
        var msAccess = (string)msToken["access_token"]!;
        var refresh = (string?)msToken["refresh_token"];

        var xbl = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new JsonObject
        {
            ["Properties"] = new JsonObject { ["AuthMethod"] = "RPS", ["SiteName"] = "user.auth.xboxlive.com", ["RpsTicket"] = "d=" + msAccess },
            ["RelyingParty"] = "http://auth.xboxlive.com",
            ["TokenType"] = "JWT",
        }, null, ct);
        var xblToken = (string)xbl["Token"]!;
        var uhs = (string)xbl["DisplayClaims"]!["xui"]![0]!["uhs"]!;

        var xsts = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new JsonObject
        {
            ["Properties"] = new JsonObject { ["SandboxId"] = "RETAIL", ["UserTokens"] = new JsonArray(xblToken) },
            ["RelyingParty"] = "rp://api.minecraftservices.com/",
            ["TokenType"] = "JWT",
        }, null, ct, xstsErrors: true);
        var xstsToken = (string)xsts["Token"]!;
        var xuid = (string?)xsts["DisplayClaims"]?["xui"]?[0]?["xid"] ?? "";

        var mc = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new JsonObject { ["identityToken"] = $"XBL3.0 x={uhs};{xstsToken}" }, null, ct);
        var mcToken = (string)mc["access_token"]!;

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new("Bearer", mcToken);
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new AuthException("no_profile", "This Microsoft account does not own Minecraft Java Edition (or has no profile yet).");
        resp.EnsureSuccessStatusCode();
        var profile = JsonNode.Parse(await resp.Content.ReadAsStringAsync(ct))!;
        var name = (string)profile["name"]!;
        var id = (string)profile["id"]!;

        if (refresh is not null) _store.Save(new SavedAccount { RefreshToken = refresh, Username = name, Uuid = id });
        return new GameSession(name, id, mcToken, "msa", xuid);
    }

    static async Task<JsonNode> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var resp = await Http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var json = string.IsNullOrWhiteSpace(body) ? new JsonObject() : JsonNode.Parse(body)!;
        if (!resp.IsSuccessStatusCode)
            throw new AuthException((string?)json["error"] ?? resp.StatusCode.ToString(), (string?)json["error_description"] ?? "Microsoft sign-in failed.");
        return json;
    }

    static async Task<JsonNode> PostJsonAsync(string url, JsonObject payload, string? bearer, CancellationToken ct, bool xstsErrors = false)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        req.Headers.Accept.Add(new("application/json"));
        if (bearer != null) req.Headers.Authorization = new("Bearer", bearer);
        using var resp = await Http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            if (xstsErrors && body.Length > 0 && JsonNode.Parse(body)?["XErr"] is JsonNode xerr)
            {
                var code = (long)xerr;
                throw new AuthException("xsts", code switch
                {
                    2148916233 => "This Microsoft account has no Xbox profile. Sign in at minecraft.net once to create one.",
                    2148916235 => "Xbox Live is not available in your country.",
                    2148916238 => "This is a child account. An adult must add it to a Microsoft family.",
                    _ => $"Xbox sign-in failed (XErr {code}).",
                });
            }
            if ((int)resp.StatusCode == 403 && url.Contains("minecraftservices"))
                throw new AuthException("forbidden", "Minecraft services refused this app's client ID. It must be approved by Microsoft/Mojang for Minecraft sign-in.");
            throw new AuthException(resp.StatusCode.ToString(), $"Sign-in step failed: {url} ({(int)resp.StatusCode}).");
        }
        return JsonNode.Parse(body)!;
    }
}

public sealed class AuthException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class SavedAccount
{
    public string? RefreshToken { get; set; }
    public string? Username { get; set; }
    public string? Uuid { get; set; }
}

/// <summary>Stores the Microsoft refresh token encrypted with Windows DPAPI (current user only).</summary>
public sealed class AccountStore
{
    readonly string _path;
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CyanLauncher.Account.v1");

    public AccountStore(string path) => _path = path;

    public SavedAccount? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<SavedAccount>(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void Save(SavedAccount account)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(account);
        File.WriteAllBytes(_path, ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
