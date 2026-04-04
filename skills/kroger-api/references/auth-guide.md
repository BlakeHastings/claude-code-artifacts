# Kroger API Authentication Guide

## Overview

The Kroger API uses OAuth2. Two flows are available depending on what data you need:

| Flow                 | Use For                              | Scopes                              |
|----------------------|--------------------------------------|-------------------------------------|
| Client Credentials   | Products, Locations (public data)    | `product.compact`                   |
| Authorization Code   | Cart, Identity (user-specific data)  | `cart.basic:write`, `profile.compact` |

---

## Flow 1: Client Credentials

For accessing public data — no user login required.

**Steps:**
1. Run `auth client <scope>` to obtain a token
2. Token is saved to `~/.kroger-api/client-token.json`
3. All subsequent product/location calls auto-refresh when expired

**Example:**
```
/kroger-api auth client product.compact
```

**Getting a token for multiple scopes:**
```
/kroger-api auth client "product.compact"
```

---

## Flow 2: Authorization Code

For user-specific operations (cart, identity). The user must authorize in a browser.

### Automated flow (recommended)

`auth login` opens your browser, spins up a temporary local HTTP listener, catches
the redirect automatically, and exchanges the code — no copy/pasting required.

**One-time setup:** register `http://localhost:8080/callback` as a redirect URI in
your Kroger developer app. Then run:

```
/kroger-api auth login --scope "cart.basic:write profile.compact"
```

Use `--port` if 8080 is already in use (register the new URI in your app too):
```
/kroger-api auth login --port 9090 --scope "cart.basic:write profile.compact"
```

### Manual flow (fallback)

If the automated flow can't be used:

1. Run `auth url --scope "<scopes>"` to generate the authorization URL
2. Visit the URL and authorize
3. Kroger redirects — copy the `?code=` value from the browser address bar
4. Run `auth exchange <code>`

```
/kroger-api auth url --scope "cart.basic:write profile.compact"
# → Visit URL, authorize, copy the ?code= from the redirect address bar

/kroger-api auth exchange abc123xyz
```

---

## PKCE (Enhanced Security)

PKCE (Proof Key for Code Exchange) prevents authorization code interception attacks.

1. Generate a `code_verifier` (random 43–128 char string)
2. Compute `code_challenge = BASE64URL(SHA256(code_verifier))`
3. Include `code_challenge` and `code_challenge_method=S256` in the auth URL
4. Pass `--verifier <code_verifier>` when exchanging the code:

```
/kroger-api auth exchange <code> --verifier <code_verifier>
```

---

## Available Scopes

| Scope             | Flow               | Grants Access To                      |
|-------------------|--------------------|---------------------------------------|
| `product.compact` | Client Credentials | Search and view product data          |
| `profile.compact` | User OAuth2        | Read authenticated customer profile   |
| `cart.basic:write`| User OAuth2        | Add items to authenticated cart       |

---

## Token Storage

Tokens are stored in the OS credential store via `Devlooped.CredentialManager` — never in plaintext files.

| Key             | Account  | Contains                                    |
|-----------------|----------|---------------------------------------------|
| `client-token`  | `kroger` | Client credentials access token + expiry    |
| `user-token`    | `kroger` | User access token + refresh token + expiry  |

The credential store used depends on the OS:
- **Windows**: Windows Credential Manager
- **macOS**: Keychain
- **Linux**: Secret Service API (GNOME Keyring / KWallet); requires a running secrets daemon

Token values are stored as serialized JSON in the credential's password field.

---

## Token Lifetime & Refresh

- Access tokens expire in **30 minutes** (1,800 seconds)
- Scripts automatically detect and refresh expired tokens
- User tokens use the refresh token; client tokens re-authenticate directly
- Use `auth status` to inspect current token state

---

## Credential Setup

All credentials are stored in the OS credential store via `Devlooped.CredentialManager`. Run the interactive setup command:

```
auth setup
```

This prompts for your Client ID, Client Secret, and Redirect URI, then saves them securely. No plaintext files are written.

Obtain credentials by registering an app at https://developer.kroger.com.

To re-run setup at any time (e.g., to rotate secrets), simply run `auth setup` again — it overwrites existing values.
