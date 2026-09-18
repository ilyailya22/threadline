# Security

The assignment names two threats explicitly — **XSS** and **SQL injection**. Both are addressed
structurally rather than by filtering, and both are covered by tests. The rest of this document
covers the other ways a public, anonymous comment form gets abused.

---

## XSS

A comment board is a stored-XSS target by definition: anything one visitor submits is rendered to
every other visitor. The defence has three independent layers, so a bug in one is contained by the
next.

### 1. Server-side allowlist sanitiser — the primary defence

[`CommentTextSanitizer`](../src/Comments.Application/Comments/Sanitization/CommentTextSanitizer.cs)

- The input is tokenised into tags and text runs.
- A tag survives only if it is one of `<a href title>`, `<code>`, `<i>`, `<strong>` **and** is
  spelled the way XHTML requires. Anything else — `<script>`, `<img onerror>`, `<svg onload>`, a
  stray `<` — is entity-escaped and shows up on the page as literal text.
- Attributes are **rebuilt**, not copied. `<a>` keeps only `href` and `title`; any other attribute,
  including an unquoted `onclick=alert(1)` that a naive regex would skip over, demotes the whole tag
  to text.
- `href` is HTML-decoded *before* validation, so `jav&#x09;ascript:` is seen as the scheme it
  really is, then checked against an allowlist: `http`, `https`, `mailto`. Everything else is text.
- Every surviving link gets `rel="nofollow noopener noreferrer"`.
- Tags must balance. `<strong>` without `</strong>`, or crossed tags, are **rejected with a field
  error** rather than silently repaired — the assignment's "проверка на закрытие тегов".
- The output is then **parsed as XML** (DTD processing off, no entity resolution — so the check
  cannot itself be an XXE or billion-laughs vector). That is not how correctness is achieved; it is
  how it is verified. If a future change ever made the sanitiser emit malformed markup, the comment
  is refused rather than stored.

**Why an allowlist and not a blocklist.** Blocklists lose. There are too many ways to spell an
attack. Here nothing survives unless it is on a four-item list, so a payload nobody has seen yet
still comes out as text.

**Why not HtmlSanitizer.** It solves a different problem — keeping as much of arbitrary HTML as is
safe — and does not do the one thing the assignment asks for: refuse unbalanced markup. See
[ADR 0004](adr/0004-custom-sanitizer.md).

### 2. Angular's sanitiser — the second, independent check

The comment HTML is bound with `[innerHTML]` through
[`SanitizedHtmlPipe`](../src/Comments.Web/src/app/shared/sanitized-html.pipe.ts), which runs
Angular's own sanitiser rather than `bypassSecurityTrustHtml`. In normal operation it changes
nothing, because the server's allowlist is far stricter. Its value is the day the server has a bug.

Uploaded `.txt` files are fetched and shown with text interpolation, never as HTML.

### 3. Response headers

- The API sets a Content-Security-Policy with no `unsafe-inline` for scripts.
- `X-Content-Type-Options: nosniff` everywhere, so a browser never reinterprets a response as a
  different type.
- `.txt` attachments are served with `Content-Disposition: attachment` — downloaded, never rendered
  in this origin.

### Tests

[`CommentTextSanitizerTests`](../tests/Comments.UnitTests/Comments/Sanitization/CommentTextSanitizerTests.cs)
runs a corpus of real payloads — script tags in both cases, event handlers, `javascript:` /
`vbscript:` / `data:` URLs, entity-encoded schemes, the nested `<scr<script>ipt>` bypass, SVG,
MathML — and asserts on the **parsed tree**, not on substrings. A substring check is the wrong tool
here: the escaped text `&lt;img onerror=…&gt;` legitimately *contains* "onerror" while being
harmless, so substring tests fail on correct output and pass on dangerous output that happens to be
spelled differently.

The integration tests repeat the key cases end to end over HTTP.

---

## SQL injection

**There is no string-concatenated SQL in the solution.**

- Every query goes through EF Core, which parameterises values.
- The one piece of raw SQL — the outbox claim query, which needs `UPDLOCK, READPAST` hints — uses
  `FromSql` with an *interpolated* string. EF turns each `{value}` into a `@p0` parameter; it never
  becomes part of the SQL text.
- **Sorting is an enum.** The `ORDER BY` is chosen by a `switch` over `CommentSortField`, so the
  set of possible clauses is closed and known at compile time. A request like
  `?sortBy=userName;DROP TABLE Users--` fails model binding with a 400 and never reaches a query.
