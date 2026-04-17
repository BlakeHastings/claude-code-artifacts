#:package Devlooped.CredentialManager@*

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GitCredentialManager;

// ── Constants ──────────────────────────────────────────────────────────────────

const string ServiceName = "outlook-api";
const string AccountName = "outlook";
const string AuthBase    = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
const string GraphBase   = "https://graph.microsoft.com/v1.0";
const string CredBase    = "https://graph.microsoft.com/outlook-api";
const string Scopes      = "Mail.ReadWrite Mail.Send MailboxSettings.ReadWrite Calendars.ReadWrite Contacts.ReadWrite Tasks.ReadWrite offline_access";

string TokenFile = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".outlook-api", "token.json");

var store    = CredentialManager.Create(ServiceName);
var JsonOpts = new JsonSerializerOptions
{
    TypeInfoResolver            = new DefaultJsonTypeInfoResolver(),
    WriteIndented               = true,
    PropertyNameCaseInsensitive = true,
};

// ── Top-level routing ──────────────────────────────────────────────────────────

if (args.Length == 0) return PrintUsage();

// Auth commands don't need a token — handle before http client setup
if (args[0].ToLower() == "auth") return await HandleAuth();

// All other commands need a valid token
var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetValidToken());

return args[0].ToLower() switch
{
    "mail"      => await HandleMail(),
    "calendar"  => await HandleCalendar(),
    "contacts"  => await HandleContacts(),
    "folders"   => await HandleFolders(),
    "tasks"     => await HandleTasks(),
    "settings"  => await HandleSettings(),
    _           => PrintUsage(),
};

// ══════════════════════════════════════════════════════════════════════════════
// AUTH
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleAuth()
{
    if (args.Length < 2) return PrintAuthUsage();
    return args[1].ToLower() switch
    {
        "setup"   => AuthSetup(),
        "login"   => await AuthLogin(),
        "refresh" => await AuthRefresh(),
        "status"  => AuthStatus(),
        "logout"  => AuthLogout(),
        _         => PrintAuthUsage(),
    };
}

int AuthSetup()
{
    string id;
    if (args.Length >= 3 && !args[2].StartsWith("--"))
    {
        id = args[2].Trim();
    }
    else
    {
        Console.WriteLine("Usage: auth setup <client-id>");
        Console.WriteLine();
        Console.WriteLine("Get a Client ID by registering a free Azure App at https://portal.azure.com");
        Console.WriteLine("See references/auth-guide.md for step-by-step instructions.");
        Console.Write("\nApplication (Client) ID: ");
        id = Console.ReadLine()?.Trim() ?? "";
    }

    if (string.IsNullOrEmpty(id))
    {
        Console.Error.WriteLine("Client ID is required.");
        return 1;
    }

    store.AddOrUpdate($"{CredBase}/client-id", AccountName, id);
    Console.WriteLine("Client ID saved. Run: auth login");
    return 0;
}

async Task<int> AuthLogin()
{
    var clientId = store.Get($"{CredBase}/client-id", AccountName)?.Password;
    if (clientId == null) { Console.Error.WriteLine("Run: auth setup <client-id> first."); return 1; }

    var port        = int.TryParse(GetArg("--port"), out var p) ? p : 8080;
    var redirectUri = $"http://localhost:{port}/callback";

    var verifierBytes  = RandomNumberGenerator.GetBytes(64);
    var codeVerifier   = Base64UrlEncode(verifierBytes);
    var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
    var codeChallenge  = Base64UrlEncode(challengeBytes);
    var state          = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLower();

    var authUrl = $"{AuthBase}/authorize"
                + $"?client_id={Uri.EscapeDataString(clientId)}"
                + $"&response_type=code"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + $"&scope={Uri.EscapeDataString(Scopes)}"
                + $"&state={state}"
                + $"&code_challenge={codeChallenge}"
                + $"&code_challenge_method=S256"
                + $"&prompt=select_account";

    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    try { listener.Start(); }
    catch (HttpListenerException ex)
    {
        Console.Error.WriteLine($"Could not start listener on port {port}: {ex.Message}");
        Console.Error.WriteLine($"Try: auth login --port 9090  (register that URI in your Azure app too)");
        return 1;
    }

    Console.WriteLine($"Redirect URI must be registered in Azure app: {redirectUri}");
    Console.WriteLine("Opening browser...");
    Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    HttpListenerContext ctx;
    try { ctx = await listener.GetContextAsync().WaitAsync(cts.Token); }
    catch (OperationCanceledException) { listener.Stop(); Console.Error.WriteLine("Timed out."); return 1; }

    var qs        = ParseQueryString(ctx.Request.Url?.Query?.TrimStart('?') ?? "");
    var code      = qs.GetValueOrDefault("code");
    var error     = qs.GetValueOrDefault("error");
    var errorDesc = qs.GetValueOrDefault("error_description");

    await WriteHtmlResponse(ctx.Response, code != null,
        code != null ? "Authorization successful! You can close this tab." : $"{error}: {errorDesc}");
    listener.Stop();

    if (error != null) { Console.Error.WriteLine($"Authorization failed: {error}\n  {errorDesc}"); return 1; }
    if (code == null)  { Console.Error.WriteLine("No code received."); return 1; }
    if (!qs.TryGetValue("state", out var retState) || retState != state)
    { Console.Error.WriteLine("State mismatch — possible CSRF. Aborting."); return 1; }

    Console.WriteLine("Exchanging code for tokens...");

    using var loginHttp = new HttpClient();
    var resp = await loginHttp.PostAsync($"{AuthBase}/token",
        new FormUrlEncodedContent([
            new("grant_type",    "authorization_code"),
            new("code",          code),
            new("redirect_uri",  redirectUri),
            new("client_id",     clientId),
            new("code_verifier", codeVerifier),
            new("scope",         Scopes),
        ]));

    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"Token exchange failed: {json}"); return 1; }

    var token = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    token.ExpiresAt = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 60);
    SaveToken(token);

    Console.WriteLine($"\nLogin successful! Token saved to {TokenFile}");
    Console.WriteLine($"  Expires: {token.ExpiresAt:u}  |  Refresh: {(token.RefreshToken != null ? "yes" : "no")}");

    loginHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    var meResp = await loginHttp.GetAsync($"{GraphBase}/me?$select=displayName,mail,userPrincipalName");
    if (meResp.IsSuccessStatusCode)
    {
        var me    = JsonSerializer.Deserialize<MeResponse>(await meResp.Content.ReadAsStringAsync(), JsonOpts);
        var email = me?.Mail ?? me?.UserPrincipalName ?? "(unknown)";
        Console.WriteLine($"  Signed in as: {me?.DisplayName} <{email}>");
    }
    return 0;
}

async Task<int> AuthRefresh()
{
    var token    = LoadToken();
    var clientId = store.Get($"{CredBase}/client-id", AccountName)?.Password;
    if (token == null)    { Console.Error.WriteLine("No token. Run: auth login"); return 1; }
    if (token.RefreshToken == null) { Console.Error.WriteLine("No refresh token. Run: auth login"); return 1; }
    if (clientId == null) { Console.Error.WriteLine("No client ID. Run: auth setup"); return 1; }

    using var h   = new HttpClient();
    var resp = await h.PostAsync($"{AuthBase}/token",
        new FormUrlEncodedContent([
            new("grant_type",    "refresh_token"),
            new("refresh_token", token.RefreshToken),
            new("client_id",     clientId),
            new("scope",         Scopes),
        ]));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"Refresh failed: {json}"); return 1; }

    var newToken = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts)!;
    newToken.ExpiresAt = DateTime.UtcNow.AddSeconds(newToken.ExpiresIn - 60);
    SaveToken(newToken);
    Console.WriteLine($"Token refreshed. Expires: {newToken.ExpiresAt:u}");
    return 0;
}

int AuthStatus()
{
    var clientId = store.Get($"{CredBase}/client-id", AccountName)?.Password;
    Console.WriteLine("=== Outlook API Status ===\n");
    Console.WriteLine($"Client ID : {(clientId != null ? clientId[..8] + "..." : "NOT SET  (run: auth setup <id>)")}");
    Console.WriteLine($"Token file: {TokenFile}");
    var token = LoadToken();
    if (token == null) { Console.WriteLine("Token     : NOT FOUND  (run: auth login)"); return 0; }
    var expired = DateTime.UtcNow >= token.ExpiresAt;
    Console.WriteLine($"Token     : {(expired ? "EXPIRED" : "VALID")}  (expires {token.ExpiresAt:u})");
    Console.WriteLine($"  Refresh : {(token.RefreshToken != null ? "available" : "none")}");
    if (expired) Console.WriteLine(token.RefreshToken != null ? "\nRun: auth refresh" : "\nRun: auth login");
    return 0;
}

int AuthLogout()
{
    if (File.Exists(TokenFile)) { File.Delete(TokenFile); Console.WriteLine("Token deleted."); }
    else Console.WriteLine("No token file found.");
    return 0;
}

