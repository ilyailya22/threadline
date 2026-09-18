# Implementation Plan — Threadline Test Task: SPA "Comments"

> Target grade: **Middle+** (all levels cumulative: Base → Junior+ → Middle → Middle+).
> This file is the working plan. Progress is tracked with checkboxes and mirrored in git branches.

---

## 0. Context

**Task source:** Threadline test assignment — "SPA-приложение: Комментарии".

**Grading matrix (cumulative — Middle+ must contain everything below):**

| Level | Required |
|---|---|
| Base | OOP, relational SQL DB, .NET (latest), EF Core, SPA frontend, Git, Docker, deployed instance, README, DB schema file, demo video |
| Junior+ | Queue, Cache, Events, WebSocket |
| Middle | Graph (GraphQL **or** GraphDb), Message broker (RabbitMQ/Kafka), NoSQL (Elasticsearch/Redis/Mongo), Cloud (Azure/AWS/GCP) |
| Middle+ | Architecture that holds **1,000,000 messages** and **100,000 users / 24h** + a written **load test** |

**Chosen stack (all "we prefer" options from the assignment):**

| Concern | Choice | Why |
|---|---|---|
| Backend | .NET 10 / ASP.NET Core | assignment: "latest version" |
| ORM | EF Core 10 | mandatory |
| Relational DB | **MS SQL Server 2022** | assignment: "we prefer for .NET" |
| Frontend | **Angular 20** (standalone, signals, zoneless) | assignment: "we prefer for .NET" |
| Message broker | **RabbitMQ** + MassTransit | assignment: preferred option |
| NoSQL / search | **Elasticsearch 9** | assignment: "we prefer" |
| Cache | **Redis 7** (HybridCache L1+L2) | Junior+ "Cache" |
| Graph | **GraphQL (HotChocolate 15)** | Middle "Graph" — natural fit for a comment tree |
| WebSocket | **SignalR** + Redis backplane | Junior+ "WS" |
| Cloud | **Azure** (Container Apps, Azure SQL, Blob, Key Vault, App Insights) | assignment: "we prefer for .NET" |
| Object storage | Azure Blob Storage (Azurite locally) | attachments |
| IaC | Bicep + GitHub Actions (OIDC) | reproducible deploy |
| Load test | k6 (primary) + NBomber (C#, in-solution) | Middle+ |
| Containers | Docker + Docker Compose | mandatory |

**Deployment decision (confirmed with the author):** no paid Azure subscription is available during
development. Therefore: all application code, Bicep IaC and the CI/CD pipeline are written and
validated (`az bicep build`, `what-if` where possible) now; the actual `azd up` / `az deployment`
run happens on an **Azure Free Trial** ($200 / 30 days) immediately before submitting the task.
`docker compose up` is the always-working path and is what the demo video records.

---

## 1. Architecture at a glance

```
                       ┌──────────────────────────────────────────┐
  Browser (Angular)    │  Azure Static Web Apps / nginx container  │
        │  HTTPS        └──────────────────────────────────────────┘
        │  WS (SignalR)
        ▼
┌───────────────────────────────────────────────────────────────────┐
│  API (ASP.NET Core, stateless, N replicas)                        │
│  REST  /api/*      GraphQL  /graphql      SignalR  /hubs/comments │
│  rate limiting · output cache · response compression              │
└───────┬───────────────┬───────────────┬───────────────┬───────────┘
        │ write         │ read (list)   │ cache         │ pub/sub
        ▼               ▼               ▼               ▼
  ┌──────────┐   ┌──────────────┐  ┌─────────┐   ┌──────────────┐
  │ MS SQL   │   │Elasticsearch │  │  Redis  │   │  RabbitMQ    │
  │ (source  │   │ (read model, │  │ L2 cache│   │ (events,     │
  │ of truth)│   │  search,     │  │ captcha │   │  work queue) │
  │ + Outbox │   │  sort/page)  │  │ backplane│  └──────┬───────┘
  └────┬─────┘   └──────▲───────┘  └─────────┘          │
       │ poll           │ index                          │ consume
       ▼                │                                ▼
  ┌─────────────────────┴────────────────────────────────────────┐
  │  Worker service (N replicas)                                  │
  │  OutboxPublisher · ElasticIndexer · AttachmentProcessor       │
  │  NotificationFanout (→ SignalR via Redis backplane)           │
  └───────────────────────────────────────────────────────────────┘
                              │
                              ▼
                   Azure Blob Storage (attachments + thumbnails)
```

**The one idea that makes Middle+ work:** the write path never does expensive work inline, and the
read path never touches SQL for list queries.

- **Write:** validate → sanitize → single SQL transaction (`Comment` + `OutboxMessage`) → 201. No
  indexing, no image processing, no fan-out on the request thread.
- **Async:** Outbox publisher → RabbitMQ → idempotent consumers do indexing, thumbnails, SignalR fan-out.
- **Read (top-level table, 25/page, sortable):** served by Elasticsearch, fronted by Redis. SQL is
  only touched for a single comment's subtree (and even that is one indexed range scan thanks to a
  materialized path).

---

## 2. Data model

