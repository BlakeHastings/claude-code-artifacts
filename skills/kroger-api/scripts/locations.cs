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
    "search"      => await SearchLocations(),
    "get"         => await GetResource($"{BaseUrl}/v1/locations/{RequireArg(1, "locationId")}"),
    "chains"      => await GetResource($"{BaseUrl}/v1/chains"),
    "chain"       => await GetResource($"{BaseUrl}/v1/chains/{RequireArg(1, "chain name")}"),
    "departments" => await GetResource($"{BaseUrl}/v1/departments"),
    "department"  => await GetResource($"{BaseUrl}/v1/departments/{RequireArg(1, "departmentId")}"),
    _             => PrintUsage()
};

// ── Subcommands ────────────────────────────────────────────────────────────────

async Task<int> SearchLocations()
{
    var zipCode     = GetArg("--zip");
    var latLong     = GetArg("--latlong");
    var lat         = GetArg("--lat");
    var lon         = GetArg("--lon");
    var locationIds = GetArg("--id");

    if (zipCode == null && latLong == null && lat == null && locationIds == null)
    {
        Console.Error.WriteLine("Error: provide at least one of --zip, --latlong, --lat/--lon, or --id");
        PrintUsage();
        return 1;
    }

    var query = new List<string> { $"filter.limit={GetArg("--limit") ?? "10"}" };

    if (zipCode     != null) query.Add($"filter.zipCode.near={Uri.EscapeDataString(zipCode)}");
    if (latLong     != null) query.Add($"filter.latLong.near={Uri.EscapeDataString(latLong)}");
    if (lat         != null) query.Add($"filter.lat.near={lat}");
    if (lon         != null) query.Add($"filter.lon.near={lon}");
    if (locationIds != null) query.Add($"filter.locationId={Uri.EscapeDataString(locationIds)}");

    var radius     = GetArg("--radius");
    var chain      = GetArg("--chain");
    var department = GetArg("--department");

    if (radius     != null) query.Add($"filter.radiusInMiles={radius}");
    if (chain      != null) query.Add($"filter.chain={Uri.EscapeDataString(chain)}");
    if (department != null) query.Add($"filter.department={Uri.EscapeDataString(department)}");

    var url = $"{BaseUrl}/v1/locations?{string.Join("&", query)}";
    return args.Contains("--json") ? await GetResource(url) : await PrintLocations(url);
}

int PrintUsage()
{
    Console.WriteLine("Usage: locations <subcommand> [args]\n");
    Console.WriteLine("Subcommands:");
    Console.WriteLine("  search                     Search store locations");
    Console.WriteLine("    --zip <code>             By ZIP code");
    Console.WriteLine("    --latlong <lat,lon>      By coordinates (e.g., 39.7,-84.1)");
    Console.WriteLine("    --lat <v> --lon <v>      By separate lat/lon values");
    Console.WriteLine("    --id <ids>               By location ID(s), comma-separated");
    Console.WriteLine("    --radius <miles>         Search radius (1–100, default: all)");
    Console.WriteLine("    --limit <n>              Max results (1–200, default: 10)");
    Console.WriteLine("    --chain <name>           Filter by chain name");
    Console.WriteLine("    --department <id>        Filter by department ID(s), comma-separated");
    Console.WriteLine("    --json                   Output raw JSON instead of formatted list");
    Console.WriteLine("  get <locationId>           Get location details");
    Console.WriteLine("  chains                     List all retail chains");
    Console.WriteLine("  chain <name>               Get chain details by name");
    Console.WriteLine("  departments                List all departments");
    Console.WriteLine("  department <id>            Get department details\n");
    Console.WriteLine("Rate limit: 1,600 calls/day per endpoint");
    Console.WriteLine("Requires: auth client product.compact");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

async Task<int> PrintLocations(string url)
{
    var token = await GetOrRefreshClientToken();
    if (token == null) return 1;

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

    var response = await http.GetAsync(url);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {body}");
        return 1;
    }

    using var doc = JsonDocument.Parse(body);
    var data = doc.RootElement.GetProperty("data");
    var count = data.GetArrayLength();

    if (count == 0) { Console.WriteLine("No locations found."); return 0; }

    Console.WriteLine($"Found {count} location{(count == 1 ? "" : "s")}:\n");

    int i = 1;
    foreach (var loc in data.EnumerateArray())
    {
        var name  = loc.TryGetProperty("name",       out var n) ? n.GetString() : "Unknown";
        var id    = loc.TryGetProperty("locationId", out var l) ? l.GetString() : "?";
        var chain = loc.TryGetProperty("chain",      out var c) ? c.GetString() : null;
        var phone = loc.TryGetProperty("phone",      out var ph) ? ph.GetString() : null;

        string? address = null;
        if (loc.TryGetProperty("address", out var addr))
        {
            var line1 = addr.TryGetProperty("addressLine1", out var a1) ? a1.GetString() : null;
            var city  = addr.TryGetProperty("city",         out var cy) ? cy.GetString() : null;
            var state = addr.TryGetProperty("state",        out var st) ? st.GetString() : null;
            var zip   = addr.TryGetProperty("zipCode",      out var zp) ? zp.GetString() : null;
            address = string.Join(", ", new[] { line1, city, state, zip }.Where(s => !string.IsNullOrEmpty(s)));
        }

        Console.WriteLine($"{i++}. {name}{(chain != null ? $" ({chain})" : "")}");
        Console.WriteLine($"   ID: {id}");
        if (!string.IsNullOrEmpty(address)) Console.WriteLine($"   {address}");
        if (!string.IsNullOrEmpty(phone))   Console.WriteLine($"   {phone}");
        Console.WriteLine();
    }

    return 0;
}

async Task<int> GetResource(string url)
{
    if (url.Contains("//null")) // RequireArg returned null
        return 1;

    var token = await GetOrRefreshClientToken();
    if (token == null) return 1;

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

    var response = await http.GetAsync(url);
    var json = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {json}");
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(
        JsonSerializer.Deserialize<JsonElement>(json, JsonOpts), JsonOpts));
    return 0;
}

async Task<string?> GetOrRefreshClientToken()
{
    var stored = LoadToken("client-token");
    if (stored == null)
    {
        Console.Error.WriteLine("No client token found. Run: auth client product.compact");
        return null;
    }

    if (DateTime.UtcNow < stored.ExpiresAt)
        return stored.AccessToken;

    Console.Error.WriteLine("Client token expired. Auto-refreshing...");

    using var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{clientId}:{clientSecret}")));

    var response = await http.PostAsync($"{BaseUrl}/v1/connect/oauth2/token",
        new FormUrlEncodedContent([
            new("grant_type", "client_credentials"),
            new("scope", stored.Scope ?? "product.compact"),
        ]));

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("Token refresh failed. Run: auth client product.compact");
        return null;
    }

    var json  = await response.Content.ReadAsStringAsync();
    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    SaveToken("client-token", token);

    return token.AccessToken;
}

string? GetArg(string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

string? RequireArg(int position, string name)
{
    if (args.Length > position && !args[position].StartsWith("--"))
        return args[position];
    Console.Error.WriteLine($"Error: <{name}> is required.");
    return null;
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