- The integration tests post the classic payloads (`'; DROP TABLE Comments; --`, `' OR '1'='1`,
  `UNION SELECT`) and assert they are stored as text and that the tables are still there.

---

## CAPTCHA

- Rendered server-side as a distorted PNG (per-glyph rotation, skew, size and colour, noise curves,
  speckles), every parameter from a cryptographic RNG.
- The answer never leaves the server. The browser gets an image and an opaque id.
- Stored in Redis with a 10-minute TTL, so it works across any number of API replicas.
- **One-shot.** Validation uses `GETDEL`, which reads and deletes atomically. A wrong answer burns
  the challenge too, so one image cannot be brute-forced, and a captured (id, answer) pair cannot be
  replayed.
- Confusable characters (0/O, 1/I/l, 5/S, 2/Z) are left out of the alphabet: rejecting a human who
  read the image correctly is a worse failure than a slightly smaller keyspace.
- Checked **first** in the write path, before anything touches the database, so a bot flood stays
  off the connection pool.

The load-test bypass is a decorator that exists only when `LoadTest:Enabled` is true, needs its own
secret, and is **not registered at all in the Production environment**.

---

## File uploads

| Risk | Mitigation |
|---|---|
| HTML renamed to `.png` | Type is decided by **magic bytes**, and the extension must agree with them |
| Polyglot file (valid PNG *and* valid HTML) | Images are decoded and re-encoded, which discards everything but the pixels |
| EXIF / location data | Dropped by re-encoding |
| Decompression bomb | Pixel dimensions checked from the header **before** decoding; 64 MP ceiling |
| Path traversal (`../../web.config`) | Directory components stripped in the domain; storage paths built only from server-generated ids |
| Oversized upload | 10 MB request limit, 100 KB for text files, enforced at three layers |
| Serving user content | Blobs are private and streamed through the API with `nosniff`; `.txt` is always a download |

---

## Abuse and rate limiting

Per-client limits, partitioned by the client-id cookie when present and by IP otherwise — so
several people behind one NAT are not throttled as one, and clearing cookies does not reset the
budget for free.

| Policy | Limit | Endpoints |
|---|---|---|
| `write` | 10 / minute | posting a comment |
| `preview` | 60 / minute | preview (fired while typing) |
| `captcha` | 30 / minute | issuing challenges |
| `read` | 300 / minute | list, thread, attachments |

Exceeding a limit returns `429` with `Retry-After`.

GraphQL has a depth limit (12) and cost limits, because a recursive `replies { replies { … } }`
query is otherwise a denial-of-service request the schema invites.

---

## Privacy

The assignment asks to store "data that helps identify the client". That is done without storing
more than necessary:

- **IP addresses are stored as HMAC-SHA256 with a secret pepper**, never in clear. A plain hash of
  an IPv4 address is not anonymous — the whole space is four billion values and brute-forces in
  seconds. A keyed hash still lets posts be grouped by origin for abuse investigation, and is
  useless to anyone who only has the database.
- A first-party `cid` cookie (opaque UUID, `HttpOnly`, `SameSite=Lax`, `Secure` outside localhost)
  identifies a browser across visits without cross-site tracking.
- The User-Agent is kept, truncated to 512 characters.

---

## Secrets

- No secret is committed. Local defaults in `docker-compose.yml` are for a throwaway local stack.
- In Azure, the pepper lives in Key Vault and is referenced by the container app through a managed
  identity; the registry is pulled with RBAC (no admin user); blobs are accessed with the managed
  identity (no storage key in configuration).
- CI/CD authenticates to Azure with OIDC federated credentials — there is no client secret in the
  repository or in GitHub.
- NuGet auditing is on and warnings are errors, so a dependency with a newly published CVE fails
  the build. That is how `Microsoft.OpenApi` was pinned to a patched release during development.

---

## Licensing hygiene

Two dependencies moved to commercial licences during the life of this project and were replaced or
pinned rather than used unlicensed:

- **SixLabors ImageSharp 3.x / ImageSharp.Drawing** → replaced by **SkiaSharp** (MIT).
- **MassTransit 9** → pinned to **8.5.3**, the last Apache-2.0 line. MassTransit 9 refuses to start
  the bus without a licence key.
- **MediatR 13** → pinned to **12.5.0**, the last Apache-2.0 release.
