// Enumerates templates in Blake's Obsidian vault and reports each one's
// frontmatter metadata. Used by the create-note operation to avoid hardcoding
// a template list anywhere in the skill.
//
// Usage: dotnet run scripts/list-templates.cs

using System;
using System.IO;
using System.Linq;

const string VaultPath = @"C:\Users\Blake\Documents\main";
string templatesDir = Path.Combine(VaultPath, "Templates");

if (!Directory.Exists(templatesDir))
{
    Console.Error.WriteLine($"Templates directory not found: {templatesDir}");
    return 1;
}

var templates = Directory.GetFiles(templatesDir, "*.md")
    .OrderBy(f => Path.GetFileNameWithoutExtension(f))
    .ToList();

Console.WriteLine($"Found {templates.Count} template(s) in {templatesDir}");
Console.WriteLine();

foreach (var file in templates)
{
    string name = Path.GetFileNameWithoutExtension(file);
    string templateField = "(missing)";
    string templateVersion = "(missing)";
    string standardVersion = "(missing)";
    string primaryTag = "(missing)";

    var lines = File.ReadAllLines(file);
    bool inFrontmatter = false;
    bool inTagsList = false;
    int delimiters = 0;

    foreach (var line in lines)
    {
        string trimmed = line.Trim();

        if (trimmed == "---")
        {
            delimiters++;
            if (delimiters == 1) { inFrontmatter = true; continue; }
            if (delimiters == 2) { inFrontmatter = false; break; }
        }

        if (!inFrontmatter) continue;

        if (trimmed.StartsWith("template:") && !trimmed.StartsWith("template-version:"))
        {
            templateField = trimmed.Substring("template:".Length).Trim().Trim('"');
            inTagsList = false;
        }
        else if (trimmed.StartsWith("template-version:"))
        {
            templateVersion = trimmed.Substring("template-version:".Length).Trim().Trim('"');
            inTagsList = false;
        }
        else if (trimmed.StartsWith("standard-version:"))
        {
            standardVersion = trimmed.Substring("standard-version:".Length).Trim().Trim('"');
            inTagsList = false;
        }
        else if (trimmed.StartsWith("tags:"))
        {
            inTagsList = true;
            string inline = trimmed.Substring("tags:".Length).Trim();
            if (inline.Length > 0 && inline != "[]")
            {
                primaryTag = inline.Trim('[', ']').Split(',')[0].Trim().Trim('"');
                inTagsList = false;
            }
        }
        else if (inTagsList && trimmed.StartsWith("-"))
        {
            if (primaryTag == "(missing)")
            {
                primaryTag = trimmed.TrimStart('-').Trim().Trim('"');
            }
        }
        else if (inTagsList && !trimmed.StartsWith("-") && trimmed.Length > 0)
        {
            inTagsList = false;
        }
    }

    Console.WriteLine($"- {name}");
    Console.WriteLine($"    file:             {Path.GetFileName(file)}");
    Console.WriteLine($"    template:         {templateField}");
    Console.WriteLine($"    template-version: {templateVersion}");
    Console.WriteLine($"    standard-version: {standardVersion}");
    Console.WriteLine($"    primary tag:      {primaryTag}");
    Console.WriteLine();
}

return 0;
