#:package Microsoft.Extensions.Configuration@*
#:package Microsoft.Extensions.Configuration.UserSecrets@*

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets("kroger-api-secrets")
    .Build();

var clientId = config["KrogerClientId"]
    ?? throw new InvalidOperationException(
        "KrogerClientId not configured.\nRun: dotnet user-secrets set \"KrogerClientId\" \"value\" --id kroger-api-secrets");
var clientSecret = config["KrogerClientSecret"]
    ?? throw new InvalidOperationException(
        "KrogerClientSecret not configured.\nRun: dotnet user-secrets set \"KrogerClientSecret\" \"value\" --id kroger-api-secrets");
var redirectUri = config["KrogerRedirectUri"] ?? "http://localhost/callback";

const string BaseUrl = "https://api.kroger.com";

var tokenDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kroger-api");
Directory.CreateDirectory(tokenDir);
var clientTokenFile = Path.Combine(tokenDir, "client-token.json");
var userTokenFile   = Path.Combine(tokenDir, "user-token.json");

var JsonOpts = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    WriteIndented    = true,
};

if (args.Length == 0) return PrintUsage();

return args[0].ToLower() switch
{
    "client"   => await ClientCredentials(),
    "login"    => await LoginFlow(),
    "url"      => GenerateAuthUrl(),
    "exchange" => await ExchangeCode(),
    "refresh"  => await RefreshUserToken(),
    "status"   => ShowStatus(),
    _          => PrintUsage()
};

// ── Subcommands ────────────────────────────────────────────────────────────────

async Task<int> ClientCredentials()
{
    var scope = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : "product.compact";

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));

    var response = await http.PostAsync($"{BaseUrl}/v1/connect/oauth2/token",
        new FormUrlEncodedContent([
            new("grant_type", "client_credentials"),
            new("scope", scope),
        ]));

    var json = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {json}");
        return 1;
    }

    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    await WriteToken(clientTokenFile, token);

    Console.WriteLine($"Client credentials token acquired.");
    Console.WriteLine($"  Scope:   {token.Scope}");
    Console.WriteLine($"  Expires: {token.ExpiresAt:u}");
    Console.WriteLine($"  Saved:   {clientTokenFile}");
    return 0;
}

