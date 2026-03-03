# .NET Single-File Scripts in Skills

> Official docs: https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps

## What Are They?

`.cs` files that run directly with `dotnet run file.cs` — no project file needed. Available in .NET 10+. They're cross-platform, can reference NuGet packages, and are ideal for deterministic skill operations.

## Syntax Quick Reference

```csharp
#:package Name@Version          // Add NuGet dependency
#:package Name@*                // Latest version
#:project ../path/to.csproj     // Reference a project
#:property Key=Value            // Set MSBuild property
#:sdk Microsoft.NET.Sdk.Web     // Specify SDK (default: Microsoft.NET.Sdk)
```

Directives must appear at the top of the file before any C# code.

## Running Scripts

```bash
dotnet run script.cs                    # Standard invocation
dotnet script.cs                        # Shorthand
dotnet run --file script.cs             # Explicit flag form
dotnet run script.cs -- arg1 arg2       # Pass arguments (access via args[])
```

## Minimal Example

```csharp
#:package Spectre.Console@*

using Spectre.Console;

AnsiConsole.MarkupLine("[green]Hello from a single-file script![/]");
```

## Example With Arguments

```csharp
if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run script.cs -- <name>");
    return 1;
}

Console.WriteLine($"Hello, {args[0]}!");
return 0;
```

## Secrets Management

Never hardcode API keys, tokens, or credentials in scripts. Use `dotnet user-secrets`.

> Docs: https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets

### Setup

```bash
# Set a secret (--id ties secrets to a logical group, no .csproj needed)
dotnet user-secrets set "ApiKey" "your-api-key-here" --id my-skill-secrets
dotnet user-secrets set "ConnectionString" "Server=..." --id my-skill-secrets

# List secrets
dotnet user-secrets list --id my-skill-secrets

# Remove a secret
dotnet user-secrets remove "ApiKey" --id my-skill-secrets
```

### Access in Script

```csharp
#:package Microsoft.Extensions.Configuration@*
#:package Microsoft.Extensions.Configuration.UserSecrets@*

using Microsoft.Extensions.Configuration;

var config = new ConfigurationBuilder()
    .AddUserSecrets("my-skill-secrets")
    .Build();

var apiKey = config["ApiKey"]
    ?? throw new InvalidOperationException(
        "ApiKey not configured. Run: dotnet user-secrets set \"ApiKey\" \"value\" --id my-skill-secrets");
```

Always include a helpful error message when a secret is missing — tell the user exactly what command to run.

## When to Use Scripts vs. Claude's Tools

| Use Scripts For | Use Claude's Tools For |
|----------------|----------------------|
| Deterministic operations | File discovery and content analysis |
| API integrations | Code generation |
| Data transformations | Interactive decision-making |
| Complex validation logic | Natural language processing |
| File format conversions | Pattern matching across codebases |
| Anything that must produce the same output given the same input | Contextual reasoning |

## Script Placement Convention

```
skill-name/
├── SKILL.md
└── scripts/
    ├── action.cs
    └── helper.cs
```

Place scripts in a `scripts/` directory inside the skill folder.

## Gotchas

- **Don't place `.cs` scripts in a directory with a `.csproj` file.** The build system will try to compile them as part of the project. Keep scripts in a dedicated `scripts/` directory.
- **Use `return` for exit codes in top-level statements.** `return 0;` for success, `return 1;` for failure.
- **`args` is available implicitly** in top-level statements — no need to declare `Main(string[] args)`.
- **Package restore runs on first execution.** Subsequent runs are cached and fast.
