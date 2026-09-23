# API reference

Base URL: `http://localhost:8080` through nginx, or `http://localhost:5080` directly. In the
Development environment the interactive reference is at `/scalar/v1` and the GraphQL IDE at
`/graphql`.

Errors are [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) ProblemDetails. Validation errors carry
per-field messages keyed in camelCase, matching the form's control names:

```json
{
  "status": 400,
  "title": "One or more validation errors occurred",
  "errors": { "userName": ["User Name may contain only latin letters and digits."] },
  "traceId": "00-…"
}
```

---

## REST

### `GET /api/comments` — top-level comments

| Query | Default | Values |
|---|---|---|
| `page` | `1` | ≥ 1; offset capped at 10,000 items |
| `pageSize` | `25` | 1–100 |
| `sortBy` | `createdAt` | `createdAt`, `userName`, `email` |
| `direction` | `descending` | `ascending`, `descending` |
| `search` | — | full-text over text, user name, e-mail (not cached) |

```json
{
  "items": [{
    "id": "01a0b399-ada1-753d-9d87-0919d687ef33",
    "author": { "id": "…", "userName": "Rum8", "email": "rum@example.com" },
    "textHtml": "Thread with <i>picture</i>",
    "textPreview": "Thread with picture",
    "createdAt": "2026-09-18T08:19:48.2577225+00:00",
    "replyCount": 1,
    "lastReplyAt": "2026-09-18T08:19:48.9522917+00:00",
    "attachments": [{
      "id": "…", "kind": "Image", "contentType": "image/png",
      "originalFileName": "big.png", "sizeBytes": 1878,
      "url": "/api/attachments/…/content", "thumbnailUrl": "/api/attachments/…/thumbnail",
      "width": 320, "height": 180
    }]
  }],
  "page": 1, "pageSize": 25, "totalCount": 1, "totalPages": 1,
  "hasPrevious": false, "hasNext": false
}
```

Served from Elasticsearch through a two-level cache; falls back to SQL if search is unavailable.

### `GET /api/comments/{rootId}/thread`

One page of a thread, as a flat list in depth-first order. Threads are unbounded — the seeded
dataset has one with ~14,000 replies — so the endpoint pages on the materialised path.

| Query | Default | Values |
|---|---|---|
| `limit` | `100` | 1–500 |
| `after` | — | the previous page's `nextCursor` — opaque: pass it back unchanged |
| `maxDepth` | `64` | 1–64 |

```json
{
  "rootId": "…", "totalCount": 13859, "hasMore": true,
  "nextCursor": "019ee433dcaf7cec019ee5a2….019ee5a2…",
  "nodes": [
    { "id": "…", "parentId": null, "rootId": "…", "depth": 1, "author": {…}, "textHtml": "…", "createdAt": "…", "attachments": [] },
    { "id": "…", "parentId": "…", "rootId": "…", "depth": 2, … }
  ]
}
```

Every node appears after its parent, so pages can be appended and the nesting rebuilt from
`parentId`. `404` if the root does not exist; `400` for a malformed cursor.

### `POST /api/comments` — post a comment or reply

`multipart/form-data`:

| Field | Required | Rule |
|---|---|---|
| `userName` | yes | `^[A-Za-z0-9]{2,64}$` |
| `email` | yes | e-mail, ≤ 254 |
| `homePage` | no | absolute `http`/`https` URL |
| `text` | yes | ≤ 20,000; only `<a href title>`, `<code>`, `<i>`, `<strong>`; tags must balance |
| `parentId` | no | the comment being replied to |
| `captchaId` | yes | from `X-Captcha-Id` |
| `captchaAnswer` | yes | letters and digits |
| `file` | no | JPG/GIF/PNG ≤ 10 MB (stored at ≤ 320×240) or TXT ≤ 100 KB |

`201 Created` with `{ id, rootId, parentId, createdAt, textHtml }`. `400` validation, `404` unknown
parent, `429` rate-limited.

