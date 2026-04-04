#:package Devlooped.CredentialManager@*
#:package Microsoft.Extensions.Configuration@*
#:package Microsoft.Extensions.Configuration.UserSecrets@*

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GitCredentialManager;
using Microsoft.Extensions.Configuration;

// ── Credential resolution ──────────────────────────────────────────────────────

const string UserSecretsId = "a4f2e8b1-3c7d-4a9e-b5f0-1d2c3e4f5a6b";
const string BaseUrl       = "https://api.kroger.com";

var store   = CredentialManager.Create("kroger-api");
var secrets = new ConfigurationBuilder()
    .AddUserSecrets(UserSecretsId)
    .Build();

var JsonOpts = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    WriteIndented    = true,
};

string? Resolve(string envVar, string secretKey, string storeKey) =>
    Environment.GetEnvironmentVariable(envVar)
    ?? secrets[secretKey]
    ?? store.Get($"{BaseUrl}/{storeKey}", "kroger")?.Password;

var clientId     = Resolve("KROGER_CLIENT_ID",     "Kroger:ClientId",     "client-id")
    ?? throw new InvalidOperationException("Kroger credentials not configured. Run: auth setup");
var clientSecret = Resolve("KROGER_CLIENT_SECRET", "Kroger:ClientSecret", "client-secret")
    ?? throw new InvalidOperationException("Kroger credentials not configured. Run: auth setup");

void SaveToken(string key, TokenResponse token) =>
    store.AddOrUpdate($"{BaseUrl}/{key}", "kroger", JsonSerializer.Serialize(token, JsonOpts));

TokenResponse? LoadToken(string key)
{
    var json = store.Get($"{BaseUrl}/{key}", "kroger")?.Password;
    return json is null ? null : JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts);
}

// ── Routing ────────────────────────────────────────────────────────────────────

if (args.Length == 0) return PrintUsage();

return args[0].ToLower() switch
{
    "profile" => await GetProfile(),
    _         => PrintUsage()
};

// ── Subcommands ────────────────────────────────────────────────────────────────

async Task<int> GetProfile()
{
    var token = await GetOrRefreshUserToken();
    if (token == null) return 1;

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

    var response = await http.GetAsync($"{BaseUrl}/v1/identity/profile");
    var json = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {json}");
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            Console.Error.WriteLine("Tip: User token expired. Run: auth refresh");
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(
        JsonSerializer.Deserialize<JsonElement>(json, JsonOpts), JsonOpts));
    return 0;
}

int PrintUsage()
{
    Console.WriteLine("Usage: identity <subcommand>\n");
    Console.WriteLine("Subcommands:");
    Console.WriteLine("  profile              Get authenticated customer profile ID\n");
    Console.WriteLine("Note: Identity requires user authentication (scope: profile.compact).");
    Console.WriteLine("  Run: auth login --scope profile.compact\n");
    Console.WriteLine("Rate limit: 5,000 calls/day");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

async Task<string?> GetOrRefreshUserToken()
{
    var stored = LoadToken("user-token");
    if (stored == null)
    {
        Console.Error.WriteLine("No user token found. Identity requires user authentication.");
        Console.Error.WriteLine("  Run: auth login --scope profile.compact");
        return null;
    }

    if (DateTime.UtcNow < stored.ExpiresAt)
        return stored.AccessToken;

    if (stored.RefreshToken == null)
    {
        Console.Error.WriteLine("User token expired and no refresh token available.");
        Console.Error.WriteLine("Re-authorize: auth login --scope profile.compact");
        return null;
    }

    Console.Error.WriteLine("User token expired. Auto-refreshing...");

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));

    var response = await http.PostAsync($"{BaseUrl}/v1/connect/oauth2/token",
        new FormUrlEncodedContent([
            new("grant_type", "refresh_token"),
            new("refresh_token", stored.RefreshToken),
        ]));

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("Token refresh failed. Re-authorize: auth login --scope profile.compact");
        return null;
    }

    var json  = await response.Content.ReadAsStringAsync();
    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    SaveToken("user-token", token);

    return token.AccessToken;
}

// ── Models ─────────────────────────────────────────────────────────────────────

class TokenResponse
{
    [JsonPropertyName("access_token")]  public string   AccessToken  { get; set; } = "";
    [JsonPropertyName("token_type")]    public string   TokenType    { get; set; } = "Bearer";
    [JsonPropertyName("expires_in")]    public int      ExpiresIn    { get; set; }
    [JsonPropertyName("refresh_token")] public string?  RefreshToken { get; set; }
    [JsonPropertyName("scope")]         public string?  Scope        { get; set; }
    [JsonPropertyName("expires_at")]    public DateTime ExpiresAt    { get; set; }
}
