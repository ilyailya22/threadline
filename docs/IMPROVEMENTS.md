# Improvements

Everything the assignment asks for is implemented; this file is what comes **after** it — kept
separate on purpose, so the line between "the task" and "extra" is visible.

Items are grouped by why they matter and ordered by value within each group. Each says what it is,
why it is not done yet, and roughly what it costs.

Legend: **S** ≈ hours · **M** ≈ a day or two · **L** ≈ a week+

---

## A. Already done beyond the brief

Work that goes past the assignment's requirements and is already in the code. Listed so a reviewer
knows it is intentional rather than scope creep.

| # | Improvement | Where |
|---|---|---|
| A1 | Transactional outbox + idempotent inbox — no lost or duplicated side effects | `Persistence/Outbox`, `Messaging/Consumers` |
| A2 | Graceful degradation: list falls back to SQL when Elasticsearch is down | `GetTopLevelCommentsQueryHandler` |
| A3 | Validation rules published by the server and consumed by the SPA — one definition | `/api/validation-rules` |
| A4 | Two independent XSS layers (server allowlist + Angular sanitiser) and a CSP | `docs/SECURITY.md` |
| A5 | IP addresses stored as keyed HMAC, not in clear | `IpAddressHasher` |
| A6 | Search-index rebuild from SQL (`--reindex`) | `tools/Comments.Seeder` |
| A7 | Optimistic insert of the author's own comment; live updates behind a banner | `comments-page.ts` |
| A8 | Magic-byte upload validation and image re-encoding (strips polyglots, EXIF) | `FileTypeSniffer`, `SkiaImageProcessor` |
| A9 | Queue-depth autoscaling for the worker in Azure | `workloads.bicep` |
| A10 | OIDC deployment with a post-deploy health gate | `.github/workflows/deploy.yml` |
| A11 | Licence hygiene: ImageSharp, MassTransit 9 and MediatR 13 replaced or pinned | `Directory.Packages.props` |
| A12 | Single-process mode for small deployments and tests | `Messaging:HostWorkerConsumers` |

---

## B. Production hardening — next

What a real launch would need first.

| # | Improvement | Why | Size |
|---|---|---|---|
| B1 | **Run the Azure deployment and record the URL** | The pipeline and templates are ready; only the subscription was missing | S |
| B2 | **Private networking**: VNet-integrated Container Apps environment, private endpoints for SQL, Storage and Key Vault, public access off | Today SQL allows Azure services and storage is public-network-reachable (though private-access) | M |
| B3 | **Managed Redis / RabbitMQ / Elasticsearch** (Azure Cache for Redis, Azure Service Bus or CloudAMQP, Elastic Cloud) | Self-hosted single replicas have no HA; swapping is a connection string | M |
| B4 | **Migrations as a pipeline step**, not at start-up | Two replicas starting together can race; a DBA may want to review the script | S |
| B5 | **CDN in front of attachments** (Azure Front Door) and SAS URLs instead of streaming through the API | Attachment bandwidth currently goes through API replicas | M |
| B6 | **Dead-letter handling**: alert on `_error` queues, a replay tool that re-publishes after a fix | Today a poisoned message is visible in RabbitMQ and recoverable via `--reindex`, but nobody is told | S |
| B7 | **Outbox and inbox retention job** | Processed rows accumulate; a nightly delete of rows older than N days | S |
| B8 | **Content moderation**: report button, soft delete, admin view | Any public board needs it within days of launch | L |
| B9 | **Stronger bot defence**: Cloudflare Turnstile or hCaptcha as an option alongside the built-in CAPTCHA | A distorted-text CAPTCHA slows commodity bots, not a determined adversary | S |
| B10 | **Dashboards and alerts** in Application Insights: p95 by endpoint, queue depth, outbox lag, cache hit ratio | The telemetry is emitted; nothing watches it yet | M |
| B11 | **Close the read-through cache race**: stamp the list cache key with a generation counter that the indexer bumps, so a reader that started before an index write cannot store its stale page afterwards | A reader whose ES query began before the indexer finished writes its result into the cache *after* the invalidation, so a just-posted comment can vanish from the list for up to the 30 s TTL. The client compensates for the author's own comment ([`comments-page.ts`](../src/Comments.Web/src/app/features/comments/comments-page/comments-page.ts)); everyone else waits out the entry | S |

## C. Scale — when the numbers grow past the target

At 1M comments / 100k users a day, none of these is needed. They are the ordered steps if the target
grows by an order of magnitude. See [LOAD-TESTING.md](LOAD-TESTING.md#7-where-the-ceiling-is-and-what-comes-next).

| # | Improvement | Trigger | Size |
|---|---|---|---|
| C0 | **Page the GraphQL `replies` field** (per-parent `first`/`after` with `ROW_NUMBER() OVER (PARTITION BY ParentId)` in the DataLoader). The REST thread endpoint is paged; GraphQL `replies` still returns every direct reply of a parent — fine for ordinary threads, too much for the hottest seeded one (thousands of direct replies). Known limitation. | Any client uses GraphQL on hot threads | M |
| C1 | Keyset ("search after") pagination in the API alongside page numbers | Users paging past the 10,000-item cap | M |
| C2 | Read replica for the thread endpoint | Thread reads start competing with writes on the primary | S |
| C3 | Separate filegroup for `OutboxMessages` | Outbox churn visible in I/O waits on the primary | S |
| C4 | Partition `Comments` by `RootId` hash | Index maintenance windows grow uncomfortable | M |
| C5 | Elasticsearch: multiple shards and replicas, index lifecycle for old threads | Index > ~30 GB or search p95 rising | M |
| C6 | Redis cluster mode | Cache memory or throughput on one node exhausted | M |
| C7 | Shard SQL by `RootId` | One primary can no longer absorb the write rate | L |

## D. Product features

Natural next features for a comments board. None is in the brief.

| # | Feature | Size |
|---|---|---|
| D1 | Voting (the ↑ 0 ↓ in the assignment's screenshot) with counters kept in Redis and flushed asynchronously | M |
| D2 | Permalinks to a single comment, scrolled to and highlighted within its thread | S |
| D3 | Search box in the UI — the API already supports `?search=` | S |
| D4 | Collapsing long threads ("show 12 more replies") with lazy loading of deep branches via GraphQL | M |
| D5 | Quote-reply (the grey quoted line in the screenshot) | S |
| D6 | Editing within a short window, with an "edited" marker | M |
| D7 | Accounts (optional) — OIDC login, so identity is not just "name + e-mail typed in a form" | L |
| D8 | Localisation: UI strings to resource files, English alongside Russian | M |
| D9 | Markdown-lite input that compiles to the same four allowed tags | M |

## E. Engineering quality

| # | Improvement | Size |
|---|---|---|
| E1 | Playwright end-to-end tests of the checklist, run in CI against the compose stack | M |
| E2 | Architecture tests (NetArchTest) enforcing the layer dependencies | S |
| E3 | Mutation testing (Stryker) on the sanitiser — it is the security boundary | S |
| E4 | Contract tests between API and SPA, generating TypeScript types from the OpenAPI document | M |
| E5 | Load test in CI on a nightly schedule against a seeded environment, with trend tracking | M |
| E6 | Dependabot / Renovate with auto-merge for patch releases | S |
| E7 | SBOM and image signing (cosign) in the deploy pipeline | S |
| E8 | Frontend unit tests for the tag-balance validator and the reconciliation of optimistic comments | S |
