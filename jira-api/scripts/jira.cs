#:package Devlooped.CredentialManager@*
#pragma warning disable IL2026,IL3050  // Suppress JSON serialization trimming warnings

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GitCredentialManager;

// ── Constants ──────────────────────────────────────────────────────────────────

const string ServiceName = "jira-api";
const string AccountName  = "jira";
// The Windows Credential Manager backend parses the service name as a URI, so the
// credential keys must be full URIs (matching the outlook-api skill's pattern).
const string CredBase = "https://api.atlassian.com/jira-api";

var store = CredentialManager.Create(ServiceName);

// ── Top-level routing ────────────────────────────────────────────────────────────

if (args.Length == 0) return PrintUsage();

// Auth commands manage credentials — handle before building an authenticated client
if (args[0].ToLower() == "auth") return HandleAuth();

// All other commands need stored credentials
var (site, email, token) = LoadCreds();
if (site == null || email == null || token == null)
{
    Console.Error.WriteLine("Not configured. Run: auth setup --site <site> --email <you@example.com> --token <api-token>");
    Console.Error.WriteLine("See references/auth-guide.md for how to mint a Jira API token.");
    return 1;
}

var baseUrl = $"https://{site}.atlassian.net/rest/api/3";
var agileUrl = $"https://{site}.atlassian.net/rest/agile/1.0";
var http = new HttpClient();
var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}"));
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

// Process cache for the Story Points custom-field id(s); populated lazily by StoryPointFieldIds().
// Declared here (before the command runs) so the capturing local function sees it definitely assigned.
List<string>? _spFieldIds = null;

return args[0].ToLower() switch
{
    "projects" => await ProjectsList(),
    "issue"    => await HandleIssue(),
    "sprint"   => await HandleSprint(),
    _          => PrintUsage(),
};

// ══════════════════════════════════════════════════════════════════════════════
// AUTH
// ══════════════════════════════════════════════════════════════════════════════

int HandleAuth()
{
    if (args.Length < 2) return PrintAuthUsage();
    return args[1].ToLower() switch
    {
        "setup"  => AuthSetup(),
        "status" => AuthStatus(),
        "logout" => AuthLogout(),
        _        => PrintAuthUsage(),
    };
}

int AuthSetup()
{
    var site  = GetArg("--site");
    var email = GetArg("--email");
    var token = GetArg("--token");

    if (site == null || email == null || token == null)
    {
        Console.WriteLine("Usage: auth setup --site <site> --email <you@example.com> --token <api-token>");
        Console.WriteLine();
        Console.WriteLine("  --site   The subdomain of your Jira Cloud URL.");
        Console.WriteLine("           For https://acme.atlassian.net, the site is 'acme'.");
        Console.WriteLine("  --email  The Atlassian account email the token belongs to.");
        Console.WriteLine("  --token  An API token from https://id.atlassian.com/manage-profile/security/api-tokens");
        Console.WriteLine();
        Console.WriteLine("See references/auth-guide.md for step-by-step instructions.");
        return 1;
    }

    // Normalize: accept a full URL for --site and extract the subdomain.
    site = site.Replace("https://", "").Replace("http://", "").Replace(".atlassian.net", "").TrimEnd('/').Trim();

    store.AddOrUpdate($"{CredBase}/site",  AccountName, site);
    store.AddOrUpdate($"{CredBase}/email", AccountName, email.Trim());
    store.AddOrUpdate($"{CredBase}/token", AccountName, token.Trim());
    Console.WriteLine($"Credentials saved for {email} @ {site}.atlassian.net");
    Console.WriteLine("Verify with: auth status");
    return 0;
}

int AuthStatus()
{
    var (site, email, token) = LoadCreds();
    Console.WriteLine("=== Jira API Status ===\n");
    Console.WriteLine($"Site  : {(site  != null ? $"{site}.atlassian.net" : "NOT SET")}");
    Console.WriteLine($"Email : {email ?? "NOT SET"}");
    Console.WriteLine($"Token : {(token != null ? "stored (" + token.Length + " chars)" : "NOT SET")}");
    if (site == null || email == null || token == null)
    {
        Console.WriteLine("\nRun: auth setup --site <site> --email <email> --token <api-token>");
        return 0;
    }

    // Live check against /myself
    var h = new HttpClient();
    h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
        "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email}:{token}")));
    h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    var resp = h.GetAsync($"https://{site}.atlassian.net/rest/api/3/myself").GetAwaiter().GetResult();
    var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    if (!resp.IsSuccessStatusCode)
    {
        Console.WriteLine($"\nLogin check: FAILED ({(int)resp.StatusCode})");
        Console.WriteLine(Truncate(body, 300));
        return 1;
    }
    var me = JsonNode.Parse(body);
    Console.WriteLine($"\nLogin check: OK");
    Console.WriteLine($"  Signed in as: {me?["displayName"]?.GetValue<string>()} <{me?["emailAddress"]?.GetValue<string>()}>");
    Console.WriteLine($"  Account ID  : {me?["accountId"]?.GetValue<string>()}");
    return 0;
}

int AuthLogout()
{
    store.Remove($"{CredBase}/site",  AccountName);
    store.Remove($"{CredBase}/email", AccountName);
    store.Remove($"{CredBase}/token", AccountName);
    Console.WriteLine("Credentials cleared.");
    return 0;
}

int PrintAuthUsage()
{
    Console.WriteLine("Usage: auth <subcommand>\n");
    Console.WriteLine("  setup --site <s> --email <e> --token <t>   Store credentials");
    Console.WriteLine("  status                                     Show config and verify login");
    Console.WriteLine("  logout                                     Clear stored credentials");
    return 1;
}

(string? site, string? email, string? token) LoadCreds()
{
    var site  = store.Get($"{CredBase}/site",  AccountName)?.Password;
    var email = store.Get($"{CredBase}/email", AccountName)?.Password;
    var token = store.Get($"{CredBase}/token", AccountName)?.Password;
    return (site, email, token);
}

// ══════════════════════════════════════════════════════════════════════════════
// PROJECTS
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> ProjectsList()
{
    var resp = await http.GetAsync($"{baseUrl}/project/search?maxResults=100&orderBy=name");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("projects", resp.StatusCode, json);

    var values = JsonNode.Parse(json)?["values"]?.AsArray();
    if (values == null || values.Count == 0) { Console.WriteLine("No projects found."); return 0; }

    Console.WriteLine($"Projects ({values.Count}):\n");
    Console.WriteLine($"  {"KEY",-12} {"TYPE",-10} NAME");
    foreach (var p in values)
    {
        var key  = p?["key"]?.GetValue<string>() ?? "";
        var name = p?["name"]?.GetValue<string>() ?? "";
        var type = p?["projectTypeKey"]?.GetValue<string>() ?? "";
        Console.WriteLine($"  {key,-12} {type,-10} {name}");
    }
    return 0;
}

