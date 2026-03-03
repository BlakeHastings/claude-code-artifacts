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
var userTokenFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kroger-api", "user-token.json");

var JsonOpts = new JsonSerializerOptions
{
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    WriteIndented    = true,
};

if (args.Length == 0) return PrintUsage();

return args[0].ToLower() switch
{
    "add" => await AddToCart(),
    _     => PrintUsage()
};

// ── Subcommands ────────────────────────────────────────────────────────────────

// Each item token is  upc:qty  or  upc:qty:MODALITY
// A global --modality flag applies to any item that omits its own modality.
async Task<int> AddToCart()
{
    // Collect positional item tokens (everything after "add" that isn't a flag)
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
    foreach (var token in itemTokens)
    {
        var parts = token.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var qty) || qty < 1)
        {
            Console.Error.WriteLine($"Error: invalid item '{token}'. Expected format: upc:qty  or  upc:qty:MODALITY");
            return 1;
        }

        var itemModality = parts.Length >= 3 ? parts[2].ToUpper() : globalModality;
        if (itemModality != null && itemModality != "DELIVERY" && itemModality != "PICKUP")
        {
            Console.Error.WriteLine($"Error: modality in '{token}' must be DELIVERY or PICKUP.");
            return 1;
        }

        var item = new Dictionary<string, object> { ["upc"] = parts[0], ["quantity"] = qty };
        if (itemModality != null) item["modality"] = itemModality;
        items.Add(item);
    }

    var token = await GetOrRefreshUserToken();
    if (token == null) return 1;

    var payload = JsonSerializer.Serialize(new { items });

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
    Console.WriteLine("  Run: auth url --scope cart.basic:write");
    Console.WriteLine("  Then: auth exchange <code>\n");
    Console.WriteLine("Rate limit: 5,000 calls/day");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

async Task<string?> GetOrRefreshUserToken()
{
    if (!File.Exists(userTokenFile))
    {
        Console.Error.WriteLine("No user token found. Cart requires user authentication.");
        Console.Error.WriteLine("  Run: auth url --scope cart.basic:write");
        Console.Error.WriteLine("  Then: auth exchange <code>");
        return null;
    }

    var stored = JsonSerializer.Deserialize<TokenResponse>(await File.ReadAllTextAsync(userTokenFile))!;
    if (DateTime.UtcNow < stored.ExpiresAt)
        return stored.AccessToken;

    if (stored.RefreshToken == null)
    {
        Console.Error.WriteLine("User token expired and no refresh token available.");
        Console.Error.WriteLine("Re-authorize: auth url --scope cart.basic:write");
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
        Console.Error.WriteLine("Token refresh failed. Re-authorize: auth url --scope cart.basic:write");
        return null;
    }

    var json  = await response.Content.ReadAsStringAsync();
    var token = JsonSerializer.Deserialize<TokenResponse>(json)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);

    Directory.CreateDirectory(Path.GetDirectoryName(userTokenFile)!);
    await File.WriteAllTextAsync(userTokenFile,
        JsonSerializer.Serialize(token, new JsonSerializerOptions { WriteIndented = true }));

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
