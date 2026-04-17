---
name: outlook-api
description: >-
  Interact with Microsoft Outlook / Office 365 via the Microsoft Graph API.
  Use when the user wants to read, send, reply, forward, search, delete, or
  organize email; manage calendar events; look up or create contacts; or manage
  mail folders. Handles OAuth2 PKCE authentication for personal Microsoft
  accounts. Covers mail, calendar, contacts, and folder operations through
  direct Graph API calls — no MCP server required.
argument-hint: "<auth|mail|calendar|contacts|folders|tasks|settings> <subcommand> [args]"
allowed-tools: Bash(dotnet run *), Read
---
# Outlook API

Interact with Microsoft Outlook and Office 365 via the Microsoft Graph API.
Read `references/api-ref.md` for the full endpoint reference and
`references/auth-guide.md` for OAuth2 / Azure app registration details.

## Argument Parsing

Parse `$ARGUMENTS` — the first token routes to the appropriate subcommand within the single consolidated script:

| First Token  | Description                                            |
|--------------|--------------------------------------------------------|
| `auth`       | OAuth2 setup, login, token management                  |
| `mail`       | Email read, send, reply, search, drafts, attachments   |
| `calendar`   | Events: list, create, update, delete, respond          |
| `contacts`   | Contacts: list, search, get, create, update, delete    |
| `folders`    | Mail folders: list, create, rename, delete             |
| `tasks`      | Microsoft To-Do: lists, list, create, complete, delete |
| `settings`   | Mailbox settings: timezone, auto-reply, working hours  |

If no arguments or unrecognized first token, show this usage summary and stop.

## First-Time Setup

Read `references/auth-guide.md` and guide the user through:
1. Creating a free Azure App Registration (personal account support)
2. Running `auth setup` to store the Client ID
3. Running `auth login` to complete OAuth2 PKCE browser flow

## Operations

