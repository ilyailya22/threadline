# Load testing

The Middle+ requirement: *"We have 1,000,000 comments and 100k users in 24 hours. Design the
architecture for it and write a load test."*

This document covers what the target means in requests per second, how the dataset is produced,
the scenarios, the pass/fail thresholds, and how to read the results.

---

## 1. What the target actually means

"100,000 users in 24 hours" is a daily figure; systems fail on peaks, not averages.

| Quantity | Value | Reasoning |
|---|---|---|
| Visitors / day | 100,000 | given |
| Average arrival rate | ~1.2 visitors / s | 100,000 ÷ 86,400 |
| Requests per visit | ~8 | list, a sort or two, a couple of pages, one or two threads |
| Average request rate | ~9 req/s | |
| Peak-to-mean ratio | ~10× | typical evening peak for a discussion board |
| **Peak request rate** | **~90 req/s** | the number that matters |
| Write share | ~3–5% | most visitors read; few post |
| **Design target** | **200 req/s sustained reads, 30 req/s writes** | ≥2× headroom over the estimated peak |

So the tests do not try to "prove the average works" — nine requests a second works on anything. They
drive the system at more than twice the estimated peak and check it stays inside its latency
objectives, then push further to find the ceiling.

## 2. Service level objectives

These are enforced as thresholds: k6 and NBomber both exit non-zero when one is breached, so a
regression fails a pipeline rather than quietly appearing in a graph.

| Operation | p95 | p99 | Errors |
|---|---|---|---|
| List (any sort, any page ≤ 40) | < 300 ms | < 800 ms | < 1% |
| Thread | < 400 ms | — | < 1% |
| Post a comment | < 400 ms | < 1000 ms | < 2% |
| Under a spike | — | — | **< 0.5% 5xx** (429s are expected and correct) |

## 3. Dataset

A benchmark against an empty database measures nothing. The seeder produces the stated volume with
a realistic shape:

```bash
dotnet run --project tools/Comments.Seeder -- --comments 1000000 --users 100000 --truncate
```

| Property | Value | Why it matters |
|---|---|---|
| Comments | 1,000,000 | the stated volume |
| Users | 100,000 | the stated volume |
| Top-level share | ~4% (≈40,000 threads) | exercises the filtered index as production would |
| Max depth | 8 | long enough to exercise the path range scan |
| Reply targeting | biased to recent threads | replies cluster on fresh threads on real boards |
| Timestamps | spread over 90 days | date sorts and deep pages are meaningful |
| Search index | built from SQL by the live projector | real text and true reply counts, as production serves them |

Written with `SqlBulkCopy` — a million inserts through EF's change tracker would take hours. Paths,
ids and depths come from the domain's own `CommentPath`, so the rows are indistinguishable from ones
the API produced. On the benchmark machine below the whole seed takes about a minute.

## 4. Scenarios

### `loadtests/k6/browse.js` — the read path

Arrival-rate driven (requests per second, not virtual users), ramping 10 → 50 → **200 req/s**,
held for five minutes. Traffic shape:

- 70% of list requests use the default sort (most visitors never touch the sort controls, and a
  uniform mix would understate what the cache absorbs);
- 80% hit the first three pages, 20% go deeper (pages 4–40);
- 60% of visits then open a thread;
- 1–4 s think time between steps.

```bash
k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/browse.js
```

### `loadtests/k6/write.js` — the write path

Every iteration does what a real submission does: fetch a CAPTCHA, then post multipart form data.
Ramps to 30 posts/s — about 2.6 million comments a day if sustained, well past the target.

A script cannot solve a CAPTCHA, so this uses the load-test bypass, which exists only when
`LoadTest:Enabled=true`, requires its own secret, and is **never registered in Production**:

```bash
# start the API in Development with the bypass on
LoadTest__Enabled=true LoadTest__BypassAnswer=k6secret ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/Comments.Api

k6 run -e BASE_URL=http://localhost:5080 -e BYPASS=k6secret loadtests/k6/write.js
```

If the write p95 threshold starts failing, something has crept back onto the request thread — the
design keeps that path to one database round trip.

### `loadtests/k6/spike.js` — sudden traffic

20 → **600 req/s in ten seconds**, held for a minute, then back. This checks behaviour, not
throughput: the rate limiter should shed excess with 429s rather than letting the connection pool
queue collapse, there must be no 5xx, and latency must return to baseline in the recovery window.

### `tests/Comments.LoadTests` — NBomber

The same read-path SLOs from inside the solution, runnable with no extra tooling:

```bash
dotnet run --project tests/Comments.LoadTests -- --url http://localhost:5080 --rate 200 --duration 120
```

