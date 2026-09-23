# Requirements checklist

Every requirement from the assignment, where it is implemented, and how to check it.

Written for the QA reviewer: each row has a verification step that can be performed against a
running instance without reading any code.

**Setup:** `docker compose up -d --build`, then open <http://localhost:8080>.

---

## Base level

### The form

| # | Requirement | Where | How to verify |
|---|---|---|---|
| 1 | **User Name** — latin letters and digits, required | [`UserName.cs`](../src/Comments.Domain/Users/UserName.cs) | Type `Иван` or `John Smith` → rejected. Type `John42` → accepted. Leave empty → "Обязательное поле." |
| 2 | **E-mail** — e-mail format, required | [`EmailAddress.cs`](../src/Comments.Domain/Users/EmailAddress.cs) | Type `not-an-email` → rejected. Leave empty → rejected. |
| 3 | **Home page** — URL format, optional | [`HomePageUrl.cs`](../src/Comments.Domain/Users/HomePageUrl.cs) | Leave empty → accepted. Type `not a url` → rejected. Type `javascript:alert(1)` → rejected. |
| 4 | **CAPTCHA** — image, letters and digits, required | [`SkiaCaptchaRenderer.cs`](../src/Comments.Infrastructure/Captcha/SkiaCaptchaRenderer.cs) | An image is shown. Type the wrong answer → refused, and a **new** image appears (it is one-shot). |
| 5 | **Text** — required, no HTML except the allowed tags | [`CommentTextSanitizer.cs`](../src/Comments.Application/Comments/Sanitization/CommentTextSanitizer.cs) | Post `<script>alert(1)</script>` → it appears as literal text, no alert box. |

### The main page

| # | Requirement | Where | How to verify |
|---|---|---|---|
| 6 | Any number of replies to any comment, cascading | [`CommentPath.cs`](../src/Comments.Domain/Comments/CommentPath.cs), [`comment-node.html`](../src/Comments.Web/src/app/features/comments/comment-node/comment-node.html) | Post a comment, reply to it, reply to the reply, and again. All four render nested. |
| 7 | Top-level comments in a **table**, sortable by User Name, E-mail and date, **both directions** | [`comments-page.html`](../src/Comments.Web/src/app/features/comments/comments-page/comments-page.html) | Click each of the three headers. First click sorts descending, second ascending; the arrow and the URL both change. |
| 8 | **25 messages per page** | [`Paging.DefaultPageSize`](../src/Comments.Application/Common/Models/Paging.cs) | With more than 25 threads, the pager appears and each page holds exactly 25 rows. |
| 9 | Protection against **XSS** and **SQL injection** | [SECURITY.md](SECURITY.md) | See the XSS and SQLi sections below. |
| 10 | Default sort is **LIFO** | [`SortDirection.cs`](../src/Comments.Application/Common/Models/SortDirection.cs) | Load the page with no query string: newest comment is first. |
| 11 | A simple CSS design | [`styles.scss`](../src/Comments.Web/src/styles.scss) | The page is styled, responsive, and follows the system light/dark preference. |

### Files

| # | Requirement | Where | How to verify |
|---|---|---|---|
| 12 | An image **or** a text file can be attached | [`AttachmentIntakeService.cs`](../src/Comments.Application/Attachments/AttachmentIntakeService.cs) | Attach a `.png` and post; attach a `.txt` and post. Both appear under the comment. |
| 13 | Images larger than **320×240** are scaled down **proportionally**, on upload; JPG, GIF, PNG only | [`SkiaImageProcessor.cs`](../src/Comments.Infrastructure/Media/SkiaImageProcessor.cs) | Attach a 1920×1080 photo. The form warns it will be reduced; the response to the post already carries a 320×180 image — scaled, not cropped or squashed, and nothing waits for a worker. A photo carrying an EXIF orientation comes back the right way up. Attach a `.bmp` → rejected. |
| 14 | Text files at most **100 KB**, `.txt` only | [`Attachment.cs`](../src/Comments.Domain/Comments/Attachment.cs) | Attach a 200 KB `.txt` → rejected with the limit named. |
| 15 | File viewing has visual effects | [`lightbox.scss`](../src/Comments.Web/src/app/features/lightbox/lightbox.scss) | Click a thumbnail: the overlay fades in and the panel zooms. Escape and a click outside both close it. |

### Regular expressions

