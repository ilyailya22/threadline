# Architecture

How the system is put together, and — more usefully — why each piece is the way it is and what was
rejected.

The constraint that shapes everything: **1,000,000 comments, 100,000 users in 24 hours**. Most of
the decisions below are only interesting because of that number. At a thousand comments almost any
design works; the ones here are chosen because they still work at a million.

---

## 1. Layers

```
Comments.Api ──▶ Comments.Infrastructure ──▶ Comments.Application ──▶ Comments.Domain
Comments.Worker ──┘
```

Dependencies point inwards. `Comments.Domain` references nothing but the base class library — no EF
Core, no ASP.NET, no MediatR — which is what makes the business rules testable in milliseconds
without a database.

| Project | Contains | Never contains |
|---|---|---|
| **Domain** | Entities, value objects, domain events, invariants | Any framework, any I/O |
| **Application** | CQRS handlers, validation, the sanitiser, port interfaces | Concrete infrastructure |
| **Infrastructure** | EF Core, Elasticsearch, Redis, RabbitMQ (outbox publisher and consumers), Blob, Skia | HTTP concerns |
| **Api** | Controllers, GraphQL, SignalR hub, composition root of the web tier | Business rules, data access |
| **Worker** | Composition root of the background tier — hosts the outbox publisher and consumers | Business rules |

The application layer declares what it needs as interfaces (`ICommentRepository`,
`ICommentSearchIndex`, `IFileStorage`, `ICaptchaStore`, …) and infrastructure implements them. The
practical payoff is visible in the tests: a handler can be unit-tested with three stubs, and the
same handler runs unchanged against real SQL Server in the integration tests.

### CQRS, and how far it is taken

Commands and queries are separate, but this is **not** event sourcing and there are no two
databases pretending not to know about each other.

- **Commands** go through the domain model: load the aggregate, call a method, save. The domain
  enforces its invariants.
- **Queries** bypass the domain entirely and project straight into DTOs. There is no reason to
  materialise a `Comment` aggregate — with its value objects and its collection of attachments —
  to render a table row, and at 25 rows a page that cost is paid on every single request.

The MediatR pipeline gives every request validation and structured logging without any handler
opting in. Validators are registered, not called: a command with no validator is a greppable fact
rather than a silent hole.

Every entry point goes through the same pipeline: REST controllers and GraphQL resolvers both send
MediatR requests and never touch a repository, so a rule added to the pipeline applies to both.

---

## 2. The comment tree

This is the core data-modelling decision, and the one most worth scrutinising.

The assignment wants unlimited replies to any comment, displayed cascading. Four ways to store that:

| Approach | Read a thread | Insert a reply | Verdict |
|---|---|---|---|
| Adjacency list (`ParentId` only) | recursive CTE or N+1 | trivial | Fine at 1k rows; the CTE is the slowest query in the system at 1M |
| Nested sets (left/right) | one range scan | **rewrites half the table** | Unusable with concurrent writes |
| `hierarchyid` | one range scan | needs `GetDescendant` against siblings | SQL Server-only, and the sibling lookup contends |
| **Materialised path** | **one range scan** | **one insert, no reads** | Chosen |

### How the path works

Each comment stores a `Path`: a concatenation of fixed-width 16-character segments, one per
ancestor plus one for itself.

```
Root       0192f3c1a4b07d21
 └ Reply   0192f3c1a4b07d21 0192f3c1c8e14a05
    └ Deep 0192f3c1a4b07d21 0192f3c1c8e14a05 0192f3c2019f4c88
```

Each segment is the first 16 hex characters of the comment's own **UUID v7**, which encode the
48-bit creation timestamp.

Three properties fall out of that, and each one is load-bearing:

1. **Fixed width means no separator, and lexicographic order equals depth-first tree order.** The
   database returns a thread already in the order the page renders it. No sorting in application
   code, no re-assembly pass beyond building the parent/child links.

2. **A thread is one indexed range scan** — `WHERE RootId = @id ORDER BY Path`, covered by
   `IX_Comments_RootId_Path`. No recursive CTE, no query per level, no N+1. Because the order is
   depth-first, the same index also *pages* a thread: `AND Path > @cursor` continues exactly where
   the previous page stopped, and every page is a contiguous run of the tree in which each node's
   parent has already appeared. That mattered in practice — the seeded dataset's hottest thread has
   ~14,000 replies, which as one response was 6.5 MB; paged it is ~47 KB per request.

