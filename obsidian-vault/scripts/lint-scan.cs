#pragma warning disable CS8603, CS8600

// Scans notes in Blake's Obsidian vault for standards violations.
// Emits a JSON report to stdout. Exit code 0 = successful scan (check summary
// for whether violations were found); exit code 1 = scan error.
//
// Usage:
//   dotnet run scripts/lint-scan.cs                          # scans entire vault
//   dotnet run scripts/lint-scan.cs -- "The Pile"            # scans a subdirectory
//   dotnet run scripts/lint-scan.cs -- "The Pile/Foo.md"     # scans one file

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

const string VaultPath = @"C:\Users\Blake\Documents\main";
const string SkillPath = @"C:\Users\Blake\.claude\skills\obsidian-vault";

string target = args.Length > 0 ? args[0] : "";
string scanRoot = string.IsNullOrEmpty(target)
    ? VaultPath
    : (Path.IsPathRooted(target) ? target : Path.Combine(VaultPath, target));

string standardsReadme = Path.Combine(SkillPath, "references", "standards", "README.md");
string currentStdVer = ReadFmField(standardsReadme, "standards-version") ?? "unknown";

var currentTplVers = new Dictionary<string, string>();
string templatesDir = Path.Combine(VaultPath, "Templates");
if (Directory.Exists(templatesDir))
{
    foreach (var tf in Directory.GetFiles(templatesDir, "*.md"))
    {
        var n = ReadFmField(tf, "template");
        var v = ReadFmField(tf, "template-version");
        if (n != null && v != null) currentTplVers[n] = v;
    }
}

// Collect files
var filesToScan = new List<string>();
if (File.Exists(scanRoot) && scanRoot.EndsWith(".md"))
{
    filesToScan.Add(scanRoot);
}
else if (Directory.Exists(scanRoot))
{
    string tplFolder = Path.Combine(VaultPath, "Templates");
    string excFolder = Path.Combine(VaultPath, "Excalidraw");
    foreach (var f in Directory.EnumerateFiles(scanRoot, "*.md", SearchOption.AllDirectories))
    {
        if (f.StartsWith(tplFolder, StringComparison.OrdinalIgnoreCase)) continue;
        if (f.StartsWith(excFolder, StringComparison.OrdinalIgnoreCase)) continue;
        filesToScan.Add(f);
    }
}
else
{
    Console.Error.WriteLine($"Scan target not found: {scanRoot}");
    return 1;
}

// Scan
var fileResults = new List<(string rel, string full, List<(string code, string severity, bool autoFix, int line, string detail)> violations)>();
int totalViolations = 0;
int totalAutoFixable = 0;

foreach (var file in filesToScan.OrderBy(f => f))
{
    var violations = ScanFile(file, currentStdVer, currentTplVers);
    if (violations.Count == 0) continue;
    var rel = Path.GetRelativePath(VaultPath, file).Replace('\\', '/');
    totalViolations += violations.Count;
    totalAutoFixable += violations.Count(v => v.Item3);
    fileResults.Add((rel, file, violations));
}

// Emit JSON manually (avoids reflection serialization issues in AOT)
var sb = new StringBuilder();
sb.AppendLine("{");
sb.AppendLine($"  \"scan_root\": {Js(scanRoot)},");
sb.AppendLine($"  \"standard_version\": {Js(currentStdVer)},");
sb.Append("  \"current_template_versions\": {");
var tplEntries = currentTplVers.Select(kv => $"{Js(kv.Key)}: {Js(kv.Value)}").ToList();
sb.AppendLine(tplEntries.Count > 0 ? "" : " },");
if (tplEntries.Count > 0)
{
    sb.AppendLine();
    for (int i = 0; i < tplEntries.Count; i++)
        sb.AppendLine($"    {tplEntries[i]}{(i < tplEntries.Count - 1 ? "," : "")}");
    sb.AppendLine("  },");
}
sb.AppendLine($"  \"summary\": {{");
sb.AppendLine($"    \"files_scanned\": {filesToScan.Count},");
sb.AppendLine($"    \"files_with_violations\": {fileResults.Count},");
sb.AppendLine($"    \"total_violations\": {totalViolations},");
sb.AppendLine($"    \"auto_fixable\": {totalAutoFixable},");
sb.AppendLine($"    \"needs_judgment\": {totalViolations - totalAutoFixable}");
sb.AppendLine("  },");
sb.AppendLine("  \"files\": [");
for (int fi = 0; fi < fileResults.Count; fi++)
{
    var (rel, full, violations) = fileResults[fi];
    int afc = violations.Count(v => v.Item3);
    sb.AppendLine("    {");
    sb.AppendLine($"      \"path\": {Js(rel)},");
    sb.AppendLine($"      \"full_path\": {Js(full)},");
    sb.AppendLine($"      \"violation_count\": {violations.Count},");
    sb.AppendLine($"      \"auto_fixable_count\": {afc},");
    sb.AppendLine("      \"violations\": [");
    for (int vi = 0; vi < violations.Count; vi++)
    {
        var (code, severity, autoFix, line, detail) = violations[vi];
        sb.AppendLine("        {");
        sb.AppendLine($"          \"code\": {Js(code)},");
        sb.AppendLine($"          \"severity\": {Js(severity)},");
        sb.AppendLine($"          \"auto_fixable\": {(autoFix ? "true" : "false")},");
        sb.AppendLine($"          \"line\": {line},");
        sb.AppendLine($"          \"detail\": {Js(detail)}");
        sb.AppendLine($"        }}{(vi < violations.Count - 1 ? "," : "")}");
    }
    sb.AppendLine("      ]");
    sb.AppendLine($"    }}{(fi < fileResults.Count - 1 ? "," : "")}");
}
sb.AppendLine("  ]");
sb.Append("}");