| # | Requirement | Where | How to verify |
|---|---|---|---|
| 16 | Only `<a href="" title="">`, `<code>`, `<i>`, `<strong>` allowed | [`CommentTextSanitizer.cs`](../src/Comments.Application/Comments/Sanitization/CommentTextSanitizer.cs) | Post `<strong>bold</strong> <div>block</div>` → "bold" is bold, `<div>block</div>` is literal text. |
| 17 | Tags must be closed; the result must be **valid XHTML** | same file | Post `<strong>never closed` → refused with "Тег &lt;strong&gt; не закрыт". Post `<i><strong>x</i></strong>` → refused as crossed. |

### JavaScript and AJAX

| # | Requirement | Where | How to verify |
|---|---|---|---|
| 18 | Validation on **both** client and server | [`comment-form.ts`](../src/Comments.Web/src/app/features/comments/comment-form/comment-form.ts), [`CreateCommentCommandValidator.cs`](../src/Comments.Application/Comments/Commands/CreateComment/CreateCommentCommandValidator.cs) | Client: errors appear as you type, with no request. Server: `curl -X POST http://localhost:5080/api/comments -F "userName=не латиница" …` → 400 with per-field errors. |
| 19 | **Preview without a page reload** | [`PreviewCommentQuery.cs`](../src/Comments.Application/Comments/Queries/PreviewComment/PreviewCommentQuery.cs) | Type text with tags, press "Предпросмотр": the rendered result appears, the page does not reload. |
| 20 | A toolbar with `[i]`, `[strong]`, `[code]`, `[a]` | [`comment-form.html`](../src/Comments.Web/src/app/features/comments/comment-form/comment-form.html) | Select a word, click `[strong]`: the selection is wrapped, not replaced. |
| 21 | Visual effects | throughout | New comments fade in; the live banner and the preview slide in; `prefers-reduced-motion` disables all of it. |

### Tooling

| # | Requirement | Evidence |
|---|---|---|
| 22 | .NET, latest version | .NET 10 — [`Directory.Build.props`](../Directory.Build.props) |
| 23 | Entity Framework | EF Core 10 — [`AppDbContext.cs`](../src/Comments.Infrastructure/Persistence/AppDbContext.cs) and the migration |
| 24 | Frontend framework | Angular 22 — [`src/Comments.Web`](../src/Comments.Web) |
| 25 | Git | Feature branches merged with `--no-ff`; Conventional Commits. `git log --graph --oneline` |
| 26 | Docker | Three Dockerfiles + [`docker-compose.yml`](../docker-compose.yml) |
| 27 | Relational database | MS SQL Server 2022 |
| 28 | OOP | Encapsulated aggregates with private setters, value objects, domain events, polymorphic consumers — [`Comment.cs`](../src/Comments.Domain/Comments/Comment.cs) |

### Deliverables

| # | Requirement | Where |
|---|---|---|
| 29 | Deployed instance | [DEPLOYMENT.md](DEPLOYMENT.md) — Bicep + GitHub Actions; see the note at the bottom of this file |
| 30 | Docker packaging | `docker compose up -d --build` brings up the entire environment |
| 31 | Git repository | Branch history shows the feature-by-feature progression |
| 32 | README | [README.md](../README.md) |
| 33 | **Schema file for MySQL Workbench** | [`db/schema.mysql.sql`](../db/schema.mysql.sql) — File → Import → Reverse Engineer MySQL Create Script |
| 34 | Video | Sent separately |

---

## Junior+ level

| Requirement | Implementation | How to verify |
|---|---|---|
| **Queue** | RabbitMQ + the transactional outbox | <http://localhost:15672> (`guest`/`guest`) → Queues. Post a comment and watch the message flow. |
| **Cache** | Redis as the L2 of `HybridCache` | `docker compose exec redis redis-cli KEYS '*'` after loading the list. |
| **Events** | Domain events → outbox → integration events → consumers | `docker compose exec sqlserver … SELECT TOP 10 * FROM OutboxMessages` |
| **WebSocket** | SignalR with a Redis backplane | Open the site in two browsers. Post in one; the other shows "Новых комментариев: 1" without a refresh. |

---

## Middle level

