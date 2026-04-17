# Outlook API — Graph API Reference

Base URL: `https://graph.microsoft.com/v1.0`
Auth: `Authorization: Bearer <access_token>`

---

## Mail

### List Messages
```
GET /me/messages
GET /me/mailFolders/{folder}/messages
```
Key query params:
- `$select=id,subject,from,receivedDateTime,isRead,hasAttachments,bodyPreview`
- `$top=10` — max results
- `$orderby=receivedDateTime desc`
- `$filter=isRead eq false` — unread only
- `$filter=from/emailAddress/address eq 'user@example.com'`
- `$filter=contains(subject,'keyword')`
- `$search="keyword"` — full-text search (cannot combine with $filter)

Well-known folder names: `inbox`, `drafts`, `sentItems`, `deleteditems`, `junkEmail`, `outbox`

### Get Message
```
GET /me/messages/{id}
```
Add `body` to `$select` for full body content. Body has `contentType` (html/text) and `content`.

### Send Mail
```
POST /me/sendMail
Body: { "message": { "subject": "...", "body": { "contentType": "text|html", "content": "..." },
        "toRecipients": [{"emailAddress": {"address": "..."}}],
        "ccRecipients": [...], "bccRecipients": [...] },
       "saveToSentItems": true }
Response: 202 Accepted
```

### Reply
```
POST /me/messages/{id}/reply
Body: { "comment": "reply text" }
Response: 202 Accepted
```

### Reply All
```
POST /me/messages/{id}/replyAll
Body: { "comment": "reply text" }
```

### Forward
```
POST /me/messages/{id}/forward
Body: { "toRecipients": [{"emailAddress": {"address": "..."}}], "comment": "..." }
```

### Delete
```
DELETE /me/messages/{id}
Response: 204 No Content
```
Moves to Deleted Items. To permanently delete, delete from Deleted Items folder.

### Move
```
POST /me/messages/{id}/move
Body: { "destinationId": "<folderId or well-known-name>" }
```

### Update Message (patch)
```
PATCH /me/messages/{id}
Body: { "isRead": true } | { "flag": { "flagStatus": "flagged|notFlagged|complete" } }
```

### Create Draft
```
POST /me/messages
Body: { "subject": "...", "body": {...}, "toRecipients": [...] }
Response: 201 Created with message object
```

### Send Draft
```
POST /me/messages/{id}/send
Response: 202 Accepted
```

### Attachments
```
GET  /me/messages/{id}/attachments
GET  /me/messages/{id}/attachments/{attId}
```
File attachments have `contentBytes` (base64). Reference attachments (OneDrive) have `webUrl`.

---

## Calendar

### List Events (calendar view — respects recurrence)
```
GET /me/calendarView?startDateTime=<iso>&endDateTime=<iso>
Headers: Prefer: outlook.timezone="UTC"
```
Key select: `id,subject,start,end,location,organizer,isAllDay,isCancelled,responseStatus,attendees,bodyPreview`

### Get Event
```
GET /me/events/{id}
```

### Create Event
```
POST /me/events
Body: {
  "subject": "...",
  "start": { "dateTime": "2026-04-15T10:00:00", "timeZone": "Eastern Standard Time" },
  "end":   { "dateTime": "2026-04-15T11:00:00", "timeZone": "Eastern Standard Time" },
  "location": { "displayName": "..." },
  "body": { "contentType": "text", "content": "..." },
  "attendees": [{ "emailAddress": {"address": "..."}, "type": "required" }],
  "isAllDay": false
}
```

### Update Event
```
PATCH /me/events/{id}
```

### Delete Event
```
DELETE /me/events/{id}
Response: 204 No Content
```

### Respond to Event
```
POST /me/events/{id}/accept
POST /me/events/{id}/tentativelyAccept
POST /me/events/{id}/decline
Body: { "sendResponse": true, "comment": "..." }
Response: 202 Accepted
```

### Time Zone Names
Use Windows timezone IDs or IANA names. Common ones:
- `Eastern Standard Time` / `America/New_York`
- `Central Standard Time` / `America/Chicago`
- `Mountain Standard Time` / `America/Denver`
- `Pacific Standard Time` / `America/Los_Angeles`
- `UTC`

---

## Contacts

### List
```
GET /me/contacts?$orderby=displayName&$select=id,displayName,emailAddresses,mobilePhone,businessPhones,companyName
```

### Search
```
GET /me/contacts?$search="query"&$select=...
```

### Get
```
GET /me/contacts/{id}
```
Full fields: `givenName,surname,displayName,emailAddresses,mobilePhone,businessPhones,homePhones,companyName,jobTitle,birthday,personalNotes`

### Create
```
POST /me/contacts
Body: {
  "givenName": "...",
  "surname": "...",
  "emailAddresses": [{ "address": "...", "name": "..." }],
  "mobilePhone": "...",
  "businessPhones": ["..."],
  "companyName": "...",
  "jobTitle": "..."
}
```

### Delete
```
DELETE /me/contacts/{id}
```

---

## Mail Folders

### List
```
GET /me/mailFolders?$select=id,displayName,totalItemCount,unreadItemCount
GET /me/mailFolders/{id}/childFolders
```

### Create
```
POST /me/mailFolders
POST /me/mailFolders/{parentId}/childFolders
Body: { "displayName": "..." }
```

### Update (rename)
```
PATCH /me/mailFolders/{id}
Body: { "displayName": "..." }
```

### Delete
```
DELETE /me/mailFolders/{id}
```
Permanently deletes the folder and all messages inside it.

---

## OData Filters Quick Reference

| Goal | Filter |
|------|--------|
| Unread only | `isRead eq false` |
| From address | `from/emailAddress/address eq 'user@example.com'` |
| Subject contains | `contains(subject,'keyword')` |
| Has attachments | `hasAttachments eq true` |
| After date | `receivedDateTime ge 2026-01-01T00:00:00Z` |
| Flagged | `flag/flagStatus eq 'flagged'` |
| Combine | `isRead eq false and hasAttachments eq true` |

**Note**: `$search` and `$filter` cannot be combined in the same request. Use one or the other.

### Search Tips

- `$search` matches across subject, body, sender name, and sender address simultaneously
- Sender display names may not match the institution/company name — if a broad institution search returns noise, search on topic words instead (e.g., `"speak"`, `"invitation"`, `"meeting"`)
- Phrase search (quoted) is more precise: `$search="invite speak"` vs `$search=speak`
- When the first search misses, try 2-3 different term combinations in parallel rather than giving up
- To search sent mail, use `/me/mailFolders/sentItems/messages?$search=...`

---

## Rate Limits

| Resource | Limit |
|----------|-------|
| Per app per mailbox | 10,000 requests / 10 min |
| Attachments (download) | 25 MB per attachment |
| Message body size | 4 MB |
| Batch requests | Up to 20 requests per batch |

For large operations, use `$top` to paginate and `$skipToken` / `@odata.nextLink` for continuation.

---

## Pagination

Large result sets return `@odata.nextLink` in the response. Follow it to get the next page:
```
GET <value of @odata.nextLink>
```
The scripts currently use `$top` to limit results. To add pagination support, loop on `@odata.nextLink` until it's absent.

---

## Common Error Codes

| Code | Meaning |
|------|---------|
| 401 | Token expired or invalid — run `auth refresh` or `auth login` |
| 403 | Missing permission scope — check Azure app permissions |
| 404 | Resource (message/event/contact) not found |
| 429 | Rate limited — back off and retry |
| 503 | Graph API temporarily unavailable |