Three scenarios: the hot first page (the cache's job), a deep page sorted by e-mail (Elasticsearch's
job — the query SQL would struggle with), and thread loading (the materialised path's job). HTML,
Markdown and CSV reports are written to `loadtests/results/nbomber`.

## 5. Running a full benchmark

```bash
docker compose up -d --build
dotnet run --project tools/Comments.Seeder -- --comments 1000000 --users 100000 --truncate
k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/browse.js
k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/spike.js
```

Point `BASE_URL` at `:5080` (the API) rather than `:8080` (nginx) to measure the application
without the proxy in front; use `:8080` to measure what a user experiences.

Note that the API's own rate limiter partitions by client, and a load generator is one client. For
throughput runs, raise the `read` policy limit or run k6 from several source addresses; the spike
test is the one that is *meant* to hit the limiter.

## 6. Results

Numbers depend heavily on the machine — a laptop running SQL Server, Elasticsearch, RabbitMQ, Redis
and the load generator on the same cores is measuring contention between them as much as the
application. Record results with the hardware they were measured on.

### Measured — 2026-09-18

**Environment:** AMD Ryzen 7 5700X (8 cores / 16 threads), 48 GB RAM, Windows 11 + Docker Desktop
(WSL 2). Everything on one machine: SQL Server, Elasticsearch, RabbitMQ, Redis, Azurite, API,
worker, nginx **and** the k6 load generator — so these numbers include all of them competing for the
same cores, and a real deployment would do better. One API replica, one worker replica.

**Dataset:** 1,000,000 comments, 100,000 users, 39,660 threads (max depth 8; the hottest thread has
13,859 replies). Seeded in **63 s**; search index built from SQL in **26 s**.

| Scenario | Load | Requests | p50 | p95 | p99 | Errors | SLO |
|---|---|---|---|---|---|---|---|
| **browse** — list | ramp to 200 req/s, 5 min hold | 121,815 total | 0.66 ms | **1.41 ms** | 18.2 ms | **0 %** | p95 < 300 ms ✓ |
| **browse** — thread (paged) | 60 % of visits | (included) | 4.15 ms | **6.28 ms** | — | **0 %** | p95 < 400 ms ✓ |
| **spike** | 20 → **600 req/s** in 10 s | 43,999 | 0.49 ms | **0.96 ms** | — | **0 % 5xx** | < 0.5 % 5xx ✓ |
| **write** — post a comment | ramp to 30 posts/s | 4,393 posts | 6.09 ms | **7.71 ms** | — | **0 %** | p95 < 400 ms ✓ |

Raw k6 output and JSON summaries: [`loadtests/results/`](../loadtests/results).

**Reading these honestly.**

- The list numbers are mostly the cache: the default first page is served from the in-process L1
  tier, which is the point of the design, but it means these are not "Elasticsearch latencies".
  Cold requests (cache miss, any sort, page 200) measured separately at **40–160 ms**.
- `dropped_iterations` in the browse run (≈ 0.8/s) are k6 running out of virtual users because of
  the 1–4 s think time, not failed requests — the server returned no errors.
- The write p95 is one transaction plus the CAPTCHA round trip. Indexing, thumbnails and live updates
  are not in it, by design.

### What the benchmark changed

The first run against the full dataset found two real problems, both fixed and then re-measured:

| Finding | Before | After |
|---|---|---|
| The thread endpoint returned a whole thread at once | hottest thread: **6.5 MB, 780 ms** | keyset-paged on the materialised path: **47 KB, ~60 ms** per page |
| The indexer waited for an index refresh per message | ≈ **32 events/s**; a 4k-event backlog took minutes | batches of up to 200: a **4,393-event backlog drained in 34 s**, worker start-up included |

### Resilience check

The worker was stopped, 4,393 comments were posted, and the worker was started again. Nothing was
lost and nothing was duplicated: the outbox held every event, the backlog drained in 34 s, and the
search index ended **exactly equal to SQL (48,447 threads in both)**.

Reports produced by a run are committed under `loadtests/results/`.

## 7. Where the ceiling is, and what comes next

What each tier costs per request, in the default path:

| Request | Cache hit | Cache miss |
|---|---|---|
| First page, default sort | in-process L1 — no network | 1 Elasticsearch query |
| Any other page or sort | Redis L2 round trip | 1 Elasticsearch query |
| Thread | — | 1 SQL range scan + 1 attachment lookup |
| Post | — | 1 Redis `GETDEL`, 2 small SQL reads, 1 transaction |

The read path scales out horizontally: more API replicas, more Elasticsearch replicas. The resource
that eventually becomes the bottleneck is **SQL Server write throughput** — every post is a
transaction on one primary. In order, the next steps would be:

1. Move `OutboxMessages` to its own filegroup, so outbox churn does not compete with the comment
   pages for I/O.
2. Partition `Comments` by a hash of `RootId`, so a thread's rows stay together and partitions can
   be maintained independently.
3. Serve the thread endpoint from a readable secondary (the data is at most seconds stale, the same
   as the search index already is).
4. Only then, shard by `RootId`.

These are listed in [IMPROVEMENTS.md](IMPROVEMENTS.md) rather than built, because at the stated
volume a single well-indexed primary is not close to its limit.