3. **Building a path requires no reads.** Nested sets need a table rewrite; `hierarchyid` needs to
   look at existing siblings; a per-parent counter needs `MAX(...) + 1` or an atomic increment.
   All of those put concurrent replies to the same popular comment in contention with each other —
   which is precisely the thing that falls over at the assignment's write volume. Here, a reply
   needs the parent's path and its own id, and nothing else.

Because the segment is a UUID v7 prefix, siblings also sort chronologically for free.

**The cost:** depth is capped at 64 levels, because `varchar(1024)` has to stay inside SQL Server's
1700-byte index key limit. The assignment asks for unlimited replies *per comment* — breadth, which
is genuinely unlimited here — and 64 levels of nesting is far beyond anything readable. There is a
unit test asserting the column still fits the index limit, so this cannot regress silently.

### What is deliberately not stored

**Reply counters.** Incrementing a counter on the parent or the thread root turns every reply into
a write to a row that all repliers to that thread share. On a popular thread that is the textbook
hot-row contention problem. Counts live in the Elasticsearch read model: every reply re-projects its thread root from SQL, and
the document is written with its reply count as an external version, so an out-of-order projection
can never overwrite a fresher one ([ADR 0003](adr/0003-elasticsearch-read-model.md)). A count that
is a second stale costs nothing; a write path that serialises on a hot row costs everything.

---

## 3. The write path

```
POST /api/comments
      │
      ├─ 1. Validate the CAPTCHA          (Redis GETDEL — atomic, one-shot)
      ├─ 2. Sanitise the text             (in-process, ~50 µs)
      ├─ 3. Resolve or create the author  (one indexed lookup)
      ├─ 4. Stage the attachment          (magic bytes, then stream to Blob)
      └─ 5. ONE transaction:
              INSERT Comment
              INSERT Attachment (if any)
              INSERT OutboxMessage
      ▼
    201 Created
```

What is **not** on this path: Elasticsearch indexing, image downscaling, cache invalidation,
SignalR fan-out. All four hang off `CommentCreatedDomainEvent` and happen on a worker.

That is the difference between a write that costs one database round trip and one that costs a
round trip plus an HTTP call to a search cluster plus a CPU-bound resize plus a broadcast — with
four more ways to fail and four more latency distributions added together.

### The transactional outbox

Publishing to RabbitMQ from inside the request creates two failure modes that cannot be reasoned
about:

- The comment is saved and the publish fails → the comment never appears in search. Forever.
- The publish succeeds and the transaction rolls back → an event for a comment that does not exist.

Writing the event as a row in the same transaction removes both. `OutboxPublisher` then drains the
table onto the broker.

The claim query is what makes the publisher safe to scale out:

```sql
SELECT TOP (@batch) * FROM OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE ProcessedAt IS NULL AND (NextAttemptAt IS NULL OR NextAttemptAt <= @now)
ORDER BY OccurredAt
```

`UPDLOCK` holds what this instance took; `READPAST` skips what another instance already holds
instead of blocking on it. Run ten workers and they process disjoint batches with no leader
election and no distributed lock.

Delivery is therefore **at-least-once**, and every consumer is idempotent via an `InboxMessages`
row keyed on `(MessageId, ConsumerType)` — an insert that either succeeds once or violates a
primary key.

Failures get exponential backoff written to `NextAttemptAt` rather than a tight retry loop, so one
poison message cannot starve the queue behind it.

---

## 4. The read path

```
GET /api/comments?sortBy=email&direction=ascending&page=7
      │
      ├─ 1. Redis (HybridCache: in-process L1 + Redis L2)   ── hit ──▶ done
      ├─ 2. Elasticsearch                                   ── ok  ──▶ cache and return
      └─ 3. SQL Server                                       (only if search is down)
```

**Why Elasticsearch and not just SQL.** The table sorts by user name and e-mail, which live on
`Users`, not `Comments`. In SQL that is a join plus a sort over the top-level rows — at 40,000
threads it is survivable, at 400,000 it is not, and no index makes an arbitrary
sort-plus-deep-offset cheap. The Elasticsearch document is denormalised: the author's name and
e-mail and the reply count are already on it, so every sort is a doc-values scan and every page
costs the same.

**Why the cache.** With LIFO ordering, page 1 of the default sort is requested far more often than
everything else combined, and it only changes when someone posts. `HybridCache` puts an in-process
L1 in front of Redis: the L1 removes the network hop for the hot keys, and the Redis L2 keeps
replicas consistent and survives a restart, so a deploy does not stampede the database with cold
caches. Entries are tagged and dropped by tag when a comment is posted; the 30-second TTL is a
backstop for a lost invalidation, not the primary mechanism.