```
User          (Id, UserName, Email, HomePage, CreatedAt)         -- dedup by (UserName, Email)
Comment       (Id, UserId, ParentId, RootId, Path, Depth,
               TextHtml, TextPlain, CreatedAt, ClientIp, UserAgent)
Attachment    (Id, CommentId, Kind, ContentType, OriginalName,
               SizeBytes, BlobPath, ThumbBlobPath, Width, Height)
OutboxMessage (Id, Type, Payload, OccurredAt, ProcessedAt, Attempts, Error)
InboxMessage  (MessageId, ConsumerType, ProcessedAt)             -- consumer idempotency
```

Key decisions:

- **`Id` = `UUID v7`** (`Guid.CreateVersion7()`, .NET 9+) — time-ordered, so it is a sane clustered
  key at 1M+ rows and gives natural LIFO without a secondary sort.
- **Materialized path** (`Path varchar(900)`, e.g. `0001.0007.0002`) + `RootId` + `Depth` — unlimited
  cascading replies, whole subtree in one `WHERE RootId = @id ORDER BY Path` index range scan. No
  recursive CTE, no N+1.
- **Filtered index** `IX_Comment_TopLevel (CreatedAt DESC) WHERE ParentId IS NULL` — the default LIFO
  page is a covering index scan even if only 4% of 1M rows are top-level.
- **Client identification** (assignment: "данные которые помогут идентифицировать клиента"):
  `UserId` + hashed IP + User-Agent + a persistent `client_id` cookie. Raw IP is stored hashed
  (GDPR-friendly), the salt lives in Key Vault.

Deliverables: `docs/DATABASE.md`, `db/schema.mysql.sql` (MySQL-dialect script that MySQL Workbench
can reverse-engineer into a diagram, as the assignment requests), `db/schema.dbml`, Mermaid ERD.

---

## 3. Work breakdown

Each phase = one `feature/*` branch, merged into `develop` with `--no-ff`, then `develop` → `main`
at the end. Conventional Commits throughout — the assignment explicitly says branching will be reviewed.

### Phase 0 — Repository & tooling
- [ ] `git init`, `.gitignore`, `.editorconfig`, `.gitattributes`, `Directory.Build.props`
      (nullable, warnings-as-errors, deterministic builds), `Directory.Packages.props` (CPM)
- [ ] `PLAN.md`, `README.md` skeleton, `docs/` tree, ADR template
- [ ] GitHub Actions: build + test + lint on PR

### Phase 1 — Domain & persistence
- [ ] `Domain`: `User`, `Comment`, `Attachment`, value objects (`UserName`, `Email`, `HomePage`),
      domain events, no framework references
- [ ] `Infrastructure`: EF Core `AppDbContext`, configurations, migrations, indexes, seed
- [ ] Outbox/Inbox tables + `IUnitOfWork` writing entity + outbox atomically
- [ ] Repository + specification for keyset pagination
- [ ] Unit tests for domain invariants; integration tests on Testcontainers (MS SQL)

### Phase 2 — Application layer (CQRS)
- [ ] MediatR pipeline: validation → logging → transaction → idempotency
- [ ] `CreateCommentCommand`, `GetTopLevelCommentsQuery`, `GetCommentThreadQuery`,
      `PreviewCommentQuery`, `IssueCaptchaQuery`
- [ ] FluentValidation rules mirrored 1:1 in Angular validators (single source: `docs/VALIDATION.md`)
- [ ] **HTML sanitizer**: strict allowlist `<a href title> <code> <i> <strong>`, everything else
      escaped; XHTML well-formedness enforced by parsing the result as XML — invalid/unclosed tags
      are rejected with a field-level error, not silently fixed
- [ ] **CAPTCHA**: server-rendered PNG (ImageSharp.Drawing), answer + attempt counter in Redis with
      TTL, one-shot consumption, alphanumeric latin only

### Phase 3 — API surface
- [ ] REST controllers + Swagger/OpenAPI, ProblemDetails, global exception handler
- [ ] **GraphQL** (HotChocolate): `comments(first, after, sortBy, sortDirection)` relay connection,
      `comment(id) { replies { replies … } }` recursive tree, DataLoader batching, depth/complexity
      limits, persisted queries
- [ ] **SignalR** hub `/hubs/comments` + Redis backplane, groups per root comment
- [ ] Rate limiting (fixed window per IP + token bucket per client_id), CORS, security headers, CSP
- [ ] File upload endpoint: magic-byte sniffing (not extension), JPG/GIF/PNG ≤ 5 MB in / resized to
      **320×240 max, proportional**; TXT ≤ 100 KB; antivirus-style extension/MIME mismatch rejection

### Phase 4 — Async pipeline
- [ ] MassTransit + RabbitMQ: topology, retry + exponential backoff, dead-letter queues
- [ ] `OutboxPublisherService` (batched, `FOR UPDATE SKIP LOCKED` equivalent via `UPDLOCK, READPAST`)
- [ ] Consumers: `CommentIndexer` (→ Elasticsearch), `AttachmentProcessor` (→ resize, → Blob),
      `CommentBroadcaster` (→ SignalR), all idempotent via Inbox
