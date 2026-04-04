// ── Kroger API Credential Resolution (reference / documentation) ───────────────
//
// NOTE: This file cannot be #load-ed because the scripts use the .NET single-file
//       runner (dotnet run file.cs), which does not support #load. The pattern
//       below is inlined at the top of every script instead.
//
// Startup credentials (ClientId, ClientSecret, RedirectUri) resolve in order:
//
//   1. Environment variables
//        KROGER_CLIENT_ID
//        KROGER_CLIENT_SECRET
//        KROGER_REDIRECT_URI
//
//   2. dotnet user secrets  (ID: a4f2e8b1-3c7d-4a9e-b5f0-1d2c3e4f5a6b)
//        dotnet user-secrets --id "a4f2e8b1-3c7d-4a9e-b5f0-1d2c3e4f5a6b" set "Kroger:ClientId"     "<value>"
//        dotnet user-secrets --id "a4f2e8b1-3c7d-4a9e-b5f0-1d2c3e4f5a6b" set "Kroger:ClientSecret" "<value>"
//        dotnet user-secrets --id "a4f2e8b1-3c7d-4a9e-b5f0-1d2c3e4f5a6b" set "Kroger:RedirectUri"  "<value>"
//
//   3. System credential store  (Windows Credential Manager / macOS Keychain / Linux Secret Service)
//        Set via: auth setup
//
// Runtime tokens (client-token, user-token) are ALWAYS read from and written to
// the system credential store, regardless of how startup credentials are supplied.
//
// ── Inline pattern used in each script ────────────────────────────────────────
//
// const string UserSecretsId = "a4f2e8b1-3c7d-4a9e-b5f0-1d2c3e4f5a6b";
// var store   = CredentialManager.Create("kroger-api");
// var secrets = new ConfigurationBuilder()
//     .AddUserSecrets(UserSecretsId, optional: true)
//     .Build();
//
// string? Resolve(string envVar, string secretKey, string storeKey) =>
//     Environment.GetEnvironmentVariable(envVar)
//     ?? secrets[secretKey]
//     ?? store.Get($"https://api.kroger.com/{storeKey}", "kroger")?.Password;
//
// var clientId     = Resolve("KROGER_CLIENT_ID",     "Kroger:ClientId",     "client-id");
// var clientSecret = Resolve("KROGER_CLIENT_SECRET", "Kroger:ClientSecret", "client-secret");
//
// // Runtime tokens — always credential store
// void SaveToken(string key, TokenResponse token) =>
//     store.AddOrUpdate($"https://api.kroger.com/{key}", "kroger", JsonSerializer.Serialize(token, JsonOpts));
//
// TokenResponse? LoadToken(string key) {
//     var json = store.Get($"https://api.kroger.com/{key}", "kroger")?.Password;
//     return json is null ? null : JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts);
// }