async Task<int> LoginFlow()
{
    var scope = GetArg("--scope") ?? "cart.basic:write profile.compact";
    var port  = int.TryParse(GetArg("--port"), out var p) ? p : 8080;
    var localRedirectUri = $"http://localhost:{port}/callback";
    var state = Guid.NewGuid().ToString("N")[..16];

    var authUrl = $"{BaseUrl}/v1/connect/oauth2/authorize"
                + $"?client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(localRedirectUri)}"
                + $"&response_type=code"
                + $"&scope={Uri.EscapeDataString(scope)}"
                + $"&state={state}";

    // Start listener before opening the browser so we don't miss the redirect
    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    try
    {
        listener.Start();
    }
    catch (HttpListenerException ex)
    {
        Console.Error.WriteLine($"Could not start listener on port {port}: {ex.Message}");
        Console.Error.WriteLine($"Try a different port: auth login --port 9090");
        Console.Error.WriteLine($"Also ensure '{localRedirectUri}' is registered in your Kroger app.");
        return 1;
    }

    Console.WriteLine($"Registered redirect URI must be: {localRedirectUri}");
    Console.WriteLine($"Opening browser... (waiting up to 2 minutes)");
    Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

    // Wait for Kroger to redirect back
    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    HttpListenerContext ctx;
    try
    {
        ctx = await listener.GetContextAsync().WaitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        listener.Stop();
        Console.Error.WriteLine("Timed out waiting for authorization.");
        Console.Error.WriteLine("To authorize manually: auth url  →  auth exchange <code>");
        return 1;
    }

    // Parse the callback query string
    var query = ctx.Request.Url?.Query?.TrimStart('?') ?? "";
    var qs = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2))
        .Where(p => p.Length == 2)
        .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    var code  = qs.GetValueOrDefault("code");
    var error = qs.GetValueOrDefault("error");

    // Send a response to the browser so the tab doesn't hang
    var html = code != null
        ? "<html><body style='font-family:sans-serif;padding:2em'><h2>✓ Authorized</h2><p>You can close this tab and return to your terminal.</p></body></html>"
        : $"<html><body style='font-family:sans-serif;padding:2em'><h2>✗ Authorization failed</h2><p>{error}</p></body></html>";
    var bytes = Encoding.UTF8.GetBytes(html);
    ctx.Response.ContentType = "text/html; charset=utf-8";
    ctx.Response.ContentLength64 = bytes.Length;
    await ctx.Response.OutputStream.WriteAsync(bytes);
    ctx.Response.Close();
    listener.Stop();

    if (error != null)
    {
        Console.Error.WriteLine($"Authorization denied: {error}");
        return 1;
    }
    if (code == null)
    {
        Console.Error.WriteLine("No authorization code in callback.");
        return 1;
    }
    if (!qs.TryGetValue("state", out var returnedState) || returnedState != state)
    {
        Console.Error.WriteLine("State mismatch — possible CSRF. Aborting.");
        return 1;
    }

    // Exchange code for tokens
    Console.WriteLine("Code received. Exchanging for token...");
    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));

    var response = await http.PostAsync($"{BaseUrl}/v1/connect/oauth2/token",
        new FormUrlEncodedContent([
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", localRedirectUri),
        ]));

    var json = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Token exchange failed {(int)response.StatusCode}: {json}");
        return 1;
    }

    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    await WriteToken(userTokenFile, token);

    Console.WriteLine("User token acquired and saved.");
    Console.WriteLine($"  Scope:       {token.Scope}");
    Console.WriteLine($"  Expires:     {token.ExpiresAt:u}");
    Console.WriteLine($"  Has Refresh: {(token.RefreshToken != null ? "yes" : "no")}");
    return 0;
}

int GenerateAuthUrl()
{
    var scope = GetArg("--scope") ?? "cart.basic:write profile.compact";
    var state = GetArg("--state") ?? Guid.NewGuid().ToString("N")[..8];

    var url = $"{BaseUrl}/v1/connect/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + $"&response_type=code"
            + $"&scope={Uri.EscapeDataString(scope)}"
            + $"&state={state}";

    Console.WriteLine("Visit this URL to authorize:");
    Console.WriteLine();
    Console.WriteLine(url);
    Console.WriteLine();
    Console.WriteLine($"State: {state}");
    Console.WriteLine("After authorizing, copy the 'code' from the redirect URL and run:");
    Console.WriteLine("  auth exchange <code>");
    return 0;
}

async Task<int> ExchangeCode()
{
    if (args.Length < 2 || args[1].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: auth exchange <code> [--verifier <pkce_verifier>]");
        return 1;
    }

    var bodyParams = new List<KeyValuePair<string, string>>
    {
        new("grant_type", "authorization_code"),
        new("code", args[1]),
        new("redirect_uri", redirectUri),
    };
    var verifier = GetArg("--verifier");
    if (verifier != null) bodyParams.Add(new("code_verifier", verifier));

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));

    var response = await http.PostAsync($"{BaseUrl}/v1/connect/oauth2/token",
        new FormUrlEncodedContent(bodyParams));

    var json = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {json}");
        return 1;
    }

    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    await WriteToken(userTokenFile, token);

    Console.WriteLine("User token acquired and saved.");
    Console.WriteLine($"  Scope:       {token.Scope}");
    Console.WriteLine($"  Expires:     {token.ExpiresAt:u}");
    Console.WriteLine($"  Has Refresh: {(token.RefreshToken != null ? "yes" : "no")}");
    return 0;
}

