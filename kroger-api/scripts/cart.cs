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
    "add" => await AddToCart(),
    _     => PrintUsage()
};

// ── Subcommands ────────────────────────────────────────────────────────────────

async Task<int> AddToCart()
{
    var itemTokens = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
    if (itemTokens.Count == 0)
    {
        Console.Error.WriteLine("Usage: cart add <upc>:<qty>[:<modality>] [more items...] [--modality DELIVERY|PICKUP]");
        return 1;
    }

    var globalModality = GetArg("--modality");
    if (globalModality != null && globalModality != "DELIVERY" && globalModality != "PICKUP")
    {
        Console.Error.WriteLine("Error: --modality must be DELIVERY or PICKUP.");
        return 1;
    }

    var items = new List<Dictionary<string, object>>();
    foreach (var itemToken in itemTokens)
    {
        var parts = itemToken.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var qty) || qty < 1)
        {
            Console.Error.WriteLine($"Error: invalid item '{itemToken}'. Expected format: upc:qty  or  upc:qty:MODALITY");
            return 1;
        }

        var itemModality = parts.Length >= 3 ? parts[2].ToUpper() : globalModality;
        if (itemModality != null && itemModality != "DELIVERY" && itemModality != "PICKUP")
        {
            Console.Error.WriteLine($"Error: modality in '{itemToken}' must be DELIVERY or PICKUP.");
            return 1;
        }

        var item = new Dictionary<string, object> { ["upc"] = parts[0], ["quantity"] = qty };
        if (itemModality != null) item["modality"] = itemModality;
        items.Add(item);
    }

    var token = await GetOrRefreshUserToken();
    if (token == null) return 1;

    var payload = JsonSerializer.Serialize(new { items }, JsonOpts);

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

    var response = await http.PutAsync($"{BaseUrl}/v1/cart/add",
        new StringContent(payload, Encoding.UTF8, "application/json"));

    if (response.IsSuccessStatusCode)
    {
        Console.WriteLine($"Added {items.Count} item(s) to cart:");
        foreach (var item in items)
            Console.WriteLine($"  {item["quantity"]}x {item["upc"]}" +
                (item.TryGetValue("modality", out var m) ? $" ({m})" : ""));
        return 0;
    }

    var body = await response.Content.ReadAsStringAsync();
    Console.Error.WriteLine($"Error {(int)response.StatusCode}: {body}");
    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        Console.Error.WriteLine("Tip: User token may have expired. Run: auth refresh");
    return 1;
}

int PrintUsage()
{
    Console.WriteLine("Usage: cart <subcommand> [args]\n");
    Console.WriteLine("Subcommands:");
    Console.WriteLine("  add <items...>             Add one or more items to the cart");
    Console.WriteLine("    Item format: upc:qty  or  upc:qty:MODALITY");
    Console.WriteLine("    --modality <type>        Global modality for items without one (DELIVERY|PICKUP)");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  cart add 0001111060903:2");
    Console.WriteLine("  cart add 0001111060903:2 0001234567890:1");
    Console.WriteLine("  cart add 0001111060903:2 0001234567890:1 --modality PICKUP");
    Console.WriteLine("  cart add 0001111060903:2:DELIVERY 0001234567890:1:PICKUP");
    Console.WriteLine();
    Console.WriteLine("Note: Cart requires user authentication (scope: cart.basic:write).");
    Console.WriteLine("  Run: auth login --scope cart.basic:write\n");
    Console.WriteLine("Rate limit: 5,000 calls/day");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

async Task<string?> GetOrRefreshUserToken()
{
    var stored = LoadToken("user-token");
    if (stored == null)
    {
        Console.Error.WriteLine("No user token found. Cart requires user authentication.");
        Console.Error.WriteLine("  Run: auth login --scope cart.basic:write");
        return null;
    }

    if (DateTime.UtcNow < stored.ExpiresAt)
        return stored.AccessToken;

    if (stored.RefreshToken == null)
    {
        Console.Error.WriteLine("User token expired and no refresh token available.");
        Console.Error.WriteLine("Re-authorize: auth login --scope cart.basic:write");
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
        Console.Error.WriteLine("Token refresh failed. Re-authorize: auth login --scope cart.basic:write");
        return null;
    }

    var json  = await response.Content.ReadAsStringAsync();
    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    SaveToken("user-token", token);

    return token.AccessToken;
}

string? GetArg(string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
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
