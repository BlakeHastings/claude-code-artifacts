#:package Devlooped.CredentialManager@*
#:package Microsoft.Extensions.Configuration@*
#:package Microsoft.Extensions.Configuration.UserSecrets@*

// Suppress IL2026 and IL3050 warnings
#pragma warning disable IL2026,IL3050

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
    "search" => await SearchProducts(),
    "get"    => await GetProduct(),
    _        => PrintUsage()
};

// ── Subcommands ────────────────────────────────────────────────────────────────

async Task<int> SearchProducts()
{
    var term = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : GetArg("--term");
    if (term == null)
    {
        Console.Error.WriteLine("Usage: products search <term> [--location <id>] [--limit <n>] [--start <n>] [--brand <name>] [--fulfillment <type>] [--json]");
        return 1;
    }

    var query = new List<string>
    {
        $"filter.term={Uri.EscapeDataString(term)}",
        $"filter.limit={GetArg("--limit") ?? "10"}",
    };

    var locationId  = GetArg("--location");
    var start       = GetArg("--start");
    var brand       = GetArg("--brand");
    var fulfillment = GetArg("--fulfillment");
    var productId   = GetArg("--product-id");

    if (locationId  != null) query.Add($"filter.locationId={Uri.EscapeDataString(locationId)}");
    if (start       != null) query.Add($"filter.start={start}");
    if (brand       != null) query.Add($"filter.brand={Uri.EscapeDataString(brand)}");
    if (fulfillment != null) query.Add($"filter.fulfillment={Uri.EscapeDataString(fulfillment)}");
    if (productId   != null) query.Add($"filter.productId={Uri.EscapeDataString(productId)}");

    var token = await GetOrRefreshClientToken();
    if (token == null) return 1;

    var url = $"{BaseUrl}/v1/products?{string.Join("&", query)}";
    return args.Contains("--json") ? await Get(token, url) : await PrintProducts(token, url);
}

async Task<int> GetProduct()
{
    if (args.Length < 2 || args[1].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: products get <productId> [--location <id>] [--json]");
        return 1;
    }

    var token = await GetOrRefreshClientToken();
    if (token == null) return 1;

    var url = $"{BaseUrl}/v1/products/{Uri.EscapeDataString(args[1])}";
    var locationId = GetArg("--location");
    if (locationId != null) url += $"?filter.locationId={Uri.EscapeDataString(locationId)}";

    return args.Contains("--json") ? await Get(token, url) : await PrintProduct(token, url);
}

int PrintUsage()
{
    Console.WriteLine("Usage: products <subcommand> [args]\n");
    Console.WriteLine("Subcommands:");
    Console.WriteLine("  search <term>              Search products by keyword");
    Console.WriteLine("    --location <id>          Filter by store location ID (enables pricing)");
    Console.WriteLine("    --limit <n>              Max results (1–50, default: 10)");
    Console.WriteLine("    --start <n>              Pagination offset");
    Console.WriteLine("    --brand <name>           Filter by brand (pipe-separated for multiple)");
    Console.WriteLine("    --fulfillment <type>     Filter by fulfillment type");
    Console.WriteLine("    --product-id <ids>       Filter by product ID(s), comma-separated");
    Console.WriteLine("    --json                   Output raw JSON instead of formatted list");
    Console.WriteLine("  get <productId>            Get product details by UPC or product ID");
    Console.WriteLine("    --location <id>          Include location-specific pricing");
    Console.WriteLine("    --json                   Output raw JSON instead of formatted detail\n");
    Console.WriteLine("Rate limit: 10,000 calls/day");
    Console.WriteLine("Requires: auth client product.compact");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

async Task<int> PrintProducts(string accessToken, string url)
{
    using var http = CreateHttpClient(accessToken);
    var response = await http.GetAsync(url);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {body}");
        return 1;
    }

    using var doc  = JsonDocument.Parse(body);
    var data = doc.RootElement.GetProperty("data");
    var count = data.GetArrayLength();

    if (count == 0) { Console.WriteLine("No products found."); return 0; }

    Console.WriteLine($"Found {count} product{(count == 1 ? "" : "s")}:\n");

    int i = 1;
    foreach (var p in data.EnumerateArray())
    {
        var desc  = p.TryGetProperty("description", out var d) ? d.GetString() : "Unknown";
        var upc   = p.TryGetProperty("productId",   out var u) ? u.GetString() : "?";
        var brand = p.TryGetProperty("brand",        out var b) ? b.GetString() : null;

        string? size = null, price = null;
        if (p.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
        {
            var item = items[0];
            if (item.TryGetProperty("size", out var s)) size = s.GetString();
            if (item.TryGetProperty("price", out var pr))
            {
                if (pr.TryGetProperty("promo",   out var promo) && promo.GetDecimal() > 0)
                    price = $"${promo.GetDecimal():F2} (sale, reg ${pr.GetProperty("regular").GetDecimal():F2})";
                else if (pr.TryGetProperty("regular", out var reg) && reg.GetDecimal() > 0)
                    price = $"${reg.GetDecimal():F2}";
            }
        }

        var details = new List<string> { $"UPC: {upc}" };
        if (!string.IsNullOrEmpty(brand)) details.Add($"Brand: {brand}");
        if (!string.IsNullOrEmpty(size))  details.Add($"Size: {size}");
        if (price != null)                details.Add($"Price: {price}");

        Console.WriteLine($"{i++}. {desc}");
        Console.WriteLine($"   {string.Join("  |  ", details)}");
        Console.WriteLine();
    }

    return 0;
}

async Task<int> PrintProduct(string accessToken, string url)
{
    using var http = CreateHttpClient(accessToken);
    var response = await http.GetAsync(url);
    var body = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Error {(int)response.StatusCode}: {body}");
        return 1;
    }

    using var doc = JsonDocument.Parse(body);
    var p = doc.RootElement.TryGetProperty("data", out var data) ? data : doc.RootElement;

    string? Field(string key) => p.TryGetProperty(key, out var v) ? v.GetString() : null;

    Console.WriteLine(Field("description") ?? "Unknown product");
    Console.WriteLine(new string('─', 50));
    Console.WriteLine($"  UPC:        {Field("productId") ?? Field("upc") ?? "?"}");
    if (Field("brand") is { } brand) Console.WriteLine($"  Brand:      {brand}");

    if (p.TryGetProperty("categories", out var cats) && cats.GetArrayLength() > 0)
        Console.WriteLine($"  Categories: {string.Join(", ", cats.EnumerateArray().Select(c => c.GetString()))}");

    if (p.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
    {
        var item = items[0];
        if (item.TryGetProperty("size",   out var sz))   Console.WriteLine($"  Size:       {sz.GetString()}");
        if (item.TryGetProperty("soldBy", out var sold)) Console.WriteLine($"  Sold By:    {sold.GetString()}");
        if (item.TryGetProperty("price",  out var pr))
        {
            if (pr.TryGetProperty("regular", out var reg) && reg.GetDecimal() > 0)
                Console.WriteLine($"  Price:      ${reg.GetDecimal():F2}" +
                    (pr.TryGetProperty("promo", out var promo) && promo.GetDecimal() > 0
                        ? $"  (sale: ${promo.GetDecimal():F2})" : ""));
        }
    }

    if (p.TryGetProperty("countryOrigin", out var origin) && origin.GetString() is { Length: > 0 } o)
        Console.WriteLine($"  Origin:     {o}");

    return 0;
}

async Task<int> Get(string accessToken, string url)
{
    using var http = CreateHttpClient(accessToken);
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

HttpClient CreateHttpClient(string accessToken)
{
    var http = new HttpClient();
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    return http;
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