int PrintAuthUsage()
{
    Console.WriteLine("Usage: auth <subcommand>\n");
    Console.WriteLine("  setup <client-id>   Save Azure App Client ID");
    Console.WriteLine("  login [--port <n>]  Browser OAuth2 PKCE login");
    Console.WriteLine("  refresh             Refresh the access token");
    Console.WriteLine("  status              Show config and token state");
    Console.WriteLine("  logout              Delete stored tokens");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// MAIL
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleMail()
{
    if (args.Length < 2) return PrintMailUsage();
    return args[1].ToLower() switch
    {
        "list"        => await MailList(),
        "read"        => await MailRead(),
        "send"        => await MailSend(),
        "reply"       => await MailReply(false),
        "reply-all"   => await MailReply(true),
        "forward"     => await MailForward(),
        "delete"      => await MailDelete(),
        "move"        => await MailMove(),
        "search"      => await MailSearch(),
        "flag"        => await MailSetFlag(true),
        "unflag"      => await MailSetFlag(false),
        "mark-read"   => await MailSetRead(true),
        "mark-unread" => await MailSetRead(false),
        "draft"       => await HandleDraft(),
        "attachment"  => await HandleAttachment(),
        "categories"  => await HandleCategories(),
        _             => PrintMailUsage(),
    };
}

async Task<int> MailList()
{
    var folder  = GetArg("--folder") ?? "inbox";
    var top     = GetArg("--top") ?? "10";
    var from    = GetArg("--from");
    var subject = GetArg("--subject");
    var unread  = args.Contains("--unread");

    var folderPath = WellKnownFolder(folder);
    var select     = "$select=id,subject,from,receivedDateTime,isRead,hasAttachments,bodyPreview,importance,flag";
    var filters    = new List<string>();
    if (unread)        filters.Add("isRead eq false");
    if (from != null)  filters.Add($"from/emailAddress/address eq '{OData(from)}'");
    if (subject != null) filters.Add($"contains(subject,'{OData(subject)}')");

    var url = $"{GraphBase}/me/{folderPath}/messages?{select}&$top={top}&$orderby=receivedDateTime desc";
    if (filters.Count > 0) url += $"&$filter={Uri.EscapeDataString(string.Join(" and ", filters))}";

    var data = await GetArray(url);
    if (data == null || data.Count == 0) { Console.WriteLine("No messages found."); return 0; }

    Console.WriteLine($"Messages in {folder} (top {top}):\n");
    foreach (var msg in data)
    {
        var id       = msg?["id"]?.GetValue<string>() ?? "";
        var subj     = msg?["subject"]?.GetValue<string>() ?? "(no subject)";
        var fromName = msg?["from"]?["emailAddress"]?["name"]?.GetValue<string>() ?? msg?["from"]?["emailAddress"]?["address"]?.GetValue<string>() ?? "";
        var recvd    = msg?["receivedDateTime"]?.GetValue<string>() ?? "";
        var isRead   = msg?["isRead"]?.GetValue<bool>() ?? true;
        var hasAtt   = msg?["hasAttachments"]?.GetValue<bool>() ?? false;
        var flagged  = msg?["flag"]?["flagStatus"]?.GetValue<string>() == "flagged";
        var preview  = msg?["bodyPreview"]?.GetValue<string>() ?? "";
        var dt       = DateTime.TryParse(recvd, out var d) ? d.ToLocalTime().ToString("MMM dd HH:mm") : recvd;
        var mark     = (isRead ? "  " : "* ") + (flagged ? "[F] " : "    ") + (hasAtt ? "[A] " : "    ");

        Console.WriteLine($"{mark}{dt}  {fromName,-25}  {subj}");
        Console.WriteLine($"      ID: {id}");
        if (!string.IsNullOrWhiteSpace(preview))
            Console.WriteLine($"      {preview[..Math.Min(100, preview.Length)]}");
        Console.WriteLine();
    }
    Console.WriteLine("* = unread  [F] = flagged  [A] = attachment");
    return 0;
}

async Task<int> MailRead()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: mail read <id> [--full-body] [--raw]"); return 1; }
    var id       = args[2];
    var fullBody = args.Contains("--full-body") || args.Contains("--raw");
    var rawHtml  = args.Contains("--raw");
    var select   = fullBody
        ? "$select=id,subject,from,toRecipients,ccRecipients,receivedDateTime,isRead,hasAttachments,body,flag,importance"
        : "$select=id,subject,from,toRecipients,ccRecipients,receivedDateTime,isRead,hasAttachments,bodyPreview,flag,importance";

    var resp = await http.GetAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}?{select}");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("read", resp.StatusCode, json);

    var msg     = JsonNode.Parse(json)!;
    var subj    = msg["subject"]?.GetValue<string>() ?? "(no subject)";
    var from    = msg["from"]?["emailAddress"]?["address"]?.GetValue<string>() ?? "";
    var fromN   = msg["from"]?["emailAddress"]?["name"]?.GetValue<string>() ?? from;
    var recvd   = msg["receivedDateTime"]?.GetValue<string>() ?? "";
    var isRead  = msg["isRead"]?.GetValue<bool>() ?? true;
    var hasAtt  = msg["hasAttachments"]?.GetValue<bool>() ?? false;
    var flagged = msg["flag"]?["flagStatus"]?.GetValue<string>() == "flagged";
    var imp     = msg["importance"]?.GetValue<string>() ?? "normal";
    var to      = msg["toRecipients"]?.AsArray().Select(r => $"{r?["emailAddress"]?["name"]?.GetValue<string>()} <{r?["emailAddress"]?["address"]?.GetValue<string>()}>") ?? [];
    var cc      = msg["ccRecipients"]?.AsArray().Select(r => $"{r?["emailAddress"]?["name"]?.GetValue<string>()} <{r?["emailAddress"]?["address"]?.GetValue<string>()}>") ?? [];

    Console.WriteLine($"Subject  : {subj}");
    Console.WriteLine($"From     : {fromN} <{from}>");
    Console.WriteLine($"To       : {string.Join(", ", to)}");
    if (cc.Any()) Console.WriteLine($"CC       : {string.Join(", ", cc)}");
    Console.WriteLine($"Received : {(DateTime.TryParse(recvd, out var d) ? d.ToLocalTime().ToString("f") : recvd)}");
    Console.WriteLine($"Status   : {(isRead ? "Read" : "Unread")} | {imp} | Flagged: {flagged} | Attachments: {hasAtt}");
    Console.WriteLine($"ID       : {id}");
    Console.WriteLine("\n─────────────────────────────────────────────────────────────────────");

    if (fullBody)
    {
        var bodyType = msg["body"]?["contentType"]?.GetValue<string>() ?? "text";
        var content  = msg["body"]?["content"]?.GetValue<string>() ?? "";
        if (rawHtml)
        {
            // Output raw HTML — useful for extracting URLs or inspecting formatting
            Console.WriteLine(content);
        }
        else
        {
            if (bodyType == "html")
            {
                content = System.Text.RegularExpressions.Regex.Replace(content, "<[^>]+>", "");
                content = System.Net.WebUtility.HtmlDecode(content);
                content = System.Text.RegularExpressions.Regex.Replace(content, @"\n{3,}", "\n\n");
            }
            Console.WriteLine(content.Trim());
        }
    }
    else
    {
        Console.WriteLine(msg["bodyPreview"]?.GetValue<string>() ?? "");
        Console.WriteLine("\n(Use --full-body for complete message)");
    }

    if (!isRead)
    {
        var patch = new StringContent("""{"isRead":true}""", Encoding.UTF8, "application/json");
        await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}") { Content = patch });
    }
    return 0;
}