async Task<int> RefreshUserToken()
{
    if (!File.Exists(userTokenFile))
    {
        Console.Error.WriteLine("No user token found. Run: auth url  →  auth exchange <code>");
        return 1;
    }

    var existing = JsonSerializer.Deserialize<TokenResponse>(await File.ReadAllTextAsync(userTokenFile))!;
    if (existing.RefreshToken == null)
    {
        Console.Error.WriteLine("No refresh token available. Re-authorize: auth url");
        return 1;
    }

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));

    var response = await http.PostAsync($"{BaseUrl}/v1/connect/oauth2/token",
        new FormUrlEncodedContent([
            new("grant_type", "refresh_token"),
            new("refresh_token", existing.RefreshToken),
        ]));

    var json = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {json}");
        return 1;
    }

    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    await WriteToken(userTokenFile, token);

    Console.WriteLine($"User token refreshed. New expiry: {token.ExpiresAt:u}");
    return 0;
}

int ShowStatus()
{
    Console.WriteLine("=== Kroger API Token Status ===\n");

    if (File.Exists(clientTokenFile))
    {
        var t = JsonSerializer.Deserialize<TokenResponse>(File.ReadAllText(clientTokenFile), JsonOpts)!;
        var expired = DateTime.UtcNow > t.ExpiresAt;
        Console.WriteLine($"Client Token : {(expired ? "EXPIRED" : "VALID")}");
        Console.WriteLine($"  Scope      : {t.Scope}");
        Console.WriteLine($"  Expires    : {t.ExpiresAt:u}");
    }
    else
    {
        Console.WriteLine("Client Token : NOT FOUND  (run: auth client <scope>)");
    }

    Console.WriteLine();

    if (File.Exists(userTokenFile))
    {
        var t = JsonSerializer.Deserialize<TokenResponse>(File.ReadAllText(userTokenFile), JsonOpts)!;
        var expired = DateTime.UtcNow > t.ExpiresAt;
        Console.WriteLine($"User Token   : {(expired ? "EXPIRED" : "VALID")}");
        Console.WriteLine($"  Scope      : {t.Scope}");
        Console.WriteLine($"  Expires    : {t.ExpiresAt:u}");
        Console.WriteLine($"  Has Refresh: {(t.RefreshToken != null ? "yes" : "no")}");
    }
    else
    {
        Console.WriteLine("User Token   : NOT FOUND  (run: auth url  →  auth exchange <code>)");
    }

    return 0;
}

int PrintUsage()
{
    Console.WriteLine("Usage: auth <subcommand> [args]\n");
    Console.WriteLine("Subcommands:");
    Console.WriteLine("  client [scope]             Get client credentials token");
    Console.WriteLine("                             Default scope: product.compact");
    Console.WriteLine("  login [--scope <s>]        Full user OAuth2 flow — opens browser,");
    Console.WriteLine("        [--port <n>]         catches redirect, exchanges code automatically");
    Console.WriteLine("                             Default port: 8080");
    Console.WriteLine("  url [--scope <s>]          Generate OAuth2 URL (manual flow)");
    Console.WriteLine("      [--state <s>]          Optional CSRF state value");
    Console.WriteLine("  exchange <code>            Exchange code for user token (manual flow)");
    Console.WriteLine("           [--verifier <v>]  Optional PKCE code verifier");
    Console.WriteLine("  refresh                    Refresh the stored user token");
    Console.WriteLine("  status                     Show current token status\n");
    Console.WriteLine("Common scopes:");
    Console.WriteLine("  product.compact            Read product data (client credentials)");
    Console.WriteLine("  cart.basic:write           Modify shopping cart (user auth)");
    Console.WriteLine("  profile.compact            Read user profile (user auth)");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

string? GetArg(string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

async Task WriteToken(string path, TokenResponse token) =>
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(token, JsonOpts));

// ── Models ─────────────────────────────────────────────────────────────────────

class TokenResponse
{
    [JsonPropertyName("access_token")]  public string    AccessToken  { get; set; } = "";
    [JsonPropertyName("token_type")]    public string    TokenType    { get; set; } = "Bearer";
    [JsonPropertyName("expires_in")]    public int       ExpiresIn    { get; set; }
    [JsonPropertyName("refresh_token")] public string?   RefreshToken { get; set; }
    [JsonPropertyName("scope")]         public string?   Scope        { get; set; }
    [JsonPropertyName("expires_at")]    public DateTime  ExpiresAt    { get; set; }
}