| Requirement | Implementation | How to verify |
|---|---|---|
| **Graph** | GraphQL (HotChocolate) with DataLoader | Open <http://localhost:5080/graphql> and run the query in [API.md](API.md#graphql). |
| **Message broker** | RabbitMQ via MassTransit | <http://localhost:15672> → Exchanges shows the published contracts. |
| **NoSQL** | Elasticsearch as the read model | `curl http://localhost:9200/comments/_count` |
| **Cloud** | Azure — Container Apps, SQL, Blob, Key Vault, Monitor | [`deploy/bicep`](../deploy/bicep), [DEPLOYMENT.md](DEPLOYMENT.md) |

---

## Middle+ level

> *"У нас 1 000 000 сообщений, 100к пользователей в 24 час. Заложи в архитектуру решение, напиши
> нагрузочный тест."*

| Requirement | Implementation | Where |
|---|---|---|
| Architecture designed for the volume | Materialised path, filtered covering indexes, outbox, Elasticsearch read model, two-level cache, async attachment processing, queue-depth autoscaling | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Generate the data | 1M comments / 100k users via `SqlBulkCopy` with a realistic thread shape | [`tools/Comments.Seeder`](../tools/Comments.Seeder) |
| Load test | k6 (browse / write / spike) and NBomber, both asserting SLOs | [`loadtests/k6`](../loadtests/k6), [`tests/Comments.LoadTests`](../tests/Comments.LoadTests) |
| Results | Scenarios, thresholds, measurements, and where the ceiling is | [LOAD-TESTING.md](LOAD-TESTING.md) |

```bash
dotnet run --project tools/Comments.Seeder -- --comments 1000000 --users 100000 --truncate
k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/browse.js
```

---

## Security checks worth running

**XSS.** Post each of these and confirm no dialog appears and nothing executes — each should show
as literal text:

```html
<script>alert('xss')</script>
<img src=x onerror=alert(1)>
<svg/onload=alert(1)>
<a href="javascript:alert(1)">click</a>
<a href="jav&#x09;ascript:alert(1)">click</a>
<scr<script>ipt>alert(1)</scr</script>ipt>
```

The same corpus is asserted in
[`CommentTextSanitizerTests`](../tests/Comments.UnitTests/Comments/Sanitization/CommentTextSanitizerTests.cs),
against the *parsed DOM* rather than by string matching — substring checks pass on escaped text that
is already harmless and would miss the cases that are not.

**SQL injection.** Try these in any field and in the query string:

```
' OR '1'='1
'; DROP TABLE Comments; --
1' UNION SELECT NULL,NULL--
?sortBy=userName;DROP TABLE Users--
```

They are stored as literal text or rejected as an invalid enum. There is no string-concatenated SQL
anywhere in the solution: EF Core parameterises everything, and the only raw SQL — the outbox claim
query — uses `FromSql` with interpolated parameters, which EF turns into `@p0`, not into text.
Sorting is an **enum**, so a column name can never come from a request.

**Upload.** Rename an HTML file to `photo.png` and attach it → rejected, because the magic bytes say
text and the extension says image.

**Rate limiting.** `for i in $(seq 1 20); do curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5080/api/comments; done`
→ 429 after ten attempts within the window.

---

## The deployed instance

<https://threadline-dev-web.delightfulpebble-27670933.canadacentral.azurecontainerapps.io>

Region canadacentral, resource group `rg-threadline-dev`, deployed from the Bicep in
[`deploy/bicep`](../deploy/bicep) — see [DEPLOYMENT.md](DEPLOYMENT.md) for the commands, the cost
and a teardown that leaves nothing billable behind.

Two things only a real deployment could surface, both now fixed in the template:

* **RabbitMQ would not start.** Its Erlang cookie lived on an Azure Files share, and SMB cannot
  express mode 400 or file ownership. The broker is ephemeral now — SQL plus the outbox is the
  source of truth, MassTransit rebuilds the topology on boot, and the consumers are idempotent.
* **nginx could not reach the API.** Container Apps routes internal traffic by the Host header and
  terminates TLS on the internal ingress, so the upstream has to be the API's internal FQDN over
  https, with SNI, HTTP/1.1 and `Host` set to that name rather than the browser's.

Getting there also needed two subscription-level workarounds that are not code: a new Free Trial is
refused in most European regions (`RequestDisallowedByAzure`), and `az acr build` is disabled on it,
so the images are built locally and pushed.

`docker compose up -d --build` is the always-working path and is what the demo video records.