async Task<int> MailSend()
{
    var to      = GetArg("--to");
    var subject = GetArg("--subject");
    var cc      = GetArg("--cc");
    var bcc     = GetArg("--bcc");
    var html    = args.Contains("--html");
    var body    = await ResolveBody();

    if (to == null || subject == null || body == null)
    {
        Console.Error.WriteLine("Usage: mail send --to <addr> --subject <s> --body <text> [--cc <a>] [--bcc <a>] [--html] [--body-file <p>] [--attach <path>]");
        return 1;
    }

    var msg = new JsonObject
    {
        ["subject"]         = subject,
        ["body"]            = new JsonObject { ["contentType"] = "html", ["content"] = BodyToHtml(body, html) },
        ["toRecipients"]    = BuildRecipients(to),
        ["ccRecipients"]    = cc  != null ? BuildRecipients(cc)  : new JsonArray(),
        ["bccRecipients"]   = bcc != null ? BuildRecipients(bcc) : new JsonArray(),
    };
    var attachments = BuildAttachments();
    if (attachments != null) msg["attachments"] = attachments;

    var payload = new JsonObject { ["message"] = msg, ["saveToSentItems"] = true };

    var resp = await http.PostAsync($"{GraphBase}/me/sendMail",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Accepted)
    { Console.WriteLine($"Sent to {to}  |  Subject: {subject}"); return 0; }
    return GraphError("send", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> MailReply(bool replyAll)
{
    if (args.Length < 3) { Console.Error.WriteLine($"Usage: mail {(replyAll ? "reply-all" : "reply")} <id> --body <text> [--html] [--body-file <path>]"); return 1; }
    var id   = args[2];
    var html = args.Contains("--html");
    var body = await ResolveBody();
    if (body == null) { Console.Error.WriteLine("Provide --body <text> or --body-file <path>"); return 1; }

    var payload = new JsonObject
    {
        ["message"] = new JsonObject
        {
            ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = BodyToHtml(body, html) }
        }
    };

    var endpoint = replyAll ? $"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}/replyAll"
                             : $"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}/reply";
    var resp = await http.PostAsync(endpoint, new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));

    if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Accepted)
    { Console.WriteLine($"{(replyAll ? "Reply-all" : "Reply")} sent."); return 0; }
    return GraphError("reply", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> MailForward()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: mail forward <id> --to <addr> [--body <text>]"); return 1; }
    var id   = args[2];
    var to   = GetArg("--to");
    if (to == null) { Console.Error.WriteLine("--to is required"); return 1; }
    var body = await ResolveBody() ?? "";
    var html = args.Contains("--html");

    var payload = new JsonObject
    {
        ["toRecipients"] = BuildRecipients(to),
        ["message"] = new JsonObject
        {
            ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = BodyToHtml(body, html) }
        }
    };
    var resp    = await http.PostAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}/forward",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Accepted)
    { Console.WriteLine($"Forwarded to {to}."); return 0; }
    return GraphError("forward", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> MailDelete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: mail delete <id>"); return 1; }
    var resp = await http.DeleteAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(args[2])}");
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Deleted."); return 0; }
    return GraphError("delete", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> MailMove()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: mail move <id> --to <folder>"); return 1; }
    var id     = args[2];
    var folder = GetArg("--to");
    if (folder == null) { Console.Error.WriteLine("--to <folder> is required"); return 1; }

    var folderId = await ResolveFolderIdAsync(folder);
    var payload  = new JsonObject { ["destinationId"] = folderId };
    var resp     = await http.PostAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}/move",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Moved to {folder}."); return 0; }
    return GraphError("move", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> MailSearch()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: mail search <query> [--top <n>]"); return 1; }
    var query = args[2];
    var top   = GetArg("--top") ?? "10";
    var url   = $"{GraphBase}/me/messages?$search={Uri.EscapeDataString($"\"{query}\"")}&$top={top}&$select=id,subject,from,receivedDateTime,isRead,bodyPreview,hasAttachments";

    var req = new HttpRequestMessage(HttpMethod.Get, url);
    req.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
    var resp = await http.SendAsync(req);
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("search", resp.StatusCode, json);

    var data = JsonNode.Parse(json)?["value"]?.AsArray();
    if (data == null || data.Count == 0) { Console.WriteLine($"No results for: {query}"); return 0; }

    Console.WriteLine($"Search: \"{query}\" ({data.Count} results)\n");
    foreach (var msg in data)
    {
        var id      = msg?["id"]?.GetValue<string>() ?? "";
        var subj    = msg?["subject"]?.GetValue<string>() ?? "(no subject)";
        var fromN   = msg?["from"]?["emailAddress"]?["name"]?.GetValue<string>() ?? "";
        var recvd   = msg?["receivedDateTime"]?.GetValue<string>() ?? "";
        var isRead  = msg?["isRead"]?.GetValue<bool>() ?? true;
        var preview = msg?["bodyPreview"]?.GetValue<string>() ?? "";
        var dt      = DateTime.TryParse(recvd, out var d) ? d.ToLocalTime().ToString("MMM dd HH:mm") : recvd;
        Console.WriteLine($"{(isRead ? "  " : "* ")}{dt}  {fromN,-25}  {subj}");
        Console.WriteLine($"      ID: {id}");
        if (!string.IsNullOrWhiteSpace(preview)) Console.WriteLine($"      {preview[..Math.Min(100, preview.Length)]}");
        Console.WriteLine();
    }
    return 0;
}