All operations use `scripts/outlook.cs`. Commands work in both bash and PowerShell shells (OpenCode's Bash tool uses pwsh.exe on Windows). Just call them directly - no need for `cd` or special shell syntax:

```bash
dotnet run scripts/outlook.cs -- auth setup <client-id>
```

The working directory is automatically set to the skill root when invoked through OpenCode, so relative paths like `scripts/outlook.cs` work correctly.

### Auth
```
dotnet run scripts/outlook.cs -- auth $2 $3 $4 $5 $6 $7
```
Subcommands: `setup <client-id>`, `login [--port <n>]`, `refresh`, `status`, `logout`

### Mail
```
dotnet run scripts/outlook.cs -- mail $2 $3 $4 $5 $6 $7 $8 $9 ${10} ${11} ${12} ${13} ${14} ${15}
```
Subcommands:
- `list [--folder <name>] [--top <n>] [--unread] [--from <addr>] [--subject <s>]`
- `read <id> [--full-body] [--raw]`
- `send --to <addr> --subject <s> --body <text> [--cc <a>] [--bcc <a>] [--html] [--body-file <p>] [--attach <path>]`
- `reply <id> --body <text> [--html] [--body-file <path>]`
- `reply-all <id> --body <text> [--html] [--body-file <path>]`
- `forward <id> --to <addr> [--body <text>] [--body-file <path>]`
- `delete <id>`
- `move <id> --to <folder>`
- `search <query> [--top <n>]`
- `flag <id>`, `unflag <id>`, `mark-read <id>`, `mark-unread <id>`
- `draft list`
- `draft create --to <addr> --subject <s> --body <text> [--html] [--body-file <path>] [--attach <path>]`
- `draft reply <messageId> [--body <text>] [--body-file <path>] [--html]`
- `draft reply-all <messageId> [--body <text>] [--body-file <path>] [--html]`
- `draft update <id> [--to <addr>] [--subject <s>] [--body <text>] [--body-file <path>]`
- `draft send <id>`
- `attachment list <messageId>`
- `attachment get <messageId> <attachmentId> [--out <path>]`
- `categories list`
- `categories apply <messageId> <categoryName>`
- `categories remove <messageId> <categoryName>`

### Calendar
```
dotnet run scripts/outlook.cs -- calendar $2 $3 $4 $5 $6 $7 $8 $9 ${10} ${11} ${12} ${13} ${14} ${15}
```
Subcommands:
- `calendars`                                          List all calendars with IDs
- `list [--start <date>] [--end <date>] [--top <n>] [--calendar <id>]`
- `get <id>`
- `create --subject <s> --start <dt> --end <dt> [--location <l>] [--body <b>] [--attendees <e,...>] [--all-day] [--reminder <minutes>] [--calendar <id>]`
- `update <id> [--subject <s>] [--start <dt>] [--end <dt>] [--location <l>] [--body <b>] [--reminder <minutes>]`
- `delete <id>`
- `respond <id> <accept|tentative|decline> [--comment <c>]`

### Contacts
```
dotnet run scripts/outlook.cs -- contacts $2 $3 $4 $5 $6 $7 $8 $9 ${10} ${11}
```
Subcommands:
- `list [--top <n>]`
- `search <query>`
- `get <id>`
- `create --first <fn> --last <ln> [--email <e>] [--phone <p>] [--company <c>] [--title <t>]`
- `update <id> [--first <fn>] [--last <ln>] [--email <e>] [--phone <p>] [--company <c>] [--title <t>]`
- `delete <id>`

### Folders
```
dotnet run scripts/outlook.cs -- folders $2 $3 $4 $5 $6 $7 $8 $9
```
Subcommands:
- `list [--parent <folderId>]`
- `create --name <n> [--parent <folderId>]`
- `rename <id> --name <n>`
- `delete <id>`

### Tasks
```
dotnet run scripts/outlook.cs -- tasks $2 $3 $4 $5 $6 $7 $8 $9 ${10} ${11}
```
Subcommands:
- `lists`                                              List all To-Do task lists
- `list [--list <listId>] [--top <n>] [--completed]`  Default list: "Tasks"
- `get <taskId> [--list <listId>]`
- `create --title <t> [--list <id>] [--due <date>] [--body <notes>] [--important]`
- `complete <taskId> [--list <listId>]`
- `delete <taskId> [--list <listId>]`

> **Note:** Tasks require re-authentication after first setup to grant `Tasks.ReadWrite` scope. Run `auth login` if tasks return a 403.

### Settings
```
dotnet run scripts/outlook.cs -- settings $2 $3 $4 $5 $6 $7 $8 $9
```
Subcommands:
- `get`                                                Show timezone, auto-reply status, working hours
- `timezone <TimeZoneId>`                              e.g. `"Central Standard Time"`
- `auto-reply enable --message <text> [--external-message <text>] [--start <dt>] [--end <dt>]`
- `auto-reply disable`

## Reference Files

- `references/auth-guide.md` — Azure app registration, OAuth2 PKCE flow, scopes, token management
- `references/api-ref.md` — Graph API endpoints, OData filters, common patterns, rate limits

## Notes for Agents

- Always run `auth status` first to confirm the token is valid before performing operations
- Use `--top` to limit results; default is 10 for lists
- Message IDs from Graph API are long base64 strings — pass them exactly as returned
- Well-known folder names: `inbox`, `drafts`, `sentitems`, `deleteditems`, `junkemail`, `outbox`
- For multi-line bodies, write to a temp file and use `--body-file <path>` instead of `--body`
- Dates accept ISO 8601 format: `2026-04-15` or `2026-04-15T14:00:00`

## Search Strategy

`mail search` does full-text search across subject, body, and sender fields. Follow these steps when looking for a specific email:

1. **Start broad, then narrow**: Search by institution or person name first. If results are newsletters/noise, add topic keywords (`speak`, `invitation`, `meeting`, etc.)
2. **Sender names don't always match institutions**: An email from the University of Memphis may show sender as `Emmanuel Oduro` or `eoduro@memphis.edu`, not "University of Memphis". Try topic words from the email body when institution name searches miss.
3. **Try multiple search angles in parallel** if the first doesn't find it:
   - Institution name: `mail search "University of Memphis"`
   - Topic + location: `mail search "speak Memphis"`
   - Role + topic: `mail search "UofM speaker"`
4. **Check sent items** when researching history with a person: `mail list --folder sentitems --top 50`
5. **Duplicates happen**: Senders sometimes send the same email twice. When replying, use the most recent copy (latest `Received` timestamp).
6. **Read the full body** with `mail read <id> --full-body` once you identify the right email — previews truncate and may omit key details like event dates, times, or links.
