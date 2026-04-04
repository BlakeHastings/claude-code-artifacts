# Kroger API Reference

## Base URL
`https://api.kroger.com`

## Authentication
All API requests require: `Authorization: Bearer <access_token>`
Token requests require: `Authorization: Basic <base64(clientId:clientSecret)>`

---

## Products API

**Rate limit:** 10,000 calls/day
**Scope:** `product.compact` (client credentials)

### GET /v1/products
Search products by keyword and optional filters.

| Parameter             | Type    | Description                                           |
|-----------------------|---------|-------------------------------------------------------|
| `filter.term`         | string  | Search keyword                                        |
| `filter.locationId`   | string  | Store location ID for pricing and availability        |
| `filter.productId`    | string  | Product ID(s), comma-separated                        |
| `filter.brand`        | string  | Brand name(s), pipe-separated for multiple            |
| `filter.fulfillment`  | string  | Fulfillment type(s), comma-separated                  |
| `filter.start`        | integer | Pagination offset                                     |
| `filter.limit`        | integer | Max results (1–50, default: 10)                       |

### GET /v1/products/{productId}
Get product details by UPC or product ID.

| Parameter           | Type   | Description                                   |
|---------------------|--------|-----------------------------------------------|
| `filter.locationId` | string | Optional: include location-based pricing info |

---

## Locations API

**Rate limit:** 1,600 calls/day per endpoint
**Scope:** `product.compact` (client credentials)

### GET /v1/locations
Search store locations. At least one geographic filter required.

| Parameter               | Type    | Description                                     |
|-------------------------|---------|-------------------------------------------------|
| `filter.zipCode.near`   | string  | ZIP code for geographic search                  |
| `filter.latLong.near`   | string  | Lat,Long pair (e.g., `39.7,-84.1`)             |
| `filter.lat.near`       | float   | Latitude                                        |
| `filter.lon.near`       | float   | Longitude                                       |
| `filter.radiusInMiles`  | integer | Search radius (1–100 miles)                     |
| `filter.limit`          | integer | Max results (1–200, default: 10)               |
| `filter.chain`          | string  | Filter by chain name                            |
| `filter.department`     | string  | Filter by department ID(s), comma-separated     |
| `filter.locationId`     | string  | Filter by location ID(s), comma-separated       |

### GET /v1/locations/{locationId}
Get full details for a specific location.

### HEAD /v1/locations/{locationId}
Check if a location exists (200 = exists, 404 = not found).

### GET /v1/chains
List all retail chains owned by The Kroger Co.

### GET /v1/chains/{name}
Get details for a specific chain by name.

### HEAD /v1/chains/{name}
Check if a chain exists.

### GET /v1/departments
List all departments.

### GET /v1/departments/{departmentId}
Get details for a specific department.

### HEAD /v1/departments/{departmentId}
Check if a department exists.

---

## Cart API

**Rate limit:** 5,000 calls/day
**Scope:** `cart.basic:write` (user OAuth2 required)

### PUT /v1/cart/add
Add items to an authenticated customer's shopping cart.

Accepts one or more items in a single request. Returns 204 No Content on success.

**Request body:**
```json
{
  "items": [
    { "upc": "0001111060903", "quantity": 2, "modality": "PICKUP" },
    { "upc": "0001234567890", "quantity": 1 }
  ]
}
```

| Field      | Type    | Required | Description                     |
|------------|---------|----------|---------------------------------|
| `upc`      | string  | yes      | Product UPC code                |
| `quantity` | integer | yes      | Quantity to add (minimum: 1)    |
| `modality` | string  | no       | `DELIVERY` or `PICKUP`         |

**Skill syntax:** `upc:qty` or `upc:qty:MODALITY` — multiple items separated by spaces.

---

## Identity API

**Rate limit:** 5,000 calls/day
**Scope:** `profile.compact` (user OAuth2 required)

### GET /v1/identity/profile
Get the profile of the currently authenticated customer.

Returns: Customer profile ID and basic account information.

---

## Authorization Endpoints

### POST /v1/connect/oauth2/token
Exchange credentials for an access token.

**Headers:** `Authorization: Basic <base64(clientId:clientSecret)>`
**Content-Type:** `application/x-www-form-urlencoded`

| Parameter      | Grant Type           | Description                        |
|----------------|----------------------|------------------------------------|
| `grant_type`   | all                  | `client_credentials`, `authorization_code`, or `refresh_token` |
| `scope`        | client_credentials   | Space-separated scope string       |
| `code`         | authorization_code   | Authorization code from redirect   |
| `redirect_uri` | authorization_code   | Must match registered URI          |
| `code_verifier`| authorization_code   | PKCE verifier (optional)           |
| `refresh_token`| refresh_token        | Previously issued refresh token    |

**Response:**
```json
{
  "access_token": "...",
  "token_type": "Bearer",
  "expires_in": 1800,
  "refresh_token": "...",
  "scope": "product.compact"
}
```

### GET /v1/connect/oauth2/authorize
Redirect user for authorization (Authorization Code flow).

| Parameter             | Description                                     |
|-----------------------|-------------------------------------------------|
| `client_id`           | Your application's client ID                    |
| `redirect_uri`        | Registered redirect URI                         |
| `response_type`       | Always `code`                                   |
| `scope`               | Space-separated scopes being requested          |
| `state`               | Optional CSRF protection value                  |
| `code_challenge`      | Optional PKCE challenge                         |
| `code_challenge_method` | Optional: `S256`                              |