```bash
ID=$(curl -s -D - -o captcha.png http://localhost:8080/api/captcha | grep -i x-captcha-id | cut -d' ' -f2 | tr -d '\r')
# open captcha.png, read the characters
curl -X POST http://localhost:8080/api/comments \
  -F userName=Anonym -F email=anon@example.com \
  -F "text=Hello <strong>world</strong>" \
  -F captchaId=$ID -F captchaAnswer=ABCDE \
  -F "file=@photo.jpg"
```

### `POST /api/comments/preview`

`{ "text": "…" }` → `{ "textHtml": "…", "textPlain": "…" }`. Same sanitiser as posting, so the
preview is exactly what would be stored.

### `GET /api/captcha`

`image/png` body; challenge id in `X-Captcha-Id`, expiry in `X-Captcha-Expires-At`. Never cached.
One-shot: a challenge is consumed by its first validation attempt, right or wrong.

### `GET /api/attachments/{id}/content` · `GET /api/attachments/{id}/thumbnail`

Streams a stored file. Images as PNG (thumbnail as WebP); text files as a download. Immutable,
cacheable for a year.

### `GET /api/validation-rules`

The server's validation rules — patterns, lengths, allowed tags, file limits, page size. The Angular
form builds its validators from this so client and server cannot disagree.

### Health

| Endpoint | Meaning |
|---|---|
| `GET /health/live` | the process is running; no dependency checks |
| `GET /health/ready` | SQL Server, Redis and the message bus are reachable |

Elasticsearch is deliberately *not* part of readiness: the list degrades to SQL without it, so a
search outage should not take instances out of the load balancer.

---

## GraphQL

`POST /graphql`. Depth-limited to 12 with cost analysis enabled.

```graphql
query Page {
  comments(page: 1, pageSize: 25, sortBy: CREATED_AT, direction: DESCENDING, search: null) {
    totalCount
    items { id textHtml replyCount author { userName email } }
  }
}

query Thread($id: UUID!) {
  # A comment and as many levels of replies as the client will render.
  comment(id: $id) {
    textHtml
    author { userName }
    replies {
      textHtml
      replies { textHtml }
    }
  }
}
```

`replies` is resolved through a DataLoader: every comment on one level is fetched in a single
query, so a query five levels deep costs five queries, not one per node.

---

## Accounts

| Method | Path | What it does |
|---|---|---|
| `GET` | `/api/auth/me` | The signed-in account, or 204 |
| `GET` | `/api/auth/providers` | Which ways in exist (`{ "google": true }`) |
| `POST` | `/api/auth/register` | Address and password; signs in and sends the confirmation link |
| `POST` | `/api/auth/login` | Address and password; 401 says only that one of them was wrong |
| `POST` | `/api/auth/logout` | Drops the session cookie |
| `POST` | `/api/auth/confirm` | `{ id, token }` from a confirmation link |
| `POST` | `/api/auth/confirm/resend` | Another link for the signed-in account |
| `GET` | `/api/auth/google` | Starts the Google round trip; comes back signed in |
| `PUT` | `/api/accounts/me` | Nickname and home page |
| `POST` | `/api/accounts/me/avatar` | `multipart/form-data`, one image |
| `DELETE` | `/api/accounts/me/avatar` | Back to Google's picture, or initials |
| `GET` | `/api/accounts/{id}/avatar` | The stored avatar; public, it is drawn on every comment |

Posting a comment reads the author from the session cookie when there is one. In that case
`userName`, `email` and the CAPTCHA fields are neither required nor read.

---

## Real-time (SignalR)

Hub: `/hubs/comments`. Every connection is in the top-level group automatically.

| Direction | Name | Payload |
|---|---|---|
| server → client | `commentCreated` | a comment node; top-level to everyone, replies only to watchers of that thread |
| client → server | `WatchThread(rootId)` | subscribe to one thread's replies |
| client → server | `UnwatchThread(rootId)` | unsubscribe |

---

## Rate limits

| Policy | Default / minute | Config key |
|---|---|---|
| read | 300 | `RateLimiting:ReadPerMinute` |
| preview | 60 | `RateLimiting:PreviewPerMinute` |
| captcha | 30 | `RateLimiting:CaptchaPerMinute` |
| write | 10 | `RateLimiting:WritePerMinute` |

Partitioned by the `cid` cookie, or by IP when there is none. `429` carries `Retry-After`.