Console.WriteLine(sb.ToString());
return 0;

// ---- helpers ----

static List<(string code, string severity, bool autoFix, int line, string detail)> ScanFile(
    string path, string currentStdVer, Dictionary<string, string> tplVers)
{
    var v = new List<(string, string, bool, int, string)>();
    var lines = File.ReadAllLines(path);
    if (lines.Length == 0) return v;

    var fm = new Dictionary<string, string>();
    int fmEnd = -1;
    bool hasFm = lines[0].Trim() == "---";
    if (hasFm)
    {
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---") { fmEnd = i; break; }
            var m = Regex.Match(lines[i], @"^([A-Za-z][\w-]*):\s*(.*)$");
            if (m.Success) fm[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }
    }

    if (!hasFm || fmEnd == -1)
    {
        v.Add(("no-frontmatter", "error", false, 1, "Note has no YAML frontmatter"));
    }
    else
    {
        foreach (var req in new[] { "id", "template", "template-version", "standard-version", "tags" })
        {
            if (!fm.ContainsKey(req))
            {
                bool fixable = req == "standard-version"
                    || (req == "template-version" && fm.ContainsKey("template")
                        && tplVers.ContainsKey(Unquote(fm["template"])));
                v.Add(("frontmatter-missing-field", "error", fixable, 1, $"missing field: {req}"));
            }
        }

        if (fm.TryGetValue("standard-version", out var sv))
        {
            var cur = Unquote(sv);
            if (currentStdVer != "unknown" && cur.Length > 0 && cur != currentStdVer)
                v.Add(("standard-version-drift", "info", true, 1, $"{cur} -> {currentStdVer}"));
        }

        if (fm.TryGetValue("template", out var tpl) && fm.TryGetValue("template-version", out var tv))
        {
            var tplName = Unquote(tpl);
            var curVer = Unquote(tv);
            if (tplVers.TryGetValue(tplName, out var latest) && curVer.Length > 0 && curVer != latest)
                v.Add(("template-version-drift", "info", true, 1, $"{tplName} {curVer} -> {latest}"));
        }
    }

    int bodyStart = fmEnd == -1 ? 0 : fmEnd + 1;
    string filename = Path.GetFileNameWithoutExtension(path);
    bool foundFirstContent = false;

    for (int i = bodyStart; i < lines.Length; i++)
    {
        var line = lines[i];
        var trim = line.Trim();

        if (Regex.IsMatch(trim, @"^Status:\s"))
            v.Add(("body-has-status-line", "warn", false, i + 1, trim));

        if (Regex.IsMatch(trim, @"^Tags:\s"))
            v.Add(("body-has-tags-line", "warn", false, i + 1, trim));

        if (!foundFirstContent && trim.Length > 0)
        {
            foundFirstContent = true;
            if (trim.StartsWith("# "))
            {
                var h1 = trim.Substring(2).Trim();
                if (string.Equals(h1, filename, StringComparison.OrdinalIgnoreCase))
                    v.Add(("body-h1-matches-filename", "warn", true, i + 1, h1));
            }
        }

        if (Regex.IsMatch(trim, @"^```ad-\w+"))
            v.Add(("admonition-syntax", "warn", true, i + 1, trim));

        bool isHeader = Regex.IsMatch(trim, @"^#{1,6}\s");
        if (!isHeader && Regex.IsMatch(trim, @"^#[\w/-]+(\s+#[\w/-]+)*\s*$"))
            v.Add(("body-has-inline-tags", "warn", false, i + 1, trim));
    }

    return v;
}

static string ReadFmField(string path, string field)
{
    if (!File.Exists(path)) return null;
    var lines = File.ReadAllLines(path);
    bool inFm = false;
    int delim = 0;
    foreach (var line in lines)
    {
        if (line.Trim() == "---")
        {
            delim++;
            if (delim == 1) { inFm = true; continue; }
            if (delim == 2) break;
        }
        if (!inFm) continue;
        var m = Regex.Match(line, $@"^\s*{Regex.Escape(field)}:\s*(.*)$");
        if (m.Success) return Unquote(m.Groups[1].Value.Trim());
    }
    return null;
}

static string Unquote(string s) => s == null ? null : s.Trim().Trim('"').Trim('\'');

static string Js(string s)
{
    if (s == null) return "null";
    return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                   .Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t") + "\"";
}