- [ ] Elasticsearch index template, analyzers, alias-based zero-downtime reindex, bulk indexing
- [ ] Redis `HybridCache` for list pages; event-driven invalidation

### Phase 5 — Angular SPA
- [ ] Angular 20 standalone + signals + zoneless, strict TS, ESLint + Prettier
- [ ] Comments **table** with sorting by User Name / E-mail / date, asc+desc, **25 per page**,
      default **LIFO**, URL-synced state, keyset pagination under the hood
- [ ] Cascading reply tree (virtualized at depth), lazy-loaded subtrees via GraphQL
- [ ] Add/reply form: Reactive Forms, live client validation, CAPTCHA image + refresh,
      **preview without reload**, tag toolbar `[i] [strong] [code] [a]`, character counter
- [ ] File upload with drag&drop, client-side dimension check + preview, progress
- [ ] **Lightbox** for images and text files, with animations (own component, no jQuery)
- [ ] SignalR live insert of new comments with a subtle highlight animation
- [ ] Simple, clean CSS design (assignment asks for it), responsive, dark mode, a11y

### Phase 6 — Middle+ : scale & load testing
- [ ] `tools/Seeder`: generates 1,000,000 comments / 100,000 users into SQL + ES (bulk, batched)
- [ ] `loadtests/k6`: scenarios — read-heavy browse (95%), write (5%), spike, soak
- [ ] `tests/LoadTests` (NBomber) for an in-solution, CI-runnable variant
- [ ] Documented SLOs and measured results: `docs/LOAD-TESTING.md`
- [ ] Scaling notes: stateless API, KEDA rules (HTTP concurrency + RabbitMQ queue depth),
      connection pooling, read replicas, ES sharding, partitioning strategy for `Comment`

### Phase 7 — Cloud & delivery
- [ ] Multi-stage Dockerfiles (API, Worker, Angular/nginx), non-root, healthchecks, `.dockerignore`
- [ ] `docker-compose.yml` (full stack, one command) + `docker-compose.override.yml` (dev)
- [ ] Bicep: Container Apps Env, ACR, Azure SQL, Storage, Key Vault, Log Analytics, App Insights,
      RabbitMQ + Elasticsearch + Redis as container apps, managed identity everywhere
- [ ] GitHub Actions: build → test → scan → push to ACR → deploy (OIDC federated credentials)
- [ ] `docs/DEPLOYMENT.md` — exact commands for the Free Trial path, plus teardown & cost table

### Phase 8 — Documentation & submission
- [ ] `README.md` — what it is, feature list mapped to every assignment bullet, screenshots,
      one-command quick start, verified from a clean clone
- [ ] `docs/ARCHITECTURE.md`, `docs/API.md`, `docs/SECURITY.md`, `docs/TESTING.md`, ADRs
- [ ] `db/schema.mysql.sql` + instructions for opening it in MySQL Workbench
- [ ] `docs/CHECKLIST.md` — every assignment requirement → where it is implemented → how to verify
      (this is what their QA will use)
- [ ] Demo video script: `docs/DEMO-SCRIPT.md`
- [ ] Self-check: fresh clone → follow README → everything runs

---

## 4. Security (assignment calls out XSS and SQL injection explicitly)

| Threat | Mitigation |
|---|---|
| XSS (stored) | server-side allowlist sanitizer, output re-sanitized client-side, `<a>` forced to `rel="nofollow noopener"` + scheme allowlist (`http/https/mailto`), CSP without `unsafe-inline` |
| XSS (reflected/DOM) | Angular escapes by default; `bypassSecurityTrustHtml` used only on sanitizer output, and the sanitizer is unit-tested against an XSS payload corpus |
| SQL injection | EF Core parameterization only; zero string-concatenated SQL; analyzers ban raw SQL outside a reviewed allowlist |
| Invalid XHTML | output parsed as XML — unbalanced tags rejected |
| Malicious upload | magic bytes + re-encode images through ImageSharp (strips EXIF/polyglots), TXT re-encoded UTF-8, no user-controlled paths, blobs served from a separate origin with `Content-Disposition` |
| Flood / bots | CAPTCHA (one-shot, TTL, attempt limit), per-IP + per-client rate limits, payload size caps |
| Secrets | none in the repo; user-secrets locally, Key Vault + managed identity in Azure |

---

## 5. Definition of Done

1. `git clone && docker compose up -d` → working app at `http://localhost:8080`, no manual steps.
2. Every bullet in the assignment is checked off in `docs/CHECKLIST.md` with a verification step.
3. Unit + integration tests green in CI; integration tests run against real MS SQL / Redis /
   RabbitMQ / Elasticsearch via Testcontainers.
4. Load test runs against a seeded 1M-comment database and its report is committed.
5. Branch history shows the actual feature-by-feature progression.
6. README reproduced from scratch by its author before submission.

---

## 6. Improvements backlog (deliberately after the main task)

These are **not** part of the assignment. They are listed separately, implemented only after
everything above is done and green, and each gets its own branch so the reviewer can see the
boundary between "the task" and "extra".

See `docs/IMPROVEMENTS.md`.