The cache interface is **get-or-create**, not get/set. Under this traffic a plain get/set has a
cache-stampede failure mode: the moment a hot key expires, every concurrent request misses at once
and they all run the same expensive query.

**Why keep the SQL path at all.** Graceful degradation. If the search cluster is unavailable the
list is answered from SQL — slower, but correct. A comments board that returns 500 because a search
node restarted is a worse system than one that is briefly slower. The Elasticsearch health check is
deliberately *not* tagged `ready`, so a degraded search cluster does not take API instances out of
the load balancer.

**Deep paging is capped** at 10,000 items with a clear error. Past that, offset paging degrades on
every engine — Elasticsearch refuses beyond `index.max_result_window` outright — so the API says so
rather than timing out.

---

## 5. Indexes

Three indexes carry the system.

```sql
-- 1. The default LIFO page and both date sorts.
CREATE INDEX IX_Comments_TopLevel_CreatedAt ON Comments (CreatedAt)
    INCLUDE (AuthorId, RootId)
    WHERE ParentId IS NULL;

-- 2. A whole thread, already in display order.
CREATE INDEX IX_Comments_RootId_Path ON Comments (RootId, Path);

-- 3. Direct replies of one comment — what the GraphQL DataLoader batches on.
CREATE INDEX IX_Comments_ParentId_CreatedAt ON Comments (ParentId, CreatedAt);
```

Index 1 is both **filtered** and **covering**. Filtered because only ~4% of a million rows are
top-level, so the index holds 40,000 entries rather than a million. Covering because the included
columns mean the query never touches the base table.

The outbox and attachment indexes are filtered on their pending states, so they shrink back to
near-empty as work drains — which is the normal state — rather than growing with all history.

---

## 6. Messaging topology

```
OutboxPublisher ──▶ RabbitMQ (fanout) ──┬──▶ comment-indexer          (worker)
                                        ├──▶ attachment-processor     (worker)
                                        └──▶ comment-broadcast        (API)
```

Each consumer has its own queue, so a slow indexer does not delay a live update, and a failing
image processor does not stop search from being current.

The split between processes is deliberate:

- The **worker** owns everything CPU- or IO-heavy. Scaling it is scaling throughput.
- The **API** owns only the SignalR fan-out, because hub connections live in the web tier.

When the worker finishes an image it publishes `AttachmentReadyIntegrationEvent` rather than
touching SignalR itself. The worker therefore knows nothing about how the news reaches a browser,
and can be restarted, scaled or moved without touching the web tier.

Retries are exponential with a dead-letter queue, plus a kill switch so that a broker outage does
not turn into a thundering herd the moment it comes back.

---

## 7. Attachments

Acceptance and processing are separate steps on purpose.

**Accept** (synchronous, cheap): sniff the magic bytes, check the declared extension agrees with
them, enforce the size limits, stream to Blob Storage. Milliseconds.

**Process** (asynchronous, expensive): decode, downscale to at most 320×240 preserving the aspect
ratio, encode a PNG and a WebP thumbnail, write both, delete the original. Tens to hundreds of
milliseconds and a lot of memory.

A burst of uploads therefore fills a queue instead of occupying the API's threads and heap. The
attachment row is `Pending` in between and the UI says "файл обрабатывается…" rather than showing a
broken image; when the worker is done, SignalR swaps in the real thumbnail.

Re-encoding is also a security control: decoding to pixels and encoding afresh discards EXIF,
colour profiles and anything appended after the image payload, so a PNG that is also a valid HTML
document comes out the other side as nothing but pixels.

---

## 8. Real-time updates

SignalR with a Redis backplane. Without the backplane, a client connected to replica A never sees
an event consumed by replica B; with it, the number of API replicas stops being something the UI
can notice.

Clients join a group per thread rather than receiving everything. Broadcasting every reply to every
connected browser is the first thing to fall over on a busy board.

The UI holds incoming top-level comments behind a **"3 new comments — show"** banner instead of
splicing them into the table. Inserting a row under someone's cursor is how a user clicks the wrong
thing.

This is also what hides the eventual consistency of the search index from the person who posted:
their comment appears immediately because it was pushed to them, not because the index caught up.

---

## 9. GraphQL

GraphQL is the Middle-level "Graph" requirement, and it earns its place rather than being bolted on
for the checklist.

A comment thread is a tree of unknown depth — precisely the case REST handles badly. Either the
server picks a depth and is wrong for someone, or the client makes one request per level. A GraphQL
client asks for exactly the depth it will render, in one round trip:

