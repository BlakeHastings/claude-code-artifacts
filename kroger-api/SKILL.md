---
name: kroger-api
description: >-
  Interact with the Kroger Public API. Use when the user wants to search
  Kroger products, find store locations, view chains or departments, add
  items to a Kroger shopping cart, or access an authenticated customer
  profile. Handles OAuth2 authentication (client credentials and authorization
  code with PKCE), product search and detail lookup, location and store
  search, chain and department lookup, cart management, and identity profile
  retrieval. Covers all public Kroger API endpoints.
argument-hint: "<auth|products|locations|cart|identity> [subcommand] [args]"
allowed-tools: Bash(dotnet run *), Read
---
# Kroger API

Interact with all endpoints of the Kroger Public API. Read `references/api-ref.md`
for the full endpoint reference, and `references/auth-guide.md` for OAuth2 details.

## Argument Parsing

Parse `$ARGUMENTS` — the first token routes to a script:

| First Token  | Script                 | Description                              |
|--------------|------------------------|------------------------------------------|
| `auth`       | `scripts/auth.cs`      | Token acquisition and management         |
| `products`   | `scripts/products.cs`  | Product search and detail lookup         |
| `locations`  | `scripts/locations.cs` | Store, chain, and department lookup      |
| `cart`       | `scripts/cart.cs`      | Add items to an authenticated cart       |
| `identity`   | `scripts/identity.cs`  | Authenticated customer profile           |

If no arguments or unrecognized first token, show this usage summary and stop.

## First-Time Setup

Read `references/auth-guide.md` and guide the user through setup. Register at
https://developer.kroger.com to obtain credentials, then run:

```
auth setup
```

This interactive command stores all credentials in the OS credential store
(Windows Credential Manager / macOS Keychain / Linux Secret Service) — never plaintext.

## Operations

### Auth
```
dotnet run scripts/auth.cs -- $2 $3 $4 $5 $6 $7
```
Subcommands: `setup`, `client [scope]`, `login`, `url [--scope <s>]`, `exchange <code>`, `refresh`, `status`

### Products
```
dotnet run scripts/products.cs -- $2 $3 $4 $5 $6 $7
```
Subcommands: `search <term>`, `get <productId>`

### Locations
```
dotnet run scripts/locations.cs -- $2 $3 $4 $5 $6 $7
```
Subcommands: `search`, `get <id>`, `chains`, `chain <name>`, `departments`, `department <id>`

### Cart
```
dotnet run scripts/cart.cs -- $2 $3 $4 $5 $6 $7
```
Subcommands: `add <items...> [--modality DELIVERY|PICKUP]`

### Identity
```
dotnet run scripts/identity.cs -- $2 $3 $4 $5 $6 $7
```
Subcommands: `profile`

## Reference Files

- `references/api-ref.md` — Full endpoint reference, parameters, and rate limits
- `references/auth-guide.md` — OAuth2 flows, scopes, and token management
