#:package Microsoft.Extensions.Configuration@*
#:package Microsoft.Extensions.Configuration.UserSecrets@*

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
        "KrogerClientId not configured. Run: dotnet user-secrets set \"KrogerClientId\" \"value\" --id kroger-api-secrets");
var clientSecret = config["KrogerClientSecret"]
    ?? throw new InvalidOperationException(
        "KrogerClientSecret not configured. Run: dotnet user-secrets set \"KrogerClientSecret\" \"value\" --id kroger-api-secrets");

const string BaseUrl = "https://api.kroger.com";
var clientTokenFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kroger-api", "client-token.json");

var JsonOpts = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    WriteIndented    = true,
};

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
        Console.Error.WriteLine("Usage: products search <term> [--location <id>] [--limit <n>] [--start <n>] [--brand <name>] [--fulfillment <type>]");
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

    return await Get(token, $"{BaseUrl}/v1/products?{string.Join("&", query)}");
}

async Task<int> GetProduct()
{
    if (args.Length < 2 || args[1].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: products get <productId> [--location <id>]");
        return 1;
    }

    var token = await GetOrRefreshClientToken();
    if (token == null) return 1;

    var url = $"{BaseUrl}/v1/products/{Uri.EscapeDataString(args[1])}";
    var locationId = GetArg("--location");
    if (locationId != null) url += $"?filter.locationId={Uri.EscapeDataString(locationId)}";

    return await Get(token, url);
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
    Console.WriteLine("  get <productId>            Get product details by UPC or product ID");
    Console.WriteLine("    --location <id>          Include location-specific pricing\n");
    Console.WriteLine("Rate limit: 10,000 calls/day");
    Console.WriteLine("Requires: auth client product.compact");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

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
    if (!File.Exists(clientTokenFile))
    {
        Console.Error.WriteLine("No client token found. Run: auth client product.compact");
        return null;
    }

    var stored = JsonSerializer.Deserialize<TokenResponse>(await File.ReadAllTextAsync(clientTokenFile), JsonOpts)!;
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

    Directory.CreateDirectory(Path.GetDirectoryName(clientTokenFile)!);
    await File.WriteAllTextAsync(clientTokenFile,
        JsonSerializer.Serialize(token, JsonOpts));

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