// ══════════════════════════════════════════════════════════════════════════════
// ISSUE
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleIssue()
{
    if (args.Length < 2) return PrintIssueUsage();
    return args[1].ToLower() switch
    {
        "create"     => await IssueCreate(),
        "edit"       => await IssueEdit(),
        "get"        => await IssueGet(),
        "search"     => await IssueSearch(),
        "comment"    => await IssueComment(),
        "transition" => await IssueTransition(),
        "types"      => await IssueTypes(),
        "link"       => await IssueLink(),
        "linktypes"  => await IssueLinkTypes(),
        "delete"     => await IssueDelete(),
        _            => PrintIssueUsage(),
    };
}

async Task<int> IssueCreate()
{
    var project = GetArg("--project");
    var type    = GetArg("--type") ?? "Story";
    var summary = GetArg("--summary");
    var parent  = GetArg("--parent");
    var labels  = GetArg("--labels");
    var priority = GetArg("--priority");
    var assignee = GetArg("--assignee");
    var description = await ResolveDescription();

    if (project == null || summary == null)
    {
        Console.Error.WriteLine("Usage: issue create --project <KEY> --summary <text> [--type <Story>]");
        Console.Error.WriteLine("       [--description <text>] [--description-file <path>] [--parent <KEY>]");
        Console.Error.WriteLine("       [--labels a,b,c] [--priority <name>] [--assignee <me|accountId|email>]");
        return 1;
    }

    var fields = new JsonObject
    {
        ["project"]   = new JsonObject { ["key"] = project },
        ["issuetype"] = new JsonObject { ["name"] = type },
        ["summary"]   = summary,
    };
    if (description != null) fields["description"] = TextToAdf(description);
    if (parent != null)      fields["parent"] = new JsonObject { ["key"] = parent };
    if (priority != null)    fields["priority"] = new JsonObject { ["name"] = priority };
    if (assignee != null)
    {
        var acct = await ResolveAccountId(assignee);
        if (acct == null) { Console.Error.WriteLine($"Could not resolve assignee '{assignee}'."); return 1; }
        fields["assignee"] = new JsonObject { ["accountId"] = acct };
    }
    if (labels != null)
        fields["labels"] = new JsonArray(labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => (JsonNode)l).ToArray());

    var payload = new JsonObject { ["fields"] = fields };
    var resp = await http.PostAsync($"{baseUrl}/issue",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("create issue", resp.StatusCode, json);

    var node = JsonNode.Parse(json);
    var key  = node?["key"]?.GetValue<string>();
    var (site, _, _) = LoadCreds();
    Console.WriteLine($"Created {key}: {summary}");
    Console.WriteLine($"  https://{site}.atlassian.net/browse/{key}");
    return 0;
}

async Task<int> IssueEdit()
{
    if (args.Length < 3 || args[2].StartsWith("--"))
    {
        Console.Error.WriteLine("Usage: issue edit <KEY> [--summary <text>] [--description <text>]");
        Console.Error.WriteLine("       [--description-file <path>] [--labels a,b,c] [--priority <name>]");
        Console.Error.WriteLine("       [--assignee <me|accountId|email>]");
        return 1;
    }
    var key = args[2];
    var summary = GetArg("--summary");
    var priority = GetArg("--priority");
    var labels = GetArg("--labels");
    var assignee = GetArg("--assignee");
    var parent = GetArg("--parent");
    var description = await ResolveDescription();

    var fields = new JsonObject();
    if (summary != null)     fields["summary"] = summary;
    if (description != null)  fields["description"] = TextToAdf(description);
    if (parent != null)       fields["parent"] = new JsonObject { ["key"] = parent };
    if (priority != null)     fields["priority"] = new JsonObject { ["name"] = priority };
    if (assignee != null)
    {
        var acct = await ResolveAccountId(assignee);
        if (acct == null) { Console.Error.WriteLine($"Could not resolve assignee '{assignee}'."); return 1; }
        fields["assignee"] = new JsonObject { ["accountId"] = acct };
    }
    if (labels != null)
        fields["labels"] = new JsonArray(labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(l => (JsonNode)l).ToArray());

    if (fields.Count == 0)
    {
        Console.Error.WriteLine("Nothing to update. Pass at least one of --summary, --description(-file), --labels, --priority, --assignee.");
        return 1;
    }

    var payload = new JsonObject { ["fields"] = fields };
    var resp = await http.PutAsync($"{baseUrl}/issue/{Uri.EscapeDataString(key)}",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (!resp.IsSuccessStatusCode)
        return JiraError("edit issue", resp.StatusCode, await resp.Content.ReadAsStringAsync());

    var (site, _, _) = LoadCreds();
    Console.WriteLine($"Updated {key} ({string.Join(", ", fields.Select(kv => kv.Key))})");
    Console.WriteLine($"  https://{site}.atlassian.net/browse/{key}");
    return 0;
}

async Task<int> IssueGet()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: issue get <KEY>"); return 1; }
    var key  = args[2];
    var pointIds = await StoryPointFieldIds();
    var pointFields = string.Concat(pointIds.Select(p => "," + p));
    var resp = await http.GetAsync($"{baseUrl}/issue/{Uri.EscapeDataString(key)}?fields=summary,status,issuetype,priority,assignee,labels,parent,description,issuelinks,resolutiondate{pointFields}");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("get issue", resp.StatusCode, json);

    var f = JsonNode.Parse(json)?["fields"];
    Console.WriteLine($"Key      : {key}");
    Console.WriteLine($"Summary  : {f?["summary"]?.GetValue<string>()}");
    Console.WriteLine($"Type     : {f?["issuetype"]?["name"]?.GetValue<string>()}");
    Console.WriteLine($"Status   : {f?["status"]?["name"]?.GetValue<string>()}");
    Console.WriteLine($"Priority : {f?["priority"]?["name"]?.GetValue<string>() ?? "-"}");
    Console.WriteLine($"Assignee : {f?["assignee"]?["displayName"]?.GetValue<string>() ?? "Unassigned"}");
    var points = ReadPoints(f, pointIds);
    if (points != null) Console.WriteLine($"Points   : {points:0.#}");
    var resolved = FmtDate(f?["resolutiondate"]);
    if (resolved.Length > 0) Console.WriteLine($"Resolved : {resolved}");
    if (f?["parent"] != null) Console.WriteLine($"Parent   : {f["parent"]?["key"]?.GetValue<string>()}");
    var labels = f?["labels"]?.AsArray();
    if (labels != null && labels.Count > 0)
        Console.WriteLine($"Labels   : {string.Join(", ", labels.Select(l => l?.GetValue<string>()))}");

    var links = f?["issuelinks"]?.AsArray();
    if (links != null && links.Count > 0)
    {
        Console.WriteLine("Links    :");
        foreach (var l in links)
        {
            var t = l?["type"];
            // An entry holding `outwardIssue` means THIS issue is the inward side (use the inward
            // phrase); holding `inwardIssue` means THIS issue is the outward side (use the outward phrase).
            JsonNode? other; string phrase;
            if (l?["outwardIssue"] != null)
            {
                other  = l["outwardIssue"];
                phrase = t?["inward"]?.GetValue<string>() ?? "is linked to";
            }
            else
            {
                other  = l?["inwardIssue"];
                phrase = t?["outward"]?.GetValue<string>() ?? "is linked to";
            }
            var oKey  = other?["key"]?.GetValue<string>() ?? "";
            var oSumm = other?["fields"]?["summary"]?.GetValue<string>() ?? "";
            Console.WriteLine($"  {phrase} {oKey}  ({oSumm})");
        }
    }

    var desc = AdfToText(f?["description"]);
    if (!string.IsNullOrWhiteSpace(desc))
    {
        Console.WriteLine("\nDescription:");
        Console.WriteLine(desc);
    }
    return 0;
}