```graphql
query {
  comment(id: "...") {
    author { userName }
    replies { author { userName } replies { textHtml } }
  }
}
```

Nested replies resolve through a **DataLoader**, so a five-level query costs five batched queries
rather than one per node.

Recursion is also the risk: `replies { replies { replies … } }` is an unbounded query that the
schema itself invites. Depth (12) and cost limits turn that from an outage into a 400.

---

## 10. Scaling, concretely

| Component | State | How it scales |
|---|---|---|
| API | stateless | horizontally; KEDA on HTTP concurrency |
| Worker | stateless | horizontally; KEDA on **RabbitMQ queue depth** |
| SQL Server | stateful | vertically, then read replicas for the fallback path |
| Elasticsearch | stateful | more shards and replicas |
| Redis | stateful | vertically, then cluster mode |
| RabbitMQ | stateful | clustering with quorum queues |

Scaling the worker on queue depth rather than CPU is the point. The whole reason indexing and image
processing are off the request path is that a backlog can be absorbed by adding consumers — and
queue length is what actually measures the backlog. CPU only reacts after the damage is done.

**Where the next bottleneck is.** Not the read path: that is cache and Elasticsearch, both of which
scale out. It is SQL Server write throughput. When one instance is no longer enough the next steps,
in order, are: move `OutboxMessages` to its own filegroup, partition `Comments` by `RootId` hash,
and only then shard. Measurements are in [LOAD-TESTING.md](LOAD-TESTING.md).

---

## 11. Decisions worth arguing with

Every one of these is a trade, and the alternative is defensible.

| Decision | Cost accepted | Why it was still chosen |
|---|---|---|
| Reply counts in Elasticsearch, not SQL | counts can be a second stale | no hot-row contention on the write path |
| Search index eventually consistent | own comment briefly absent from the list | write path stays one round trip; SignalR hides it |
| Depth capped at 64 | not literally unlimited nesting | keeps `Path` inside the index key limit; breadth is unlimited |
| Blobs served through the API | API bandwidth, no CDN yet | private storage, controlled headers, `.txt` cannot render in-origin |
| Migrations applied at start-up | two replicas could race | `docker compose up` and a Container Apps revision both just work |
| Redis/RabbitMQ/ES self-hosted in Azure | no managed SLA | no free managed tier exists for them; swapping is a connection string |
| MediatR pinned to 12.5.0 | no 13.x features | 13.x moved to a commercial licence |
| SkiaSharp instead of ImageSharp | a less idiomatic .NET API | ImageSharp 3.x requires a paid Six Labors licence |

The ones that would change first at real production scale are collected in
[IMPROVEMENTS.md](IMPROVEMENTS.md).

---

## 12. Decision records

| ADR | Decision |
|---|---|
| [0001](adr/0001-materialised-path.md) | Materialised path for the comment tree |
| [0002](adr/0002-transactional-outbox.md) | Transactional outbox instead of publishing inline |
| [0003](adr/0003-elasticsearch-read-model.md) | Elasticsearch as the read model for the list |
| [0004](adr/0004-custom-sanitizer.md) | A hand-written sanitiser rather than a library |
| [0005](adr/0005-skiasharp-over-imagesharp.md) | SkiaSharp for image work |

---

## 13. Code conventions

The rules below are enforced by the build, not by review: warnings are errors, analyzers run at
`latest-recommended`, and CI runs `dotnet format --verify-no-changes --severity info` and `ng lint`,
so what the IDE highlights and what the build accepts are the same thing.

| Concern | Convention |
|---|---|
| Files | One top-level type per file, file named after the type |
| Naming | `_camelCase` private instance fields; `PascalCase` constants and `static readonly` fields; `Async` suffix on awaitables |
| Commands and queries | `XxxCommand`/`XxxQuery` and `XxxHandler` in separate files, one folder per use case |
| Controllers | HTTP only — route, binding, status code, headers, rate limit. Each action sends one MediatR request; no repository, `DbContext` or business rule in a controller |
| Abstractions | Ports live in `Application/Common/Abstractions`, one interface per file, only the members a caller uses |
| Registration | One `AddXxx` extension per layer (`AddApplication`, `AddInfrastructure`) and per concern in the API |
| Limits and patterns | Defined once — in the domain (`UserName.Pattern`, `CommentBody.MaxHtmlLength`) or next to the feature (`CaptchaAnswerFormat`) — and referenced by the validator, the request model and the rules the client downloads |
| Frontend | Strict TypeScript and strict templates; form validators are built from `/api/validation-rules`, never retyped |

