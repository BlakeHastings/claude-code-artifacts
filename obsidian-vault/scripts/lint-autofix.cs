#pragma warning disable CS8603, CS8600

// Applies safe, unambiguous lint fixes to a single note in Blake's vault.
//
// Usage: dotnet run scripts/lint-autofix.cs -- "<path-to-note>"
//
// Safe fixes applied:
//   - Remove body H1 that matches the filename
//   - Convert ```ad-TYPE blocks to native > [!type] callouts
//   - Bump standard-version in frontmatter to current
//   - Bump template-version in frontmatter to current (when template is known)
//   - Add standard-version if missing (set to current)
//   - Add template-version if missing AND template is known (set to current)
//
// Will NOT:
//   - Add frontmatter to a file that has none
//   - Add missing id, template, or tags fields (requires judgment)
//   - Touch body-level Status/Tags lines or inline hashtag lines (requires judgment)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

const string VaultPath = @"C:\Users\Blake\Documents\main";
const string SkillPath = @"C:\Users\Blake\.claude\skills\obsidian-vault";

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run scripts/lint-autofix.cs -- <path-to-note>");
    return 1;
}

string notePath = args[0];
if (!Path.IsPathRooted(notePath)) notePath = Path.Combine(VaultPath, notePath);
if (!File.Exists(notePath))
{
    Console.Error.WriteLine($"File not found: {notePath}");
    return 1;
}

string standardsReadme = Path.Combine(SkillPath, "references", "standards", "README.md");
string currentStdVer = ReadFmField(standardsReadme, "standards-version");
if (currentStdVer == null)
{
    Console.Error.WriteLine("Cannot determine current standards version from " + standardsReadme);
    return 1;
}

var currentTplVers = new Dictionary<string, string>();
string templatesDir = Path.Combine(VaultPath, "Templates");
foreach (var tf in Directory.GetFiles(templatesDir, "*.md"))
{
    var n = ReadFmField(tf, "template");
    var v = ReadFmField(tf, "template-version");
    if (n != null && v != null) currentTplVers[n] = v;
}

var lines = File.ReadAllLines(notePath).ToList();
string filename = Path.GetFileNameWithoutExtension(notePath);
var fixes = new List<string>();

// Locate frontmatter
bool hasFm = lines.Count > 0 && lines[0].Trim() == "---";
int fmEnd = -1;
if (hasFm)
{
    for (int i = 1; i < lines.Count; i++)
    {
        if (lines[i].Trim() == "---") { fmEnd = i; break; }
    }
}

// FIX: frontmatter version bumps and additions (only if frontmatter exists)
if (hasFm && fmEnd > 0)
{
    string currentTpl = null;
    int tplLine = -1;
    int tplVerLine = -1;
    int stdVerLine = -1;

    for (int i = 1; i < fmEnd; i++)
    {
        var t = lines[i].TrimStart();
        if (t.StartsWith("template:") && !t.StartsWith("template-version:"))
        {
            tplLine = i;
            currentTpl = Unquote(t.Substring("template:".Length).Trim());
        }
        else if (t.StartsWith("template-version:")) tplVerLine = i;
        else if (t.StartsWith("standard-version:")) stdVerLine = i;
    }

    // standard-version
    if (stdVerLine >= 0)
    {
        var t = lines[stdVerLine].TrimStart();
        var curVal = Unquote(t.Substring("standard-version:".Length).Trim());
        if (curVal != currentStdVer)
        {
            lines[stdVerLine] = $"standard-version: \"{currentStdVer}\"";
            fixes.Add($"bumped standard-version: {curVal} -> {currentStdVer}");
        }
    }
    else
    {
        lines.Insert(fmEnd, $"standard-version: \"{currentStdVer}\"");
        fmEnd++;
        if (tplLine > fmEnd) tplLine++;
        if (tplVerLine > fmEnd) tplVerLine++;
        fixes.Add($"added standard-version: {currentStdVer}");
    }

    // template-version (only if template is known)
    if (currentTpl != null && currentTplVers.TryGetValue(currentTpl, out var latestTplVer))
    {
        if (tplVerLine >= 0)
        {
            var t = lines[tplVerLine].TrimStart();
            var curVal = Unquote(t.Substring("template-version:".Length).Trim());
            if (curVal != latestTplVer)
            {
                lines[tplVerLine] = $"template-version: \"{latestTplVer}\"";
                fixes.Add($"bumped template-version: {curVal} -> {latestTplVer}");
            }
        }
        else if (tplLine >= 0)
        {
            lines.Insert(tplLine + 1, $"template-version: \"{latestTplVer}\"");
            fmEnd++;
            fixes.Add($"added template-version: {latestTplVer}");
        }
    }
}

// FIX: remove H1 matching filename (first content line after frontmatter)
int bodyStart = fmEnd == -1 ? 0 : fmEnd + 1;
for (int i = bodyStart; i < lines.Count; i++)
{
    var t = lines[i].Trim();
    if (t.Length == 0) continue;
    if (t.StartsWith("# "))
    {
        var h1 = t.Substring(2).Trim();
        if (string.Equals(h1, filename, StringComparison.OrdinalIgnoreCase))
        {
            lines.RemoveAt(i);
            if (i < lines.Count && lines[i].Trim().Length == 0) lines.RemoveAt(i);
            fixes.Add($"removed H1 matching filename: {h1}");
        }
    }
    break;
}

// FIX: convert ```ad-TYPE blocks to > [!type] callouts
var admonitionTypes = new HashSet<string> { "quote", "important", "note", "abstract", "warning", "tip", "info", "caution", "danger", "example" };

var rewritten = new List<string>();
int idx = 0;
while (idx < lines.Count)
{
    var line = lines[idx];
    var m = Regex.Match(line.TrimStart(), @"^```ad-(\w+)\s*$");
    if (m.Success)
    {
        string type = m.Groups[1].Value.ToLower();
        string mapped = admonitionTypes.Contains(type) ? type : type;
        var content = new List<string>();
        int j = idx + 1;
        bool foundClose = false;
        while (j < lines.Count)
        {
            if (lines[j].TrimStart() == "```")
            {
                foundClose = true;
                break;
            }
            content.Add(lines[j]);
            j++;
        }
        if (foundClose)
        {
            rewritten.Add($"> [!{mapped}]");
            foreach (var c in content)
                rewritten.Add(string.IsNullOrWhiteSpace(c) ? ">" : $"> {c}");
            fixes.Add($"converted ad-{type} block (line {idx + 1}) to > [!{mapped}] callout");
            idx = j + 1;
            continue;
        }
    }
    rewritten.Add(line);
    idx++;
}
lines = rewritten;

if (fixes.Count > 0)
{
    File.WriteAllLines(notePath, lines);
}

Console.WriteLine($"File: {notePath}");
Console.WriteLine($"Fixes applied: {fixes.Count}");
foreach (var f in fixes) Console.WriteLine($"  - {f}");

return 0;

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