async Task<int> MailSetFlag(bool flagged)
{
    if (args.Length < 3) { Console.Error.WriteLine($"Usage: mail {(flagged ? "flag" : "unflag")} <id>"); return 1; }
    var payload = new JsonObject { ["flag"] = new JsonObject { ["flagStatus"] = flagged ? "flagged" : "notFlagged" } };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/messages/{Uri.EscapeDataString(args[2])}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Message {(flagged ? "flagged" : "unflagged")}."); return 0; }
    return GraphError("flag", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> MailSetRead(bool isRead)
{
    if (args.Length < 3) { Console.Error.WriteLine($"Usage: mail {(isRead ? "mark-read" : "mark-unread")} <id>"); return 1; }
    var payload = new JsonObject { ["isRead"] = isRead };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/messages/{Uri.EscapeDataString(args[2])}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Marked {(isRead ? "read" : "unread")}."); return 0; }
    return GraphError("mark-read", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> HandleDraft()
{
    if (args.Length < 3) return PrintDraftUsage();
    return args[2].ToLower() switch
    {
        "list"      => await DraftList(),
        "create"    => await DraftCreate(),
        "update"    => await DraftUpdate(),
        "send"      => await DraftSend(),
        "reply"     => await DraftReply(replyAll: false),
        "reply-all" => await DraftReply(replyAll: true),
        _           => PrintDraftUsage(),
    };
}

async Task<int> DraftList()
{
    var top  = GetArg("--top") ?? "10";
    var data = await GetArray($"{GraphBase}/me/mailFolders/drafts/messages?$select=id,subject,toRecipients,lastModifiedDateTime&$top={top}");
    if (data == null || data.Count == 0) { Console.WriteLine("No drafts."); return 0; }
    Console.WriteLine($"Drafts (top {top}):\n");
    foreach (var d in data)
    {
        var id   = d?["id"]?.GetValue<string>() ?? "";
        var subj = d?["subject"]?.GetValue<string>() ?? "(no subject)";
        var mod  = d?["lastModifiedDateTime"]?.GetValue<string>() ?? "";
        var to   = d?["toRecipients"]?.AsArray().Select(r => r?["emailAddress"]?["address"]?.GetValue<string>()) ?? [];
        Console.WriteLine($"  {(DateTime.TryParse(mod, out var dt) ? dt.ToLocalTime().ToString("MMM dd HH:mm") : mod)}  {subj}");
        Console.WriteLine($"    To: {string.Join(", ", to)}  |  ID: {id}\n");
    }
    return 0;
}

async Task<int> DraftCreate()
{
    var to      = GetArg("--to");
    var subject = GetArg("--subject");
    var body    = await ResolveBody() ?? "";
    var html    = args.Contains("--html");
    if (to == null || subject == null) { Console.Error.WriteLine("Usage: mail draft create --to <addr> --subject <s> --body <text>"); return 1; }

    var payload = new JsonObject
    {
        ["subject"]      = subject,
        ["body"]         = new JsonObject { ["contentType"] = "html", ["content"] = BodyToHtml(body, html) },
        ["toRecipients"] = BuildRecipients(to),
    };
    var attachments = BuildAttachments();
    if (attachments != null) payload["attachments"] = attachments;
    var resp = await http.PostAsync($"{GraphBase}/me/messages",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("create draft", resp.StatusCode, json);
    Console.WriteLine($"Draft created. ID: {JsonNode.Parse(json)?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> DraftUpdate()
{
    if (args.Length < 4) { Console.Error.WriteLine("Usage: mail draft update <id> [--to <a>] [--subject <s>] [--body <t>]"); return 1; }
    var id      = args[3];
    var payload = new JsonObject();
    var subject = GetArg("--subject");
    var to      = GetArg("--to");
    var body    = await ResolveBody();
    var html    = args.Contains("--html");
    if (subject != null) payload["subject"] = subject;
    if (to != null)      payload["toRecipients"] = BuildRecipients(to);
    if (body != null)    payload["body"] = new JsonObject { ["contentType"] = "html", ["content"] = BodyToHtml(body, html) };

    var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Draft updated."); return 0; }
    return GraphError("update draft", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> DraftSend()
{
    if (args.Length < 4) { Console.Error.WriteLine("Usage: mail draft send <id>"); return 1; }
    var resp = await http.PostAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(args[3])}/send", new StringContent(""));
    if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Accepted)
    { Console.WriteLine("Draft sent."); return 0; }
    return GraphError("send draft", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> DraftReply(bool replyAll)
{
    var cmd = replyAll ? "reply-all" : "reply";
    if (args.Length < 4) { Console.Error.WriteLine($"Usage: mail draft {cmd} <messageId> [--body <text>] [--body-file <path>] [--html]"); return 1; }
    var id   = args[3];
    var html = args.Contains("--html");
    var body = await ResolveBody();

    // Create the reply/reply-all draft — Graph preserves To/CC/threading headers automatically
    var endpoint = replyAll
        ? $"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}/createReplyAll"
        : $"{GraphBase}/me/messages/{Uri.EscapeDataString(id)}/createReply";

    var createResp = await http.PostAsync(endpoint, new StringContent("{}", Encoding.UTF8, "application/json"));
    var createJson = await createResp.Content.ReadAsStringAsync();
    if (!createResp.IsSuccessStatusCode) return GraphError($"create {cmd} draft", createResp.StatusCode, createJson);

    var draftId = JsonNode.Parse(createJson)?["id"]?.GetValue<string>()
        ?? throw new InvalidOperationException("No draft ID returned");

    // Patch the body if supplied
    if (body != null)
    {
        var patch = new JsonObject
        {
            ["body"] = new JsonObject { ["contentType"] = "html", ["content"] = BodyToHtml(body, html) }
        };
        var updateResp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
            $"{GraphBase}/me/messages/{Uri.EscapeDataString(draftId)}")
            { Content = new StringContent(patch.ToJsonString(), Encoding.UTF8, "application/json") });
        if (!updateResp.IsSuccessStatusCode)
            return GraphError("update reply draft body", updateResp.StatusCode, await updateResp.Content.ReadAsStringAsync());
    }

    Console.WriteLine($"Draft {cmd} created.");
    Console.WriteLine($"  ID: {draftId}");
    return 0;
}

int PrintDraftUsage() { Console.Error.WriteLine("Usage: mail draft <list|create|reply|reply-all|update|send>"); return 1; }

async Task<int> HandleAttachment()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: mail attachment <list|get> ..."); return 1; }
    return args[2].ToLower() switch
    {
        "list" => await AttachmentList(),
        "get"  => await AttachmentGet(),
        _      => PrintAttachmentUsage(),
    };
}

int PrintAttachmentUsage() { Console.Error.WriteLine("Usage: mail attachment <list|get>"); return 1; }

async Task<int> AttachmentList()
{
    if (args.Length < 4) { Console.Error.WriteLine("Usage: mail attachment list <messageId>"); return 1; }
    var data = await GetArray($"{GraphBase}/me/messages/{Uri.EscapeDataString(args[3])}/attachments?$select=id,name,contentType,size");
    if (data == null || data.Count == 0) { Console.WriteLine("No attachments."); return 0; }
    Console.WriteLine("Attachments:\n");
    foreach (var a in data)
        Console.WriteLine($"  {a?["name"]?.GetValue<string>()}  ({a?["contentType"]?.GetValue<string>()}, {FormatBytes(a?["size"]?.GetValue<long>() ?? 0)})\n    ID: {a?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> AttachmentGet()
{
    if (args.Length < 5) { Console.Error.WriteLine("Usage: mail attachment get <messageId> <attachmentId> [--out <path>]"); return 1; }
    var resp = await http.GetAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(args[3])}/attachments/{Uri.EscapeDataString(args[4])}");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("get attachment", resp.StatusCode, json);

    var node         = JsonNode.Parse(json)!;
    var name         = node["name"]?.GetValue<string>() ?? "attachment";
    var contentBytes = node["contentBytes"]?.GetValue<string>();
    if (contentBytes == null) { Console.Error.WriteLine("No content bytes (may be a OneDrive link)."); return 1; }

    var outPath = GetArg("--out") ?? name;
    var bytes   = Convert.FromBase64String(contentBytes);
    await File.WriteAllBytesAsync(outPath, bytes);
    Console.WriteLine($"Saved: {outPath}  ({FormatBytes(bytes.Length)})");
    return 0;
}

int PrintMailUsage()
{
    Console.WriteLine("Usage: mail <subcommand> [options]\n");
    Console.WriteLine("  list          [--folder <name>] [--top <n>] [--unread] [--from <a>] [--subject <s>]");
    Console.WriteLine("  read          <id> [--full-body]");
    Console.WriteLine("  send          --to <a> --subject <s> --body <t> [--cc <a>] [--bcc <a>] [--html] [--body-file <p>]");
    Console.WriteLine("  reply         <id> --body <t> [--html] [--body-file <p>]");
    Console.WriteLine("  reply-all     <id> --body <t>");
    Console.WriteLine("  forward       <id> --to <a> [--body <t>]");
    Console.WriteLine("  delete        <id>");
    Console.WriteLine("  move          <id> --to <folder>");
    Console.WriteLine("  search        <query> [--top <n>]");
    Console.WriteLine("  flag/unflag   <id>");
    Console.WriteLine("  mark-read/mark-unread  <id>");
    Console.WriteLine("  draft list|create|update|send");
    Console.WriteLine("  attachment list|get");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// CALENDAR
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleCalendar()
{
    if (args.Length < 2) return PrintCalendarUsage();
    return args[1].ToLower() switch
    {
        "list"      => await CalendarList(),
        "get"       => await CalendarGet(),
        "create"    => await CalendarCreate(),
        "update"    => await CalendarUpdate(),
        "delete"    => await CalendarDelete(),
        "respond"   => await CalendarRespond(),
        "calendars" => await CalendarsList(),
        _           => PrintCalendarUsage(),
    };
}

async Task<int> CalendarList()
{
    var start   = GetArg("--start") ?? DateTime.Today.ToString("yyyy-MM-dd");
    var end     = GetArg("--end")   ?? DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");
    var top     = GetArg("--top") ?? "15";
    var startDt = DateTime.TryParse(start, out var s) ? s : DateTime.Today;
    var endDt   = DateTime.TryParse(end, out var e) ? e : DateTime.Today.AddDays(30);

    var calBase = GetArg("--calendar") is string calId
        ? $"{GraphBase}/me/calendars/{Uri.EscapeDataString(calId)}/calendarView"
        : $"{GraphBase}/me/calendarView";
    var url  = $"{calBase}?startDateTime={Uri.EscapeDataString(startDt.ToString("o"))}&endDateTime={Uri.EscapeDataString(endDt.ToString("o"))}&$top={top}&$select=id,subject,start,end,location,organizer,isAllDay,isCancelled,responseStatus,attendees,bodyPreview&$orderby=start/dateTime";
    var req  = new HttpRequestMessage(HttpMethod.Get, url);
    req.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
    var resp = await http.SendAsync(req);
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("calendar list", resp.StatusCode, json);

    var data = JsonNode.Parse(json)?["value"]?.AsArray();
    if (data == null || data.Count == 0) { Console.WriteLine($"No events between {start} and {end}."); return 0; }

    Console.WriteLine($"Events ({start} → {end}):\n");
    foreach (var ev in data)
    {
        var id        = ev?["id"]?.GetValue<string>() ?? "";
        var subj      = ev?["subject"]?.GetValue<string>() ?? "(no subject)";
        var startStr  = ev?["start"]?["dateTime"]?.GetValue<string>() ?? "";
        var endStr    = ev?["end"]?["dateTime"]?.GetValue<string>() ?? "";
        var loc       = ev?["location"]?["displayName"]?.GetValue<string>() ?? "";
        var isAllDay  = ev?["isAllDay"]?.GetValue<bool>() ?? false;
        var cancelled = ev?["isCancelled"]?.GetValue<bool>() ?? false;
        var myResp    = ev?["responseStatus"]?["response"]?.GetValue<string>() ?? "";
        var orgName   = ev?["organizer"]?["emailAddress"]?["name"]?.GetValue<string>() ?? "";

        var sd = DateTime.TryParse(startStr, out var sdt) ? sdt : (DateTime?)null;
        var ed = DateTime.TryParse(endStr,   out var edt) ? edt : (DateTime?)null;
        var timeStr = isAllDay
            ? sd?.ToString("MMM dd") ?? startStr
            : $"{sd?.ToString("MMM dd HH:mm") ?? startStr} – {ed?.ToString("HH:mm") ?? endStr}";
        var respMark = myResp switch { "accepted" => " ✓", "declined" => " ✗", "tentativelyAccepted" => " ?", _ => "" };

        Console.WriteLine($"  {timeStr}{(cancelled ? " [CANCELLED]" : "")}{respMark}  {subj}");
        if (!string.IsNullOrWhiteSpace(loc))    Console.WriteLine($"    Location: {loc}");
        if (!string.IsNullOrWhiteSpace(orgName)) Console.WriteLine($"    Organizer: {orgName}");
        Console.WriteLine($"    ID: {id}\n");
    }
    return 0;
}

async Task<int> CalendarGet()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: calendar get <id>"); return 1; }
    var resp = await http.GetAsync($"{GraphBase}/me/events/{Uri.EscapeDataString(args[2])}");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("calendar get", resp.StatusCode, json);

    var ev        = JsonNode.Parse(json)!;
    var isAllDay  = ev["isAllDay"]?.GetValue<bool>() ?? false;
    var attendees = ev["attendees"]?.AsArray()
        .Select(a => $"{a?["emailAddress"]?["name"]?.GetValue<string>()} <{a?["emailAddress"]?["address"]?.GetValue<string>()}> [{a?["status"]?["response"]?.GetValue<string>()}]")
        ?? [];

    Console.WriteLine($"Subject    : {ev["subject"]?.GetValue<string>()}");
    Console.WriteLine($"Start      : {FmtDt(ev["start"]?["dateTime"]?.GetValue<string>() ?? "", isAllDay)}");
    Console.WriteLine($"End        : {FmtDt(ev["end"]?["dateTime"]?.GetValue<string>() ?? "", isAllDay)}");
    if (ev["location"]?["displayName"]?.GetValue<string>() is string loc && !string.IsNullOrWhiteSpace(loc))
        Console.WriteLine($"Location   : {loc}");
    Console.WriteLine($"Organizer  : {ev["organizer"]?["emailAddress"]?["name"]?.GetValue<string>()} <{ev["organizer"]?["emailAddress"]?["address"]?.GetValue<string>()}>");
    Console.WriteLine($"My Response: {ev["responseStatus"]?["response"]?.GetValue<string>()}");
    Console.WriteLine($"Cancelled  : {ev["isCancelled"]?.GetValue<bool>()}");
    Console.WriteLine($"ID         : {args[2]}");
    if (attendees.Any()) { Console.WriteLine("\nAttendees:"); foreach (var a in attendees) Console.WriteLine($"  {a}"); }
    if (ev["bodyPreview"]?.GetValue<string>() is string body && !string.IsNullOrWhiteSpace(body))
        Console.WriteLine($"\nDescription:\n  {body}");
    return 0;
}

async Task<int> CalendarCreate()
{
    var subject   = GetArg("--subject");
    var start     = GetArg("--start");
    var end       = GetArg("--end");
    var location  = GetArg("--location");
    var body      = GetArg("--body");
    var attendees = GetArg("--attendees");
    var allDay    = args.Contains("--all-day");

    var reminderArg = GetArg("--reminder");

    if (subject == null || start == null || end == null)
    { Console.Error.WriteLine("Usage: calendar create --subject <s> --start <dt> --end <dt> [--location <l>] [--body <b>] [--attendees <e,...>] [--all-day] [--reminder <minutes>]"); return 1; }
    if (!DateTime.TryParse(start, out var startDt) || !DateTime.TryParse(end, out var endDt))
    { Console.Error.WriteLine("Invalid date. Use ISO 8601: 2026-04-15T14:00:00"); return 1; }

    var payload = new JsonObject
    {
        ["subject"]  = subject,
        ["isAllDay"] = allDay,
        ["start"]    = new JsonObject { ["dateTime"] = startDt.ToString("o"), ["timeZone"] = TimeZoneInfo.Local.Id },
        ["end"]      = new JsonObject { ["dateTime"] = endDt.ToString("o"),   ["timeZone"] = TimeZoneInfo.Local.Id },
    };
    if (location  != null) payload["location"] = new JsonObject { ["displayName"] = location };
    if (body      != null) payload["body"]      = new JsonObject { ["contentType"] = "text", ["content"] = body };
    if (reminderArg != null && int.TryParse(reminderArg, out var reminderMins))
    {
        payload["isReminderOn"] = true;
        payload["reminderMinutesBeforeStart"] = reminderMins;
    }
    if (attendees != null) payload["attendees"] = new JsonArray(
        attendees.Split(',', StringSplitOptions.TrimEntries)
            .Select(e => (JsonNode)new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = e }, ["type"] = "required" })
            .ToArray());

    var resp = await http.PostAsync(CalendarEventsBase(),
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("create event", resp.StatusCode, json);
    Console.WriteLine($"Event created: {subject}\n  ID: {JsonNode.Parse(json)?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> CalendarUpdate()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: calendar update <id> [--subject <s>] [--start <dt>] [--end <dt>] [--location <l>] [--body <b>] [--reminder <minutes>]"); return 1; }
    var id      = args[2];
    var payload = new JsonObject();
    var subject = GetArg("--subject"); if (subject != null) payload["subject"] = subject;
    var loc     = GetArg("--location"); if (loc != null) payload["location"] = new JsonObject { ["displayName"] = loc };
    var body    = GetArg("--body"); if (body != null) payload["body"] = new JsonObject { ["contentType"] = "text", ["content"] = body };
    var start   = GetArg("--start"); if (start != null && DateTime.TryParse(start, out var sd)) payload["start"] = new JsonObject { ["dateTime"] = sd.ToString("o"), ["timeZone"] = TimeZoneInfo.Local.Id };
    var end     = GetArg("--end");   if (end   != null && DateTime.TryParse(end,   out var ed)) payload["end"]   = new JsonObject { ["dateTime"] = ed.ToString("o"), ["timeZone"] = TimeZoneInfo.Local.Id };
    var reminder = GetArg("--reminder");
    if (reminder != null && int.TryParse(reminder, out var remMins))
    {
        payload["isReminderOn"] = true;
        payload["reminderMinutesBeforeStart"] = remMins;
    }

    var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/events/{Uri.EscapeDataString(id)}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Event updated."); return 0; }
    return GraphError("update event", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> CalendarDelete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: calendar delete <id>"); return 1; }
    var resp = await http.DeleteAsync($"{GraphBase}/me/events/{Uri.EscapeDataString(args[2])}");
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Event deleted."); return 0; }
    return GraphError("delete event", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> CalendarRespond()
{
    if (args.Length < 4) { Console.Error.WriteLine("Usage: calendar respond <id> <accept|tentative|decline> [--comment <c>]"); return 1; }
    var id      = args[2];
    var action  = args[3].ToLower() switch { "accept" => "accept", "tentative" => "tentativelyAccept", "decline" => "decline", _ => null };
    if (action == null) { Console.Error.WriteLine("Action must be: accept, tentative, or decline"); return 1; }

    var payload = new JsonObject { ["sendResponse"] = true, ["comment"] = GetArg("--comment") ?? "" };
    var resp    = await http.PostAsync($"{GraphBase}/me/events/{Uri.EscapeDataString(id)}/{action}",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.Accepted)
    { Console.WriteLine($"Response sent: {args[3]}."); return 0; }
    return GraphError("respond", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> CalendarsList()
{
    var data = await GetArray($"{GraphBase}/me/calendars?$select=id,name,isDefaultCalendar,canEdit,color");
    if (data == null || data.Count == 0) { Console.WriteLine("No calendars found."); return 0; }
    Console.WriteLine("Calendars:\n");
    foreach (var c in data)
    {
        var isDefault = c?["isDefaultCalendar"]?.GetValue<bool>() == true ? " [default]" : "";
        var canEdit   = c?["canEdit"]?.GetValue<bool>() == true ? "" : " [read-only]";
        Console.WriteLine($"  {c?["name"]?.GetValue<string>(),-35}{isDefault}{canEdit}");
        Console.WriteLine($"    ID: {c?["id"]?.GetValue<string>()}");
    }
    return 0;
}

int PrintCalendarUsage()
{
    Console.WriteLine("Usage: calendar <subcommand>\n");
    Console.WriteLine("  calendars                                     List all calendars");
    Console.WriteLine("  list    [--start <date>] [--end <date>] [--top <n>] [--calendar <id>]");
    Console.WriteLine("  get     <id>");
    Console.WriteLine("  create  --subject <s> --start <dt> --end <dt> [--location <l>] [--body <b>] [--attendees <e,...>] [--all-day] [--calendar <id>]");
    Console.WriteLine("  update  <id> [--subject <s>] [--start <dt>] [--end <dt>] [--location <l>] [--body <b>]");
    Console.WriteLine("  delete  <id>");
    Console.WriteLine("  respond <id> <accept|tentative|decline> [--comment <c>]");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// CONTACTS
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleContacts()
{
    if (args.Length < 2) return PrintContactsUsage();
    return args[1].ToLower() switch
    {
        "list"   => await ContactsList(),
        "search" => await ContactsSearch(),
        "get"    => await ContactsGet(),
        "create" => await ContactsCreate(),
        "update" => await ContactsUpdate(),
        "delete" => await ContactsDelete(),
        _        => PrintContactsUsage(),
    };
}

async Task<int> ContactsList()
{
    var top  = GetArg("--top") ?? "20";
    var data = await GetArray($"{GraphBase}/me/contacts?$top={top}&$select=id,displayName,emailAddresses,mobilePhone,businessPhones,companyName&$orderby=displayName");
    if (data == null || data.Count == 0) { Console.WriteLine("No contacts."); return 0; }
    Console.WriteLine($"Contacts (top {top}):\n");
    foreach (var c in data) PrintContact(c);
    return 0;
}

async Task<int> ContactsSearch()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: contacts search <query>"); return 1; }
    var data = await GetArray($"{GraphBase}/me/contacts?$search={Uri.EscapeDataString($"\"{args[2]}\"")}&$top=20&$select=id,displayName,emailAddresses,mobilePhone,businessPhones,companyName");
    if (data == null || data.Count == 0) { Console.WriteLine($"No results for: {args[2]}"); return 0; }
    Console.WriteLine($"Results for \"{args[2]}\":\n");
    foreach (var c in data) PrintContact(c);
    return 0;
}

async Task<int> ContactsGet()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: contacts get <id>"); return 1; }
    var resp = await http.GetAsync($"{GraphBase}/me/contacts/{Uri.EscapeDataString(args[2])}");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("get contact", resp.StatusCode, json);

    var c     = JsonNode.Parse(json)!;
    var emails = c["emailAddresses"]?.AsArray().Select(e => $"{e?["name"]?.GetValue<string>()} <{e?["address"]?.GetValue<string>()}>".Trim()) ?? [];
    var bizPh  = c["businessPhones"]?.AsArray().Select(p => p?.GetValue<string>()).Where(p => p != null) ?? [];

    Console.WriteLine($"Name       : {c["displayName"]?.GetValue<string>()}");
    if (c["jobTitle"]?.GetValue<string>()  is string jt  && !string.IsNullOrWhiteSpace(jt))  Console.WriteLine($"Title      : {jt}");
    if (c["companyName"]?.GetValue<string>() is string co && !string.IsNullOrWhiteSpace(co))  Console.WriteLine($"Company    : {co}");
    if (emails.Any())  Console.WriteLine($"Email      : {string.Join(", ", emails)}");
    if (c["mobilePhone"]?.GetValue<string>() is string mp && !string.IsNullOrWhiteSpace(mp))  Console.WriteLine($"Mobile     : {mp}");
    if (bizPh.Any())   Console.WriteLine($"Work Phone : {string.Join(", ", bizPh)}");
    Console.WriteLine($"ID         : {args[2]}");
    return 0;
}

async Task<int> ContactsCreate()
{
    var first   = GetArg("--first");
    var last    = GetArg("--last");
    var email   = GetArg("--email");
    var phone   = GetArg("--phone");
    var company = GetArg("--company");
    var title   = GetArg("--title");

    if (first == null && last == null)
    { Console.Error.WriteLine("Usage: contacts create --first <fn> --last <ln> [--email <e>] [--phone <p>] [--company <c>] [--title <t>]"); return 1; }

    var payload = new JsonObject { ["givenName"] = first ?? "", ["surname"] = last ?? "" };
    if (company != null) payload["companyName"] = company;
    if (title   != null) payload["jobTitle"]    = title;
    if (phone   != null) payload["mobilePhone"] = phone;
    if (email   != null) payload["emailAddresses"] = new JsonArray(new JsonObject { ["address"] = email, ["name"] = $"{first} {last}".Trim() });

    var resp = await http.PostAsync($"{GraphBase}/me/contacts",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("create contact", resp.StatusCode, json);
    Console.WriteLine($"Contact created: {first} {last}  |  ID: {JsonNode.Parse(json)?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> ContactsUpdate()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: contacts update <id> [--first <fn>] [--last <ln>] [--email <e>] [--phone <p>] [--company <c>] [--title <t>]"); return 1; }
    var id      = args[2];
    var payload = new JsonObject();
    var first   = GetArg("--first");   if (first   != null) payload["givenName"]   = first;
    var last    = GetArg("--last");    if (last    != null) payload["surname"]      = last;
    var phone   = GetArg("--phone");   if (phone   != null) payload["mobilePhone"] = phone;
    var company = GetArg("--company"); if (company != null) payload["companyName"] = company;
    var title   = GetArg("--title");   if (title   != null) payload["jobTitle"]    = title;
    var email   = GetArg("--email");
    if (email != null)
    {
        var displayName = $"{first ?? ""} {last ?? ""}".Trim();
        payload["emailAddresses"] = new JsonArray(new JsonObject { ["address"] = email, ["name"] = displayName });
    }
    if (payload.Count == 0) { Console.Error.WriteLine("Provide at least one field to update."); return 1; }

    var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
        $"{GraphBase}/me/contacts/{Uri.EscapeDataString(id)}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Contact updated."); return 0; }
    return GraphError("update contact", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> ContactsDelete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: contacts delete <id>"); return 1; }
    var resp = await http.DeleteAsync($"{GraphBase}/me/contacts/{Uri.EscapeDataString(args[2])}");
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Contact deleted."); return 0; }
    return GraphError("delete contact", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

void PrintContact(JsonNode? c)
{
    var emails = c?["emailAddresses"]?.AsArray().Select(e => e?["address"]?.GetValue<string>()).Where(e => e != null) ?? [];
    Console.WriteLine($"  {c?["displayName"]?.GetValue<string>() ?? "(no name)"}{(c?["companyName"]?.GetValue<string>() is string co && !string.IsNullOrWhiteSpace(co) ? $" — {co}" : "")}");
    if (emails.Any()) Console.WriteLine($"    Email: {string.Join(", ", emails)}");
    if (c?["mobilePhone"]?.GetValue<string>() is string mp && !string.IsNullOrWhiteSpace(mp)) Console.WriteLine($"    Phone: {mp}");
    Console.WriteLine($"    ID: {c?["id"]?.GetValue<string>()}\n");
}

int PrintContactsUsage()
{
    Console.WriteLine("Usage: contacts <subcommand>\n");
    Console.WriteLine("  list    [--top <n>]");
    Console.WriteLine("  search  <query>");
    Console.WriteLine("  get     <id>");
    Console.WriteLine("  create  --first <fn> --last <ln> [--email <e>] [--phone <p>] [--company <c>] [--title <t>]");
    Console.WriteLine("  update  <id> [--first <fn>] [--last <ln>] [--email <e>] [--phone <p>] [--company <c>] [--title <t>]");
    Console.WriteLine("  delete  <id>");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// FOLDERS
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleFolders()
{
    if (args.Length < 2) return PrintFoldersUsage();
    return args[1].ToLower() switch
    {
        "list"   => await FoldersList(),
        "create" => await FoldersCreate(),
        "rename" => await FoldersRename(),
        "delete" => await FoldersDelete(),
        _        => PrintFoldersUsage(),
    };
}

async Task<int> FoldersList()
{
    var parentId = GetArg("--parent");
    var url      = parentId != null
        ? $"{GraphBase}/me/mailFolders/{Uri.EscapeDataString(parentId)}/childFolders?$select=id,displayName,totalItemCount,unreadItemCount&$top=50"
        : $"{GraphBase}/me/mailFolders?$select=id,displayName,totalItemCount,unreadItemCount&$top=50";
    var data     = await GetArray(url);
    if (data == null || data.Count == 0) { Console.WriteLine("No folders."); return 0; }
    Console.WriteLine(parentId != null ? $"Child folders of {parentId}:\n" : "Mail folders:\n");
    foreach (var f in data)
    {
        var unread = f?["unreadItemCount"]?.GetValue<int>() ?? 0;
        Console.WriteLine($"  {f?["displayName"]?.GetValue<string>(),-30} {f?["totalItemCount"]?.GetValue<int>(),4} items{(unread > 0 ? $" ({unread} unread)" : "")}");
        Console.WriteLine($"    ID: {f?["id"]?.GetValue<string>()}");
    }
    return 0;
}

async Task<int> FoldersCreate()
{
    var name     = GetArg("--name");
    var parentId = GetArg("--parent");
    if (name == null) { Console.Error.WriteLine("Usage: folders create --name <n> [--parent <id>]"); return 1; }

    var url     = parentId != null
        ? $"{GraphBase}/me/mailFolders/{Uri.EscapeDataString(parentId)}/childFolders"
        : $"{GraphBase}/me/mailFolders";
    var resp    = await http.PostAsync(url,
        new StringContent(new JsonObject { ["displayName"] = name }.ToJsonString(), Encoding.UTF8, "application/json"));
    var json    = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("create folder", resp.StatusCode, json);
    Console.WriteLine($"Folder created: {name}  |  ID: {JsonNode.Parse(json)?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> FoldersRename()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: folders rename <id> --name <n>"); return 1; }
    var name = GetArg("--name");
    if (name == null) { Console.Error.WriteLine("--name is required"); return 1; }
    var resp = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/mailFolders/{Uri.EscapeDataString(args[2])}")
        { Content = new StringContent(new JsonObject { ["displayName"] = name }.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Folder renamed to: {name}"); return 0; }
    return GraphError("rename folder", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> FoldersDelete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: folders delete <id>"); return 1; }
    var resp = await http.DeleteAsync($"{GraphBase}/me/mailFolders/{Uri.EscapeDataString(args[2])}");
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Folder deleted (and all its contents)."); return 0; }
    return GraphError("delete folder", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

int PrintFoldersUsage()
{
    Console.WriteLine("Usage: folders <subcommand>\n");
    Console.WriteLine("  list    [--parent <id>]");
    Console.WriteLine("  create  --name <n> [--parent <id>]");
    Console.WriteLine("  rename  <id> --name <n>");
    Console.WriteLine("  delete  <id>");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// SHARED HELPERS
// ══════════════════════════════════════════════════════════════════════════════

async Task<string> GetValidToken()
{
    if (!File.Exists(TokenFile))
        throw new InvalidOperationException("Not logged in. Run: /outlook-api auth login");

    var token = JsonSerializer.Deserialize<TokenResponse>(File.ReadAllText(TokenFile), JsonOpts)!;
    if (DateTime.UtcNow < token.ExpiresAt) return token.AccessToken;

    if (token.RefreshToken == null)
        throw new InvalidOperationException("Token expired. Run: /outlook-api auth login");

    var clientId = store.Get($"{CredBase}/client-id", AccountName)?.Password
        ?? throw new InvalidOperationException("Client ID not found. Run: /outlook-api auth setup");

    using var h = new HttpClient();
    var resp    = await h.PostAsync($"{AuthBase}/token",
        new FormUrlEncodedContent([
            new("grant_type",    "refresh_token"),
            new("refresh_token", token.RefreshToken),
            new("client_id",     clientId),
            new("scope",         Scopes),
        ]));

    var newJson = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
        throw new InvalidOperationException($"Token refresh failed: {newJson}");

    var newToken = JsonSerializer.Deserialize<TokenResponse>(newJson, JsonOpts)!;
    newToken.ExpiresAt = DateTime.UtcNow.AddSeconds(newToken.ExpiresIn - 60);
    SaveToken(newToken);
    return newToken.AccessToken;
}

void SaveToken(TokenResponse token)
{
    Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!);
    File.WriteAllText(TokenFile, JsonSerializer.Serialize(token, JsonOpts));
}

TokenResponse? LoadToken() =>
    File.Exists(TokenFile)
        ? JsonSerializer.Deserialize<TokenResponse>(File.ReadAllText(TokenFile), JsonOpts)
        : null;

async Task<JsonArray?> GetArray(string url)
{
    var resp = await http.GetAsync(url);
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) { GraphError("request", resp.StatusCode, json); return null; }
    return JsonNode.Parse(json)?["value"]?.AsArray();
}

async Task<string?> ResolveFolderIdAsync(string nameOrId)
{
    var wellKnown = nameOrId.ToLower() switch
    {
        "inbox" => "inbox", "drafts" => "drafts", "sent" or "sentitems" => "sentItems",
        "deleted" or "deleteditems" => "deleteditems", "junk" or "junkemail" => "junkEmail",
        "outbox" => "outbox", _ => null,
    };
    if (wellKnown != null)
    {
        var r = await http.GetAsync($"{GraphBase}/me/mailFolders/{wellKnown}");
        if (r.IsSuccessStatusCode) return JsonNode.Parse(await r.Content.ReadAsStringAsync())?["id"]?.GetValue<string>();
    }
    var resp = await http.GetAsync($"{GraphBase}/me/mailFolders?$filter=displayName eq '{OData(nameOrId)}'&$select=id");
    if (resp.IsSuccessStatusCode)
    {
        var arr = JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["value"]?.AsArray();
        if (arr != null && arr.Count > 0) return arr[0]?["id"]?.GetValue<string>();
    }
    return nameOrId; // assume it's already an ID
}

string WellKnownFolder(string name) => name.ToLower() switch
{
    "inbox"        => "mailFolders/inbox",
    "drafts"       => "mailFolders/drafts",
    "sent"         => "mailFolders/sentItems",
    "sentitems"    => "mailFolders/sentItems",
    "deleted"      => "mailFolders/deleteditems",
    "deleteditems" => "mailFolders/deleteditems",
    "junk"         => "mailFolders/junkEmail",
    "junkemail"    => "mailFolders/junkEmail",
    "outbox"       => "mailFolders/outbox",
    _              => $"mailFolders/{Uri.EscapeDataString(name)}",
};

JsonArray BuildRecipients(string addresses) =>
    new(addresses.Split(',', StringSplitOptions.TrimEntries)
        .Select(addr => (JsonNode)new JsonObject { ["emailAddress"] = new JsonObject { ["address"] = addr.Trim() } })
        .ToArray());

async Task<string?> ResolveBody()
{
    var file = GetArg("--body-file");
    if (file != null) return File.Exists(file) ? await File.ReadAllTextAsync(file) : null;
    return GetArg("--body");
}

// Build file attachment array from one or more --attach <path> args.
// Returns null if no --attach args present. Errors on files >4 MB.
JsonArray? BuildAttachments()
{
    var paths = new List<string>();
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == "--attach") paths.Add(args[i + 1]);
    if (paths.Count == 0) return null;

    var arr = new JsonArray();
    foreach (var path in paths)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Attachment not found: {path}");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 4 * 1024 * 1024)
            throw new InvalidOperationException($"File '{path}' exceeds 4 MB. Use OneDrive link instead.");
        arr.Add(new JsonObject
        {
            ["@odata.type"]  = "#microsoft.graph.fileAttachment",
            ["name"]         = Path.GetFileName(path),
            ["contentType"]  = "application/octet-stream",
            ["contentBytes"] = Convert.ToBase64String(bytes),
        });
    }
    return arr;
}

// Resolve --calendar <id> to the correct events base URL.
string CalendarEventsBase() {
    var cal = GetArg("--calendar");
    return cal != null
        ? $"{GraphBase}/me/calendars/{Uri.EscapeDataString(cal)}/events"
        : $"{GraphBase}/me/events";
}

// Convert plain text to HTML, preserving paragraph breaks and line breaks.
// Used whenever sending body content so Outlook renders newlines correctly.
string BodyToHtml(string text, bool alreadyHtml = false)
{
    if (alreadyHtml) return text;
    var escaped = text
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\r\n", "\n")
        .Replace("\n\n", "</p><p>")
        .Replace("\n", "<br>");
    return $"<html><body><p>{escaped}</p></body></html>";
}

string? GetArg(string flag)
{
    var idx = Array.IndexOf(args, flag);
    return idx >= 0 && idx + 1 < args.Length ? args[idx + 1] : null;
}

int GraphError(string op, System.Net.HttpStatusCode code, string body)
{
    Console.Error.WriteLine($"Graph API error in '{op}' ({(int)code} {code}):");
    try   { Console.Error.WriteLine($"  {JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body}"); }
    catch { Console.Error.WriteLine($"  {body}"); }
    return 1;
}

string OData(string s) => s.Replace("'", "''");
string FormatBytes(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:F1} KB" : $"{b / 1048576.0:F1} MB";
string FmtDt(string iso, bool allDay) => DateTime.TryParse(iso, out var d) ? (allDay ? d.ToString("MMM dd, yyyy") : d.ToString("MMM dd, yyyy HH:mm")) : iso;
string Base64UrlEncode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

Dictionary<string, string> ParseQueryString(string q) =>
    q.Split('&', StringSplitOptions.RemoveEmptyEntries)
     .Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
     .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

async Task WriteHtmlResponse(HttpListenerResponse r, bool ok, string msg)
{
    var html  = ok
        ? $"<html><body style='font-family:sans-serif;padding:2em;color:#1a7f37'><h2>&#10003; {msg}</h2></body></html>"
        : $"<html><body style='font-family:sans-serif;padding:2em;color:#d1242f'><h2>&#10007; Failed</h2><p>{msg}</p></body></html>";
    var bytes = Encoding.UTF8.GetBytes(html);
    r.ContentType = "text/html; charset=utf-8"; r.ContentLength64 = bytes.Length;
    await r.OutputStream.WriteAsync(bytes); r.Close();
}

// ══════════════════════════════════════════════════════════════════════════════
// TASKS (Microsoft To-Do)
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleTasks()
{
    if (args.Length < 2) return PrintTasksUsage();
    return args[1].ToLower() switch
    {
        "lists"    => await TasksLists(),
        "list"     => await TasksList(),
        "get"      => await TasksGet(),
        "create"   => await TasksCreate(),
        "complete" => await TasksComplete(),
        "delete"   => await TasksDelete(),
        _          => PrintTasksUsage(),
    };
}

async Task<int> TasksLists()
{
    var data = await GetArray($"{GraphBase}/me/todo/lists?$select=id,displayName,isOwner,wellknownListName");
    if (data == null || data.Count == 0) { Console.WriteLine("No task lists found."); return 0; }
    Console.WriteLine("Task lists:\n");
    foreach (var l in data)
        Console.WriteLine($"  {l?["displayName"]?.GetValue<string>(),-30}  ID: {l?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> TasksList()
{
    var listId = GetArg("--list") ?? "Tasks";
    var top    = GetArg("--top") ?? "20";
    var filter = args.Contains("--completed") ? "&$filter=status eq 'completed'" : "&$filter=status ne 'completed'";
    var data   = await GetArray($"{GraphBase}/me/todo/lists/{Uri.EscapeDataString(listId)}/tasks?$top={top}&$select=id,title,status,importance,dueDateTime,body{filter}");
    if (data == null || data.Count == 0) { Console.WriteLine("No tasks found."); return 0; }
    Console.WriteLine($"Tasks (list: {listId}):\n");
    foreach (var t in data)
    {
        var done      = t?["status"]?.GetValue<string>() == "completed";
        var important = t?["importance"]?.GetValue<string>() == "high";
        var due       = t?["dueDateTime"]?["dateTime"]?.GetValue<string>();
        var dueStr    = due != null && DateTime.TryParse(due, out var d) ? $"  due {d:MMM dd}" : "";
        Console.WriteLine($"  {(done ? "[x]" : "[ ]")} {(important ? "! " : "  ")}{t?["title"]?.GetValue<string>()}{dueStr}");
        Console.WriteLine($"      ID: {t?["id"]?.GetValue<string>()}");
    }
    return 0;
}

async Task<int> TasksGet()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: tasks get <taskId> [--list <listId>]"); return 1; }
    var listId = GetArg("--list") ?? "Tasks";
    var resp   = await http.GetAsync($"{GraphBase}/me/todo/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(args[2])}");
    var json   = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("get task", resp.StatusCode, json);
    var t = JsonNode.Parse(json)!;
    Console.WriteLine($"Title     : {t["title"]?.GetValue<string>()}");
    Console.WriteLine($"Status    : {t["status"]?.GetValue<string>()}");
    Console.WriteLine($"Importance: {t["importance"]?.GetValue<string>()}");
    if (t["dueDateTime"]?["dateTime"]?.GetValue<string>() is string due && DateTime.TryParse(due, out var d))
        Console.WriteLine($"Due       : {d:f}");
    if (t["body"]?["content"]?.GetValue<string>() is string body && !string.IsNullOrWhiteSpace(body))
        Console.WriteLine($"Notes     : {body}");
    Console.WriteLine($"ID        : {args[2]}");
    return 0;
}

async Task<int> TasksCreate()
{
    var title = GetArg("--title");
    if (title == null) { Console.Error.WriteLine("Usage: tasks create --title <t> [--list <listId>] [--due <date>] [--body <notes>] [--important]"); return 1; }
    var listId    = GetArg("--list") ?? "Tasks";
    var due       = GetArg("--due");
    var body      = GetArg("--body");
    var important = args.Contains("--important");

    var payload = new JsonObject { ["title"] = title };
    if (important) payload["importance"] = "high";
    if (body != null) payload["body"] = new JsonObject { ["contentType"] = "text", ["content"] = body };
    if (due != null && DateTime.TryParse(due, out var dueDate))
        payload["dueDateTime"] = new JsonObject { ["dateTime"] = dueDate.ToString("o"), ["timeZone"] = TimeZoneInfo.Local.Id };

    var resp = await http.PostAsync($"{GraphBase}/me/todo/lists/{Uri.EscapeDataString(listId)}/tasks",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("create task", resp.StatusCode, json);
    Console.WriteLine($"Task created: {title}");
    Console.WriteLine($"  ID: {JsonNode.Parse(json)?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> TasksComplete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: tasks complete <taskId> [--list <listId>]"); return 1; }
    var listId  = GetArg("--list") ?? "Tasks";
    var payload = new JsonObject { ["status"] = "completed" };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch,
        $"{GraphBase}/me/todo/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(args[2])}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Task marked complete."); return 0; }
    return GraphError("complete task", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> TasksDelete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: tasks delete <taskId> [--list <listId>]"); return 1; }
    var listId = GetArg("--list") ?? "Tasks";
    var resp   = await http.DeleteAsync($"{GraphBase}/me/todo/lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(args[2])}");
    if (resp.IsSuccessStatusCode) { Console.WriteLine("Task deleted."); return 0; }
    return GraphError("delete task", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

int PrintTasksUsage()
{
    Console.WriteLine("Usage: tasks <subcommand>\n");
    Console.WriteLine("  lists                                          List all task lists");
    Console.WriteLine("  list    [--list <listId>] [--top <n>] [--completed]");
    Console.WriteLine("  get     <taskId> [--list <listId>]");
    Console.WriteLine("  create  --title <t> [--list <id>] [--due <date>] [--body <notes>] [--important]");
    Console.WriteLine("  complete <taskId> [--list <listId>]");
    Console.WriteLine("  delete  <taskId> [--list <listId>]");
    Console.WriteLine("\nDefault list: 'Tasks'. Pass --list <id> to target another list.");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// SETTINGS (Mailbox Settings)
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleSettings()
{
    if (args.Length < 2) return PrintSettingsUsage();
    return args[1].ToLower() switch
    {
        "get"        => await SettingsGet(),
        "timezone"   => await SettingsSetTimezone(),
        "auto-reply" => await SettingsAutoReply(),
        _            => PrintSettingsUsage(),
    };
}

async Task<int> SettingsGet()
{
    var resp = await http.GetAsync($"{GraphBase}/me/mailboxSettings");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return GraphError("get settings", resp.StatusCode, json);
    var s      = JsonNode.Parse(json)!;
    var ar     = s["automaticRepliesSetting"];
    var arStatus = ar?["status"]?.GetValue<string>() ?? "disabled";
    Console.WriteLine($"Timezone      : {s["timeZone"]?.GetValue<string>()}");
    Console.WriteLine($"Language      : {s["language"]?["displayName"]?.GetValue<string>()} ({s["language"]?["locale"]?.GetValue<string>()})");
    Console.WriteLine($"Auto-reply    : {arStatus}");
    if (arStatus != "disabled")
    {
        Console.WriteLine($"  Internal msg: {ar?["internalReplyMessage"]?.GetValue<string>()?.Split('\n')[0]}");
        if (arStatus == "scheduled")
        {
            Console.WriteLine($"  Start        : {ar?["scheduledStartDateTime"]?["dateTime"]?.GetValue<string>()}");
            Console.WriteLine($"  End          : {ar?["scheduledEndDateTime"]?["dateTime"]?.GetValue<string>()}");
        }
    }
    var wh = s["workingHours"];
    if (wh != null)
    {
        var days = wh["daysOfWeek"]?.AsArray().Select(d => d?.GetValue<string>()) ?? [];
        Console.WriteLine($"Working hours : {wh["startTime"]?.GetValue<string>()} - {wh["endTime"]?.GetValue<string>()} ({string.Join(", ", days)})");
    }
    return 0;
}

async Task<int> SettingsSetTimezone()
{
    var tz = args.Length >= 3 ? args[2] : null;
    if (tz == null) { Console.Error.WriteLine("Usage: settings timezone <TimeZoneId>  e.g. \"Central Standard Time\""); return 1; }
    var payload = new JsonObject { ["timeZone"] = tz };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/mailboxSettings")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Timezone set to: {tz}"); return 0; }
    return GraphError("set timezone", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> SettingsAutoReply()
{
    if (args.Length < 3) return PrintSettingsUsage();
    var action = args[2].ToLower();

    JsonObject arSetting;
    if (action == "disable")
    {
        arSetting = new JsonObject { ["status"] = "disabled" };
    }
    else if (action == "enable")
    {
        var msg      = GetArg("--message") ?? GetArg("--msg");
        var extMsg   = GetArg("--external-message") ?? msg;
        var start    = GetArg("--start");
        var end      = GetArg("--end");
        if (msg == null) { Console.Error.WriteLine("--message <text> is required"); return 1; }

        arSetting = new JsonObject
        {
            ["status"]               = (start != null && end != null) ? "scheduled" : "alwaysEnabled",
            ["internalReplyMessage"] = msg,
            ["externalReplyMessage"] = extMsg,
        };
        if (start != null && DateTime.TryParse(start, out var sd))
            arSetting["scheduledStartDateTime"] = new JsonObject { ["dateTime"] = sd.ToString("o"), ["timeZone"] = TimeZoneInfo.Local.Id };
        if (end != null && DateTime.TryParse(end, out var ed))
            arSetting["scheduledEndDateTime"] = new JsonObject { ["dateTime"] = ed.ToString("o"), ["timeZone"] = TimeZoneInfo.Local.Id };
    }
    else { return PrintSettingsUsage(); }

    var payload = new JsonObject { ["automaticRepliesSetting"] = arSetting };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/mailboxSettings")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Auto-reply {action}d."); return 0; }
    return GraphError("set auto-reply", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

int PrintSettingsUsage()
{
    Console.WriteLine("Usage: settings <subcommand>\n");
    Console.WriteLine("  get");
    Console.WriteLine("  timezone <TimeZoneId>                         e.g. \"Central Standard Time\"");
    Console.WriteLine("  auto-reply enable --message <text> [--external-message <text>] [--start <dt>] [--end <dt>]");
    Console.WriteLine("  auto-reply disable");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// CATEGORIES (mail categories handler — called from HandleMail)
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleCategories()
{
    if (args.Length < 3) return PrintCategoriesUsage();
    return args[2].ToLower() switch
    {
        "list"   => await CategoriesList(),
        "apply"  => await CategoriesApply(),
        "remove" => await CategoriesRemove(),
        _        => PrintCategoriesUsage(),
    };
}

async Task<int> CategoriesList()
{
    var data = await GetArray($"{GraphBase}/me/outlook/masterCategories");
    if (data == null || data.Count == 0) { Console.WriteLine("No categories defined."); return 0; }
    Console.WriteLine("Categories:\n");
    foreach (var c in data)
        Console.WriteLine($"  {c?["displayName"]?.GetValue<string>(),-30}  color: {c?["color"]?.GetValue<string>()}  ID: {c?["id"]?.GetValue<string>()}");
    return 0;
}

async Task<int> CategoriesApply()
{
    if (args.Length < 5) { Console.Error.WriteLine("Usage: mail categories apply <messageId> <categoryName>"); return 1; }
    var msgId    = args[3];
    var category = args[4];

    // Fetch existing categories first so we don't overwrite them
    var getResp = await http.GetAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(msgId)}?$select=categories");
    var getJson = await getResp.Content.ReadAsStringAsync();
    if (!getResp.IsSuccessStatusCode) return GraphError("get message categories", getResp.StatusCode, getJson);
    var existing = JsonNode.Parse(getJson)?["categories"]?.AsArray().Select(c => c?.GetValue<string>()).Where(c => c != null).ToList() ?? new List<string?>();
    if (!existing.Contains(category)) existing.Add(category);

    var payload = new JsonObject { ["categories"] = new JsonArray(existing.Select(c => (JsonNode)(c ?? "")).ToArray()) };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/messages/{Uri.EscapeDataString(msgId)}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Category '{category}' applied."); return 0; }
    return GraphError("apply category", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> CategoriesRemove()
{
    if (args.Length < 5) { Console.Error.WriteLine("Usage: mail categories remove <messageId> <categoryName>"); return 1; }
    var msgId    = args[3];
    var category = args[4];

    var getResp = await http.GetAsync($"{GraphBase}/me/messages/{Uri.EscapeDataString(msgId)}?$select=categories");
    var getJson = await getResp.Content.ReadAsStringAsync();
    if (!getResp.IsSuccessStatusCode) return GraphError("get message categories", getResp.StatusCode, getJson);
    var remaining = JsonNode.Parse(getJson)?["categories"]?.AsArray()
        .Select(c => c?.GetValue<string>()).Where(c => c != null && c != category).ToList() ?? new List<string?>();

    var payload = new JsonObject { ["categories"] = new JsonArray(remaining.Select(c => (JsonNode)(c ?? "")).ToArray()) };
    var resp    = await http.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"{GraphBase}/me/messages/{Uri.EscapeDataString(msgId)}")
        { Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json") });
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Category '{category}' removed."); return 0; }
    return GraphError("remove category", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

int PrintCategoriesUsage()
{
    Console.WriteLine("Usage: mail categories <subcommand>\n");
    Console.WriteLine("  list");
    Console.WriteLine("  apply  <messageId> <categoryName>");
    Console.WriteLine("  remove <messageId> <categoryName>");
    return 1;
}

int PrintUsage()
{
    Console.WriteLine("Usage: /outlook-api <domain> <subcommand> [options]\n");
    Console.WriteLine("  auth      setup|login|refresh|status|logout");
    Console.WriteLine("  mail      list|read|send|reply|reply-all|forward|delete|move|search|flag|draft|attachment|categories");
    Console.WriteLine("  calendar  list|get|create|update|delete|respond|calendars");
    Console.WriteLine("  contacts  list|search|get|create|update|delete");
    Console.WriteLine("  folders   list|create|rename|delete");
    Console.WriteLine("  tasks     lists|list|get|create|complete|delete");
    Console.WriteLine("  settings  get|timezone|auto-reply\n");
    Console.WriteLine("Examples:");
    Console.WriteLine("  /outlook-api mail list --unread");
    Console.WriteLine("  /outlook-api mail send --to you@example.com --subject 'Hi' --body 'Hello' --attach file.pdf");
    Console.WriteLine("  /outlook-api calendar list --start 2026-04-13 --end 2026-04-20");
    Console.WriteLine("  /outlook-api tasks create --title 'Prep slides' --due 2026-04-19 --important");
    Console.WriteLine("  /outlook-api settings auto-reply enable --message 'Out of office'");
    return 1;
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

class MeResponse
{
    [JsonPropertyName("displayName")]       public string? DisplayName      { get; set; }
    [JsonPropertyName("mail")]              public string? Mail              { get; set; }
    [JsonPropertyName("userPrincipalName")] public string? UserPrincipalName { get; set; }
}
