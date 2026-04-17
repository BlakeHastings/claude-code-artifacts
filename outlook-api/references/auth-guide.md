# Outlook API — Authentication Guide

## Overview

This skill uses **OAuth2 Authorization Code with PKCE** to authenticate with Microsoft's personal account infrastructure. No client secret is required — this is a **public client** registration, which is the recommended approach for desktop/CLI apps.

Tokens are stored in **Windows Credential Manager** (via `Devlooped.CredentialManager`) — never in plaintext files.

---

## Step 1: Create an Azure App Registration (one-time, ~5 minutes)

1. Go to **[https://portal.azure.com](https://portal.azure.com)** and sign in with your Microsoft account.
2. Navigate to **Azure Active Directory → App registrations → New registration**
3. Fill in:
   - **Name**: anything (e.g., `Claude Outlook`)
   - **Supported account types**: `Personal Microsoft accounts only`
   - **Redirect URI**: Select `Public client/native (mobile & desktop)` → enter `http://localhost:8080/callback`
4. Click **Register**. Copy the **Application (client) ID** — you'll need it.
5. In the app's **Authentication** blade:
   - Under **Advanced settings**, set `Allow public client flows` = **Yes**
   - Ensure the redirect URI `http://localhost:8080/callback` is listed
6. In the **API permissions** blade, add these **Microsoft Graph Delegated permissions**:
   - `Mail.ReadWrite`
   - `Mail.Send`
   - `MailboxSettings.ReadWrite`
   - `Calendars.ReadWrite`
   - `Contacts.ReadWrite`
   - `offline_access` (enables refresh tokens)
   - Click **Grant admin consent** if the button is available (optional for personal accounts — users consent on login)

---

## Step 2: Configure the Skill

```
/outlook-api auth setup
```

Paste your Application (client) ID when prompted. It is stored securely in Windows Credential Manager under the key `outlook-api/client-id`.

---

## Step 3: Login

```
/outlook-api auth login
```

This:
1. Generates a PKCE `code_verifier` + `code_challenge` (SHA256)
2. Starts a temporary HTTP listener on `http://localhost:8080`
3. Opens your browser to the Microsoft sign-in page
4. Captures the authorization callback automatically
5. Exchanges the code + verifier for access + refresh tokens
6. Saves tokens to Windows Credential Manager

The browser flow will ask you to sign in and consent to the requested permissions.

**Custom port** (if 8080 is in use — remember to also register the new URI in your Azure app):
```
/outlook-api auth login --port 9090
```

---

## Token Lifetime & Refresh

| Token Type    | Lifetime         | Notes                                    |
|---------------|------------------|------------------------------------------|
| Access Token  | 1 hour           | Auto-refreshed by each script            |
| Refresh Token | 90 days          | Silently obtains new access tokens       |

All scripts automatically detect an expired access token and silently refresh it using the stored refresh token. If the refresh token is also expired (after 90 days of inactivity), re-run `auth login`.

---

## Credential Storage

All credentials are stored in **Windows Credential Manager** under the `outlook-api` service:

| Credential Key          | Account   | Contains                          |
|-------------------------|-----------|-----------------------------------|
| `outlook-api/client-id` | `outlook` | Azure App Client ID               |
| `outlook-api/token`     | `outlook` | Access token + refresh token (JSON) |

To view in Windows: Start → Credential Manager → Windows Credentials → look for `outlook-api`

---

## Status & Troubleshooting

```
/outlook-api auth status    — Show Client ID and token expiry
/outlook-api auth refresh   — Manually refresh the access token
/outlook-api auth logout    — Remove stored tokens (keeps Client ID)
```

**Common issues:**

| Error | Fix |
|-------|-----|
| `AADSTS50011: redirect_uri mismatch` | Ensure `http://localhost:8080/callback` is registered in your Azure app's Authentication blade |
| `AADSTS70011: Invalid scope` | Check that all required Graph permissions are added to your app |
| `Could not start listener on port 8080` | Use `auth login --port 9090` and register the new URI |
| `Token refresh failed` | Run `auth login` again to re-authenticate |
| `Client ID not configured` | Run `auth setup` first |

---

## Security Notes

- No client secret is used. PKCE prevents authorization code interception attacks.
- Tokens are stored encrypted in Windows Credential Manager, not in files.
- The skill never writes credentials to disk in plaintext.
- Refresh tokens expire after 90 days without use — re-authenticate if needed.
