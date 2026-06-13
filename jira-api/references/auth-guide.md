# Jira Auth Guide

Jira Cloud's REST API uses **Basic authentication** with your Atlassian account
email and an **API token** (not your password). The token is sent as
`Authorization: Basic base64(email:token)`.

## 1. Create an API token

1. Go to https://id.atlassian.com/manage-profile/security/api-tokens
2. Click **Create API token**.
3. Give it a label (e.g. `claude-jira-skill`) and copy the token. You only see it once.

> Tokens carry the same permissions as your account. Treat them like passwords.
> To revoke, return to the same page and delete the token.

## 2. Find your site

Your Jira Cloud URL looks like `https://YOURSITE.atlassian.net`. The **site** is
the subdomain — for `https://acme.atlassian.net`, the site is `acme`. The
`auth setup` command also accepts the full URL and extracts the subdomain for you.

## 3. Store credentials

```
dotnet run scripts/jira.cs -- auth setup --site acme --email you@example.com --token <api-token>
```

This saves the site, email, and token to the OS credential store:

| Platform | Backend                          |
|----------|----------------------------------|
| Windows  | Windows Credential Manager       |
| macOS    | Keychain                         |
| Linux    | Secret Service (GNOME Keyring / KWallet) |

Nothing is written to disk in plaintext, and nothing is hardcoded in the skill,
so each user keeps their own token locally.

## 4. Verify

```
dotnet run scripts/jira.cs -- auth status
```

This calls `GET /rest/api/3/myself` and prints the signed-in display name, email,
and account ID. A `401` means the email/token pair is wrong; a `403` usually means
the token is valid but lacks permission for the resource.

## Clearing credentials

```
dotnet run scripts/jira.cs -- auth logout
```

Removes the stored site, email, and token from the credential store.
