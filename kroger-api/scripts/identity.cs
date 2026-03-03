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
        JsonSerializer.Deserialize<JsonElement>(json),
        new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

int PrintUsage()
{
    Console.WriteLine("Usage: identity <subcommand>\n");
    Console.WriteLine("Subcommands:");
    Console.WriteLine("  profile              Get authenticated customer profile ID\n");
    Console.WriteLine("Note: Identity requires user authentication (scope: profile.compact).");
    Console.WriteLine("  Run: auth url --scope profile.compact");
    Console.WriteLine("  Then: auth exchange <code>\n");
    Console.WriteLine("Rate limit: 5,000 calls/day");
    return 1;
}

// ── Helpers ────────────────────────────────────────────────────────────────────

async Task<string?> GetOrRefreshUserToken()
{
    if (!File.Exists(userTokenFile))
    {
        Console.Error.WriteLine("No user token found. Identity requires user authentication.");
        Console.Error.WriteLine("  Run: auth url --scope profile.compact");
        Console.Error.WriteLine("  Then: auth exchange <code>");
        return null;
    }

    var stored = JsonSerializer.Deserialize<TokenResponse>(await File.ReadAllTextAsync(userTokenFile))!;
    if (DateTime.UtcNow < stored.ExpiresAt)
        return stored.AccessToken;

    if (stored.RefreshToken == null)
    {
        Console.Error.WriteLine("User token expired and no refresh token available.");
        Console.Error.WriteLine("Re-authorize: auth url --scope profile.compact");
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
        Console.Error.WriteLine("Token refresh failed. Re-authorize: auth url --scope profile.compact");
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