async Task<int> IssueSearch()
{
    var jql = GetArg("--jql");
    if (jql == null) { Console.Error.WriteLine("Usage: issue search --jql \"<JQL>\" [--max <n>]"); return 1; }
    var max = int.TryParse(GetArg("--max"), out var m) ? m : 25;

    var payload = new JsonObject
    {
        ["jql"]        = jql,
        ["maxResults"] = max,
        ["fields"]     = new JsonArray("summary", "status", "issuetype", "priority"),
    };
    var resp = await http.PostAsync($"{baseUrl}/search/jql",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("search", resp.StatusCode, json);

    var issues = JsonNode.Parse(json)?["issues"]?.AsArray();
    if (issues == null || issues.Count == 0) { Console.WriteLine("No issues match."); return 0; }

    Console.WriteLine($"Results ({issues.Count}):\n");
    foreach (var i in issues)
    {
        var key  = i?["key"]?.GetValue<string>() ?? "";
        var f    = i?["fields"];
        var summ = f?["summary"]?.GetValue<string>() ?? "";
        var st   = f?["status"]?["name"]?.GetValue<string>() ?? "";
        var tp   = f?["issuetype"]?["name"]?.GetValue<string>() ?? "";
        Console.WriteLine($"  {key,-12} [{st,-12}] {tp,-8} {summ}");
    }
    return 0;
}

async Task<int> IssueComment()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: issue comment <KEY> --body <text> | --body-file <path>"); return 1; }
    var key  = args[2];
    var body = await ResolveBody();
    if (body == null) { Console.Error.WriteLine("Provide --body <text> or --body-file <path>"); return 1; }

    var payload = new JsonObject { ["body"] = TextToAdf(body) };
    var resp = await http.PostAsync($"{baseUrl}/issue/{Uri.EscapeDataString(key)}/comment",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("comment", resp.StatusCode, json);
    Console.WriteLine($"Comment added to {key}.");
    return 0;
}

async Task<int> IssueTransition()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: issue transition <KEY> [--to <status name>]"); return 1; }
    var key = args[2];
    var to  = GetArg("--to");

    // Fetch available transitions
    var listResp = await http.GetAsync($"{baseUrl}/issue/{Uri.EscapeDataString(key)}/transitions");
    var listJson = await listResp.Content.ReadAsStringAsync();
    if (!listResp.IsSuccessStatusCode) return JiraError("transitions", listResp.StatusCode, listJson);
    var transitions = JsonNode.Parse(listJson)?["transitions"]?.AsArray();

    if (to == null)
    {
        Console.WriteLine($"Available transitions for {key}:\n");
        foreach (var t in transitions ?? new JsonArray())
            Console.WriteLine($"  {t?["name"]?.GetValue<string>()}  ->  {t?["to"]?["name"]?.GetValue<string>()}");
        Console.WriteLine("\nRe-run with --to \"<name>\" to apply one.");
        return 0;
    }

    var match = transitions?.FirstOrDefault(t =>
        string.Equals(t?["name"]?.GetValue<string>(), to, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(t?["to"]?["name"]?.GetValue<string>(), to, StringComparison.OrdinalIgnoreCase));
    if (match == null)
    {
        Console.Error.WriteLine($"No transition matching '{to}'. Available:");
        foreach (var t in transitions ?? new JsonArray())
            Console.Error.WriteLine($"  {t?["name"]?.GetValue<string>()} -> {t?["to"]?["name"]?.GetValue<string>()}");
        return 1;
    }

    var payload = new JsonObject { ["transition"] = new JsonObject { ["id"] = match["id"]?.GetValue<string>() } };
    var resp = await http.PostAsync($"{baseUrl}/issue/{Uri.EscapeDataString(key)}/transitions",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"{key} -> {match["to"]?["name"]?.GetValue<string>()}"); return 0; }
    return JiraError("transition", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

async Task<int> IssueTypes()
{
    var project = GetArg("--project");
    if (project == null) { Console.Error.WriteLine("Usage: issue types --project <KEY>"); return 1; }
    var resp = await http.GetAsync($"{baseUrl}/issue/createmeta/{Uri.EscapeDataString(project)}/issuetypes");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("issue types", resp.StatusCode, json);

    var types = JsonNode.Parse(json)?["issueTypes"]?.AsArray() ?? JsonNode.Parse(json)?["values"]?.AsArray();
    if (types == null || types.Count == 0) { Console.WriteLine("No issue types found."); return 0; }
    Console.WriteLine($"Issue types for {project}:\n");
    foreach (var t in types)
        Console.WriteLine($"  {t?["name"]?.GetValue<string>(),-16} {(t?["subtask"]?.GetValue<bool>() == true ? "(subtask)" : "")}");
    return 0;
}

async Task<int> IssueLink()
{
    // issue link <OUTWARD-KEY> --to <INWARD-KEY> [--type <name>]
    // Creates: <OUTWARD-KEY> <type.outward> <INWARD-KEY>. For "Blocks", outward = "blocks",
    // so `issue link A --to B` means A blocks B (and B is blocked by A).
    var inward = GetArg("--to");
    var type   = GetArg("--type") ?? "Blocks";
    if (args.Length < 3 || inward == null)
    {
        Console.Error.WriteLine("Usage: issue link <OUTWARD-KEY> --to <INWARD-KEY> [--type <name>]");
        Console.Error.WriteLine("  Creates: <OUTWARD-KEY> <type.outward> <INWARD-KEY>  (default type: Blocks)");
        Console.Error.WriteLine("  e.g. issue link PROT-37 --to PROT-38      # PROT-37 blocks PROT-38");
        Console.Error.WriteLine("  See valid types with: issue linktypes");
        return 1;
    }
    var outward = args[2];

    // Validate the link type (and get canonical casing + phrasing for a clear confirmation).
    var ltResp = await http.GetAsync($"{baseUrl}/issueLinkType");
    var ltJson = await ltResp.Content.ReadAsStringAsync();
    if (!ltResp.IsSuccessStatusCode) return JiraError("link types", ltResp.StatusCode, ltJson);
    var ltypes = JsonNode.Parse(ltJson)?["issueLinkTypes"]?.AsArray();
    var lt = ltypes?.FirstOrDefault(t =>
        string.Equals(t?["name"]?.GetValue<string>(), type, StringComparison.OrdinalIgnoreCase));
    if (lt == null)
    {
        Console.Error.WriteLine($"Unknown link type '{type}'. Available:");
        foreach (var t in ltypes ?? new JsonArray())
            Console.Error.WriteLine($"  {t?["name"]?.GetValue<string>()}  (outward: \"{t?["outward"]?.GetValue<string>()}\", inward: \"{t?["inward"]?.GetValue<string>()}\")");
        return 1;
    }
    var typeName      = lt["name"]?.GetValue<string>();
    var outwardPhrase = lt["outward"]?.GetValue<string>() ?? "is linked to";

    var payload = new JsonObject
    {
        ["type"]         = new JsonObject { ["name"] = typeName },
        ["inwardIssue"]  = new JsonObject { ["key"] = inward },
        ["outwardIssue"] = new JsonObject { ["key"] = outward },
    };
    var resp = await http.PostAsync($"{baseUrl}/issueLink",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (!resp.IsSuccessStatusCode) return JiraError("link", resp.StatusCode, await resp.Content.ReadAsStringAsync());

    Console.WriteLine($"Linked: {outward} {outwardPhrase} {inward}.");
    return 0;
}

async Task<int> IssueLinkTypes()
{
    var resp = await http.GetAsync($"{baseUrl}/issueLinkType");
    var json = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode) return JiraError("link types", resp.StatusCode, json);

    var types = JsonNode.Parse(json)?["issueLinkTypes"]?.AsArray();
    if (types == null || types.Count == 0) { Console.WriteLine("No link types found."); return 0; }

    Console.WriteLine($"Issue link types ({types.Count}):\n");
    foreach (var t in types)
        Console.WriteLine($"  {t?["name"]?.GetValue<string>(),-16} outward: \"{t?["outward"]?.GetValue<string>()}\"  inward: \"{t?["inward"]?.GetValue<string>()}\"");
    return 0;
}

async Task<int> IssueDelete()
{
    if (args.Length < 3) { Console.Error.WriteLine("Usage: issue delete <KEY>"); return 1; }
    var key  = args[2];
    var resp = await http.DeleteAsync($"{baseUrl}/issue/{Uri.EscapeDataString(key)}");
    if (resp.IsSuccessStatusCode) { Console.WriteLine($"Deleted {key}."); return 0; }
    return JiraError("delete issue", resp.StatusCode, await resp.Content.ReadAsStringAsync());
}

int PrintIssueUsage()
{
    Console.WriteLine("Usage: issue <subcommand> [options]\n");
    Console.WriteLine("  create     --project <KEY> --summary <text> [--type <Story>] [--description <t>]");
    Console.WriteLine("             [--description-file <path>] [--parent <KEY>] [--labels a,b] [--priority <name>]");
    Console.WriteLine("             [--assignee <me|accountId|email>]");
    Console.WriteLine("  edit       <KEY> [--summary <text>] [--description <t>] [--description-file <path>]");
    Console.WriteLine("             [--labels a,b,c] [--priority <name>] [--assignee <me|accountId|email>]");
    Console.WriteLine("  get        <KEY>");
    Console.WriteLine("  search     --jql \"<JQL>\" [--max <n>]");
    Console.WriteLine("  comment    <KEY> --body <text> | --body-file <path>");
    Console.WriteLine("  transition <KEY> [--to <status name>]");
    Console.WriteLine("  types      --project <KEY>");
    Console.WriteLine("  link       <OUTWARD-KEY> --to <INWARD-KEY> [--type <name>]   (default: Blocks)");
    Console.WriteLine("  linktypes  (list available issue link types)");
    Console.WriteLine("  delete     <KEY>");
    Console.WriteLine("\nDescriptions and comments accept Markdown: # headings, - / 1. lists, ``` code");
    Console.WriteLine("fences, **bold**, *italic*, `code`, [text](url). Rendered to Jira ADF.");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// SPRINT (Agile board API — /rest/agile/1.0)
// ══════════════════════════════════════════════════════════════════════════════

async Task<int> HandleSprint()
{
    if (args.Length < 2) return PrintSprintUsage();
    return args[1].ToLower() switch
    {
        "list"   => await SprintList(),
        "add"    => await SprintAdd(),
        "report" => await SprintReport(),
        _        => PrintSprintUsage(),
    };
}

// Every board associated with a project key, as (id, name, type) tuples.
async Task<List<(int id, string name, string type)>> BoardsForProject(string project)
{
    var result = new List<(int, string, string)>();
    var resp = await http.GetAsync($"{agileUrl}/board?projectKeyOrId={Uri.EscapeDataString(project)}&maxResults=50");
    if (!resp.IsSuccessStatusCode) return result;
    foreach (var b in JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["values"]?.AsArray() ?? new JsonArray())
    {
        var id = b?["id"]?.GetValue<int>();
        if (id == null) continue;
        result.Add((id.Value, b?["name"]?.GetValue<string>() ?? "", b?["type"]?.GetValue<string>() ?? ""));
    }
    return result;
}

// The first active sprint across a project's scrum boards.
async Task<(int? id, string? name)> ActiveSprint(string project)
{
    foreach (var (id, _, _) in await BoardsForProject(project))
    {
        var resp = await http.GetAsync($"{agileUrl}/board/{id}/sprint?state=active");
        if (!resp.IsSuccessStatusCode) continue; // kanban boards have no sprints (400)
        var sprints = JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["values"]?.AsArray();
        if (sprints != null && sprints.Count > 0)
            return (sprints[0]?["id"]?.GetValue<int>(), sprints[0]?["name"]?.GetValue<string>());
    }
    return (null, null);
}

async Task<int> SprintList()
{
    var project = GetArg("--project");
    var state   = GetArg("--state"); // active | future | closed (optional)
    if (project == null) { Console.Error.WriteLine("Usage: sprint list --project <KEY> [--state active|future|closed]"); return 1; }

    var boards = await BoardsForProject(project);
    if (boards.Count == 0) { Console.WriteLine($"No boards found for project {project}."); return 0; }

    var any = false;
    foreach (var (id, name, type) in boards)
    {
        var url = $"{agileUrl}/board/{id}/sprint?maxResults=50" + (state != null ? $"&state={Uri.EscapeDataString(state)}" : "");
        var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) continue; // kanban boards have no sprints (400)
        var sprints = JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["values"]?.AsArray();
        if (sprints == null || sprints.Count == 0) continue;
        any = true;
        Console.WriteLine($"Board [{id}] {name} ({type}):");
        foreach (var s in sprints)
        {
            var st = FmtDate(s?["startDate"]); var en = FmtDate(s?["endDate"]);
            var when = (st.Length > 0 || en.Length > 0) ? $"  {st} -> {en}" : "";
            Console.WriteLine($"  [{s?["id"]?.GetValue<int>()}] {s?["name"]?.GetValue<string>()}  ({s?["state"]?.GetValue<string>()}){when}");
        }
    }
    if (!any) Console.WriteLine($"No {(state != null ? state + " " : "")}sprints found for {project}.");
    return 0;
}

async Task<int> SprintAdd()
{
    var to      = GetArg("--to");
    var project = GetArg("--project");
    if (to == null)
    {
        Console.Error.WriteLine("Usage: sprint add <KEY> [<KEY> ...] --to <active|SPRINT-ID> [--project <KEY>]");
        Console.Error.WriteLine("  --to active requires --project to locate the board's active sprint.");
        return 1;
    }

    // Collect positional issue keys, skipping flag tokens and their values.
    var skip = new HashSet<int>();
    for (int i = 2; i < args.Length; i++)
        if (args[i] == "--to" || args[i] == "--project") { skip.Add(i); skip.Add(i + 1); }
    var keys = new List<string>();
    for (int i = 2; i < args.Length; i++)
        if (!skip.Contains(i) && !args[i].StartsWith("--")) keys.Add(args[i]);
    if (keys.Count == 0) { Console.Error.WriteLine("Provide at least one issue KEY."); return 1; }

    int sprintId; string sprintLabel;
    if (string.Equals(to, "active", StringComparison.OrdinalIgnoreCase))
    {
        if (project == null) { Console.Error.WriteLine("--to active requires --project <KEY>."); return 1; }
        var (id, name) = await ActiveSprint(project);
        if (id == null) { Console.Error.WriteLine($"No active sprint found for {project}."); return 1; }
        sprintId = id.Value; sprintLabel = $"'{name}' (active)";
    }
    else if (int.TryParse(to, out var explicitId))
    {
        sprintId = explicitId; sprintLabel = $"id {explicitId}";
    }
    else { Console.Error.WriteLine($"--to must be 'active' or a numeric sprint id (got '{to}')."); return 1; }

    var payload = new JsonObject { ["issues"] = new JsonArray(keys.Select(k => (JsonNode)k).ToArray()) };
    var resp = await http.PostAsync($"{agileUrl}/sprint/{sprintId}/issue",
        new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
    if (!resp.IsSuccessStatusCode) return JiraError("sprint add", resp.StatusCode, await resp.Content.ReadAsStringAsync());
    Console.WriteLine($"Moved {string.Join(", ", keys)} -> sprint {sprintLabel}.");
    return 0;
}

// A human-readable "what got done" report for a sprint: header (name, state, dates, goal),
// completion + story-point totals, then issues grouped by outcome (completed / in progress /
// not started / dropped) with type, points, assignee, and a one-line description so the report
// reads as work done rather than a list of keys. `--full` prints whole descriptions.
async Task<int> SprintReport()
{
    var project   = GetArg("--project");
    var sprintArg = GetArg("--sprint");
    var full      = args.Contains("--full");
    var brief     = args.Contains("--brief");      // one line per item (summary + key), no detail
    var byProject = args.Contains("--by-project"); // sub-group each outcome by Jira project

    int sprintId;
    if (sprintArg != null && int.TryParse(sprintArg, out var sid))
        sprintId = sid;
    else if ((sprintArg == null || sprintArg.Equals("active", StringComparison.OrdinalIgnoreCase)) && project != null)
    {
        var (aid, _) = await ActiveSprint(project);
        if (aid == null) { Console.Error.WriteLine($"No active sprint for {project}. Pass --sprint <ID> (find one with: sprint list --project {project})."); return 1; }
        sprintId = aid.Value;
    }
    else
    {
        Console.Error.WriteLine("Usage: sprint report --sprint <ID>  |  --project <KEY> [--sprint active]");
        Console.Error.WriteLine("       [--brief] [--by-project] [--full]");
        Console.Error.WriteLine("  Find a sprint id with: sprint list --project <KEY>");
        return 1;
    }

    // Sprint metadata (name, state, dates, goal).
    var mResp = await http.GetAsync($"{agileUrl}/sprint/{sprintId}");
    var mJson = await mResp.Content.ReadAsStringAsync();
    if (!mResp.IsSuccessStatusCode) return JiraError("sprint report", mResp.StatusCode, mJson);
    var m = JsonNode.Parse(mJson);
    var sprintName = m?["name"]?.GetValue<string>() ?? $"Sprint {sprintId}";
    var state      = m?["state"]?.GetValue<string>() ?? "";
    var goal       = m?["goal"]?.GetValue<string>();
    var start      = FmtDate(m?["startDate"]);
    var end        = FmtDate(m?["endDate"]);
    var completed  = FmtDate(m?["completeDate"]);

    // Story Points live in a custom field whose id varies per site — discover it.
    var pointIds   = await StoryPointFieldIds();
    var fieldNames = new List<string> { "summary", "status", "issuetype", "assignee", "resolutiondate", "parent", "labels", "description", "project" };
    fieldNames.AddRange(pointIds);

    // Fetch issues via JQL search rather than the agile /sprint/{id}/issue endpoint: the agile
    // endpoint omits the description body, and descriptions are what make this a report rather than
    // a list of keys. The v3 search uses token pagination (nextPageToken), so loop until it's gone.
    var rows = new List<SprintRow>();
    string? pageToken = null;
    do
    {
        var payload = new JsonObject
        {
            ["jql"]        = $"sprint = {sprintId} ORDER BY key ASC",
            ["maxResults"] = 100,
            ["fields"]     = new JsonArray(fieldNames.Select(n => (JsonNode)n).ToArray()),
        };
        if (pageToken != null) payload["nextPageToken"] = pageToken;

        var r  = await http.PostAsync($"{baseUrl}/search/jql", new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"));
        var rj = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) return JiraError("sprint issues", r.StatusCode, rj);
        var node   = JsonNode.Parse(rj);
        var issues = node?["issues"]?.AsArray() ?? new JsonArray();
        foreach (var it in issues)
        {
            var f = it?["fields"];
            var pkey = f?["project"]?["key"]?.GetValue<string>() ?? "";
            rows.Add(new SprintRow(
                it?["key"]?.GetValue<string>() ?? "",
                f?["issuetype"]?["name"]?.GetValue<string>() ?? "",
                f?["summary"]?.GetValue<string>() ?? "",
                f?["status"]?["name"]?.GetValue<string>() ?? "",
                f?["status"]?["statusCategory"]?["key"]?.GetValue<string>() ?? "",
                f?["assignee"]?["displayName"]?.GetValue<string>() ?? "Unassigned",
                ReadPoints(f, pointIds),
                AdfToText(f?["description"]),
                pkey,
                f?["project"]?["name"]?.GetValue<string>() ?? (pkey.Length > 0 ? pkey : "Other")));
        }
        pageToken = node?["nextPageToken"]?.GetValue<string>();
    }
    while (pageToken != null);

    if (rows.Count == 0) { Console.WriteLine($"Sprint: {sprintName} ({state}): no issues."); return 0; }

    // Bucket by outcome. "Dropped" (rejected/won't-do/cancelled) is split out from genuine Done,
    // since Jira often files those under the same "done" status category but they are not work done.
    bool IsDropped(string status) => Regex.IsMatch(status, @"reject|won'?t|cancel|duplicate|abandon", RegexOptions.IgnoreCase);
    var dropped = rows.Where(r => IsDropped(r.Status)).ToList();
    var live    = rows.Where(r => !IsDropped(r.Status)).ToList();
    var done    = live.Where(r => r.Cat == "done").ToList();
    var prog    = live.Where(r => r.Cat == "indeterminate").ToList();
    var todo    = live.Where(r => r.Cat != "done" && r.Cat != "indeterminate").ToList();

    var hasPts   = rows.Any(r => r.Points != null);
    var ptsTotal = rows.Sum(r => r.Points ?? 0);
    var ptsDone  = done.Sum(r => r.Points ?? 0);

    // ── Render ───────────────────────────────────────────────────────────────────
    Console.WriteLine($"Sprint: {sprintName}  ({state})");
    var dateLine = (start.Length > 0 || end.Length > 0) ? $"{(start.Length > 0 ? start : "?")} -> {(end.Length > 0 ? end : "?")}" : "";
    if (completed.Length > 0) dateLine += (dateLine.Length > 0 ? "   " : "") + $"completed {completed}";
    if (dateLine.Length > 0) Console.WriteLine($"Dates : {dateLine}");
    if (!string.IsNullOrWhiteSpace(goal)) Console.WriteLine($"Goal  : {goal}");
    Console.WriteLine();

    var summary = $"Summary: {rows.Count} issues: {done.Count} completed, {prog.Count} in progress, {todo.Count} not started";
    if (dropped.Count > 0) summary += $", {dropped.Count} dropped";
    Console.WriteLine(summary);
    if (hasPts) Console.WriteLine($"Points : {ptsDone:0.#} of {ptsTotal:0.#} completed");
    Console.WriteLine();

    string Snippet(string desc)
    {
        if (string.IsNullOrWhiteSpace(desc)) return "";
        if (full) return desc.Replace("\n", "\n      ");
        var firstLine = desc.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return Truncate(firstLine, 200);
    }

    // One issue line. Brief = just the summary + key (a sendable one-liner); otherwise the full
    // detail line with a description snippet underneath.
    void Emit(SprintRow r, string indent)
    {
        if (brief) { Console.WriteLine($"{indent}- {r.Summary}  ({r.Key})"); return; }
        var pts = r.Points != null ? $", {r.Points:0.#}pt" : "";
        Console.WriteLine($"{indent}{r.Key}  [{r.Type}, {r.Status}{pts}]  {r.Summary}  ({r.Assignee})");
        var snip = Snippet(r.Desc);
        if (snip.Length > 0) Console.WriteLine($"{indent}    {snip}");
    }

    void Section(string title, List<SprintRow> items)
    {
        if (items.Count == 0) return;
        Console.WriteLine($"{title} ({items.Count}):");
        if (byProject)
        {
            // Sub-group by project name (falling back to the key), e.g. "Constellation:" / "Protostar:".
            foreach (var g in items.GroupBy(r => r.ProjName.Length > 0 ? r.ProjName : r.ProjKey)
                                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {g.Key}:");
                foreach (var r in g.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) Emit(r, "    ");
            }
        }
        else
        {
            foreach (var r in items.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) Emit(r, "  ");
        }
        Console.WriteLine();
    }

    Section("Completed", done);
    Section("In progress", prog);
    Section("Not started / carryover", todo);
    Section("Dropped (rejected / won't do)", dropped);

    // The per-assignee tally is extra detail; omit it in a brief, sendable recap.
    if (!brief && done.Count > 0)
    {
        Console.WriteLine("By assignee (completed):");
        foreach (var g in done.GroupBy(r => r.Assignee).OrderByDescending(g => g.Count()))
        {
            var p = g.Sum(x => x.Points ?? 0);
            Console.WriteLine($"  {g.Key,-22} {g.Count()} done" + (hasPts ? $", {p:0.#}pt" : ""));
        }
    }
    return 0;
}

int PrintSprintUsage()
{
    Console.WriteLine("Usage: sprint <subcommand> [options]\n");
    Console.WriteLine("  list   --project <KEY> [--state active|future|closed]   List sprints (with dates) on the project's boards");
    Console.WriteLine("  add    <KEY> [<KEY> ...] --to <active|SPRINT-ID> [--project <KEY>]   Move issue(s) into a sprint");
    Console.WriteLine("  report --sprint <ID> | --project <KEY> [--sprint active] [--brief] [--by-project] [--full]");
    Console.WriteLine("         Human-readable 'what got done' report. --brief = one line per item; --by-project");
    Console.WriteLine("         = group each outcome by project; --full = whole descriptions.");
    Console.WriteLine("\n'add --to active' needs --project to locate the active sprint on the project's scrum board.");
    Console.WriteLine("'report' takes a numeric sprint id (from 'sprint list'), or --project with the active sprint.");
    return 1;
}

// ══════════════════════════════════════════════════════════════════════════════
// HELPERS
// ══════════════════════════════════════════════════════════════════════════════

int PrintUsage()
{
    Console.WriteLine("Jira API — Jira Cloud REST v3\n");
    Console.WriteLine("Usage: <auth|projects|issue|sprint> <subcommand> [options]\n");
    Console.WriteLine("  auth     setup | status | logout");
    Console.WriteLine("  projects (list all visible projects)");
    Console.WriteLine("  issue    create | edit | get | search | comment | transition | types | link | delete");
    Console.WriteLine("  sprint   list | add | report   (Agile board sprints)");
    Console.WriteLine("\nFirst run: auth setup --site <site> --email <email> --token <api-token>");
    return 1;
}

string? GetArg(string name)
{
    for (int i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

async Task<string?> ResolveBody()
{
    var file = GetArg("--body-file");
    if (file != null) return await File.ReadAllTextAsync(file);
    return GetArg("--body");
}

async Task<string?> ResolveDescription()
{
    var file = GetArg("--description-file");
    if (file != null) return await File.ReadAllTextAsync(file);
    return GetArg("--description");
}

// Resolve an --assignee value to an account id. Accepts "me" (the authenticated user),
// a raw accountId, an email, or a display name (the latter two via user search). A value
// with no '@' and no space is assumed to already be an accountId.
async Task<string?> ResolveAccountId(string who)
{
    if (string.Equals(who, "me", StringComparison.OrdinalIgnoreCase))
    {
        var r = await http.GetAsync($"{baseUrl}/myself");
        if (!r.IsSuccessStatusCode) return null;
        return JsonNode.Parse(await r.Content.ReadAsStringAsync())?["accountId"]?.GetValue<string>();
    }
    if (!who.Contains('@') && !who.Contains(' ')) return who;
    var sr = await http.GetAsync($"{baseUrl}/user/search?query={Uri.EscapeDataString(who)}");
    if (!sr.IsSuccessStatusCode) return null;
    var users = JsonNode.Parse(await sr.Content.ReadAsStringAsync())?.AsArray();
    return users != null && users.Count > 0 ? users[0]?["accountId"]?.GetValue<string>() : null;
}

// Convert a practical subset of Markdown into Atlassian Document Format. Jira Cloud REST v3
// requires ADF for the description and comment body fields; passing plain text renders as one
// flat paragraph, so we parse common Markdown block + inline syntax here.
//
// Supported blocks:   # / ## / ... headings, "- "/"* " bullet lists, "1. " ordered lists,
//                     ``` fenced code blocks (optional language), blank-line-separated paragraphs.
// Supported inline:   **bold**, `inline code`, [text](url), *italic*. Consecutive non-blank lines
//                     join into one paragraph (Markdown soft-wrap) — use a blank line to split.
JsonNode TextToAdf(string text)
{
    var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    var content = new JsonArray();
    int i = 0;
    while (i < lines.Length)
    {
        var line = lines[i];
        if (line.Trim().Length == 0) { i++; continue; }

        // Fenced code block: ```lang ... ```
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```"))
        {
            var lang = trimmed[3..].Trim();
            var code = new List<string>();
            i++;
            while (i < lines.Length && !lines[i].TrimStart().StartsWith("```")) { code.Add(lines[i]); i++; }
            if (i < lines.Length) i++; // skip closing fence
            var cb = new JsonObject { ["type"] = "codeBlock" };
            if (lang.Length > 0) cb["attrs"] = new JsonObject { ["language"] = lang };
            var codeText = string.Join("\n", code);
            cb["content"] = codeText.Length > 0
                ? new JsonArray(new JsonObject { ["type"] = "text", ["text"] = codeText })
                : new JsonArray();
            content.Add(cb);
            continue;
        }

        // Heading: #..###### text
        var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
        if (h.Success)
        {
            content.Add(new JsonObject
            {
                ["type"]    = "heading",
                ["attrs"]   = new JsonObject { ["level"] = h.Groups[1].Value.Length },
                ["content"] = InlineToAdf(h.Groups[2].Value.Trim()),
            });
            i++;
            continue;
        }

        // Bullet / ordered list (consecutive matching lines)
        if (Regex.IsMatch(line, @"^\s*[-*]\s+"))
        {
            var items = new JsonArray();
            while (i < lines.Length && Regex.IsMatch(lines[i], @"^\s*[-*]\s+"))
            {
                items.Add(ListItem(Regex.Replace(lines[i], @"^\s*[-*]\s+", "")));
                i++;
            }
            content.Add(new JsonObject { ["type"] = "bulletList", ["content"] = items });
            continue;
        }
        if (Regex.IsMatch(line, @"^\s*\d+\.\s+"))
        {
            var items = new JsonArray();
            while (i < lines.Length && Regex.IsMatch(lines[i], @"^\s*\d+\.\s+"))
            {
                items.Add(ListItem(Regex.Replace(lines[i], @"^\s*\d+\.\s+", "")));
                i++;
            }
            content.Add(new JsonObject { ["type"] = "orderedList", ["content"] = items });
            continue;
        }

        // Paragraph: gather consecutive plain lines until a blank line or a block starts.
        var paraLines = new List<string>();
        while (i < lines.Length)
        {
            var l = lines[i];
            if (l.Trim().Length == 0) break;
            if (l.TrimStart().StartsWith("```")) break;
            if (Regex.IsMatch(l, @"^(#{1,6})\s+")) break;
            if (Regex.IsMatch(l, @"^\s*[-*]\s+")) break;
            if (Regex.IsMatch(l, @"^\s*\d+\.\s+")) break;
            paraLines.Add(l.Trim());
            i++;
        }
        content.Add(new JsonObject
        {
            ["type"]    = "paragraph",
            ["content"] = InlineToAdf(string.Join(" ", paraLines)),
        });
    }
    if (content.Count == 0)
        content.Add(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray() });
    return new JsonObject { ["type"] = "doc", ["version"] = 1, ["content"] = content };
}

// A list item wraps a single paragraph of inline content.
JsonObject ListItem(string text) => new()
{
    ["type"]    = "listItem",
    ["content"] = new JsonArray(new JsonObject { ["type"] = "paragraph", ["content"] = InlineToAdf(text.Trim()) }),
};

// Parse inline Markdown (**bold**, `code`, [text](url), *italic*) into an array of ADF text nodes.
// Nodes accumulate in a List and are only placed into a JsonArray once, here at the top level —
// a JsonNode cannot belong to two parents, so recursion must not build intermediate JsonArrays.
JsonArray InlineToAdf(string text)
{
    var arr = new JsonArray();
    foreach (var n in ParseInline(text, new List<(string type, string? href)>())) arr.Add(n);
    return arr;
}

List<JsonNode> ParseInline(string s, List<(string type, string? href)> marks)
{
    var nodes = new List<JsonNode>();
    var plain = new StringBuilder();
    void FlushPlain()
    {
        if (plain.Length == 0) return;
        nodes.Add(MkText(plain.ToString(), marks));
        plain.Clear();
    }
    void AddSpan(string inner, (string, string?) mark)
    {
        FlushPlain();
        nodes.AddRange(ParseInline(inner, Append(marks, mark)));
    }

    int i = 0;
    while (i < s.Length)
    {
        var c = s[i];
        // `inline code` — literal, no nested formatting
        if (c == '`')
        {
            var end = s.IndexOf('`', i + 1);
            if (end > i)
            {
                FlushPlain();
                nodes.Add(MkText(s[(i + 1)..end], Append(marks, ("code", null))));
                i = end + 1;
                continue;
            }
        }
        // **bold**
        if (c == '*' && i + 1 < s.Length && s[i + 1] == '*')
        {
            var end = s.IndexOf("**", i + 2);
            if (end > i + 1) { AddSpan(s[(i + 2)..end], ("strong", null)); i = end + 2; continue; }
        }
        // [text](url)
        if (c == '[')
        {
            var close = s.IndexOf(']', i + 1);
            if (close > i && close + 1 < s.Length && s[close + 1] == '(')
            {
                var paren = s.IndexOf(')', close + 2);
                if (paren > close)
                {
                    AddSpan(s[(i + 1)..close], ("link", s[(close + 2)..paren]));
                    i = paren + 1;
                    continue;
                }
            }
        }
        // *italic* (single asterisk; lone/unmatched asterisks stay literal)
        if (c == '*')
        {
            var end = s.IndexOf('*', i + 1);
            if (end > i + 1) { AddSpan(s[(i + 1)..end], ("em", null)); i = end + 1; continue; }
        }
        plain.Append(c);
        i++;
    }
    FlushPlain();
    return nodes;
}

List<(string, string?)> Append(List<(string type, string? href)> marks, (string, string?) extra)
{
    var copy = new List<(string, string?)>(marks) { extra };
    return copy;
}

JsonObject MkText(string text, List<(string type, string? href)> marks)
{
    var node = new JsonObject { ["type"] = "text", ["text"] = text };
    if (marks.Count > 0)
    {
        var arr = new JsonArray();
        foreach (var (type, href) in marks)
            arr.Add(type == "link"
                ? new JsonObject { ["type"] = "link", ["attrs"] = new JsonObject { ["href"] = href } }
                : new JsonObject { ["type"] = type });
        node["marks"] = arr;
    }
    return node;
}

// Best-effort flatten of an ADF document back to readable text (for issue get). Renders headings,
// lists (with markers), and code blocks as line breaks so the structure survives the round-trip.
string AdfToText(JsonNode? adf)
{
    if (adf == null) return "";
    var sb = new StringBuilder();
    void Walk(JsonNode? node)
    {
        if (node is JsonArray a) { foreach (var c in a) Walk(c); return; }
        if (node is not JsonObject obj) return;
        switch (obj["type"]?.GetValue<string>())
        {
            case "text":      sb.Append(obj["text"]?.GetValue<string>()); break;
            case "hardBreak": sb.Append('\n'); break;
            case "listItem":  sb.Append("- "); Walk(obj["content"]); break;
            case "heading":   sb.Append('\n'); Walk(obj["content"]); sb.Append('\n'); break;
            case "paragraph": Walk(obj["content"]); sb.Append('\n'); break;
            case "codeBlock": Walk(obj["content"]); sb.Append('\n'); break;
            default:          Walk(obj["content"]); break;
        }
    }
    Walk(adf);
    return sb.ToString().Trim();
}

static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

// Story Points are a custom field whose id differs per Jira site (and there can be more than one:
// company-managed "Story Points" and team-managed "Story point estimate"). Look them up by name.
// Cached for the process (via _spFieldIds, declared at the top) so repeated reads don't re-fetch.
async Task<List<string>> StoryPointFieldIds()
{
    if (_spFieldIds != null) return _spFieldIds;
    _spFieldIds = new List<string>();
    var resp = await http.GetAsync($"{baseUrl}/field");
    if (!resp.IsSuccessStatusCode) return _spFieldIds;
    foreach (var f in JsonNode.Parse(await resp.Content.ReadAsStringAsync())?.AsArray() ?? new JsonArray())
    {
        var name = f?["name"]?.GetValue<string>() ?? "";
        if (name.Equals("Story Points", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Story points", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Story point estimate", StringComparison.OrdinalIgnoreCase))
        {
            var id = f?["id"]?.GetValue<string>();
            if (id != null && !_spFieldIds.Contains(id)) _spFieldIds.Add(id);
        }
    }
    return _spFieldIds;
}

// Read the first present story-point value from an issue's fields, across the candidate field ids.
double? ReadPoints(JsonNode? fields, List<string> ids)
{
    foreach (var id in ids)
        if (fields?[id] is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var n)) return n;
        }
    return null;
}

// Format an ISO-8601 Jira date/timestamp node as yyyy-MM-dd; empty string if absent.
string FmtDate(JsonNode? n)
{
    var s = n is JsonValue v && v.TryGetValue<string>(out var str) ? str : null;
    if (string.IsNullOrWhiteSpace(s)) return "";
    return DateTimeOffset.TryParse(s, out var dt) ? dt.ToString("yyyy-MM-dd") : s!;
}

int JiraError(string op, System.Net.HttpStatusCode code, string body)
{
    Console.Error.WriteLine($"{op} failed: {(int)code} {code}");
    // Jira returns { "errorMessages": [...], "errors": { field: msg } }
    try
    {
        var node = JsonNode.Parse(body);
        var msgs = node?["errorMessages"]?.AsArray();
        if (msgs != null) foreach (var m in msgs) Console.Error.WriteLine($"  {m?.GetValue<string>()}");
        var errs = node?["errors"]?.AsObject();
        if (errs != null) foreach (var kv in errs) Console.Error.WriteLine($"  {kv.Key}: {kv.Value?.GetValue<string>()}");
        if (msgs == null && errs == null) Console.Error.WriteLine($"  {Truncate(body, 500)}");
    }
    catch { Console.Error.WriteLine($"  {Truncate(body, 500)}"); }
    return 1;
}

// ── Types ──────────────────────────────────────────────────────────────────────
// Row model for the sprint report (a record, not a 10-field tuple, for readability).
record SprintRow(string Key, string Type, string Summary, string Status, string Cat,
    string Assignee, double? Points, string Desc, string ProjKey, string ProjName);
