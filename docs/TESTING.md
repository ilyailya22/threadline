# Testing

| Layer | Project | Count | Needs | Runs in |
|---|---|---|---|---|
| Unit | `tests/Comments.UnitTests` | 146 | nothing | < 1 s |
| Integration | `tests/Comments.IntegrationTests` | 30 | Docker | ~1 min after images are pulled |
| Load | `loadtests/k6`, `tests/Comments.LoadTests` | 3 + 3 scenarios | a running stack | minutes |
| Frontend | `src/Comments.Web` (`ng test`, Vitest; `ng lint`) | 15 | Node 24 | seconds |

```bash
dotnet test tests/Comments.UnitTests
dotnet test tests/Comments.IntegrationTests      # Docker must be running
cd src/Comments.Web && npm test
```

## Unit tests

Fast, deterministic, no I/O. They cover the rules that are cheapest to get wrong and most expensive
when wrong:

- **Sanitiser** — allowed tags, attribute rebuilding, tag balancing, XHTML verification, and an XSS
  corpus asserted on the *parsed tree* rather than on substrings (see
  [SECURITY.md](SECURITY.md#tests) for why that distinction matters).
- **Materialised path** — depth-first ordering, sibling chronology, the depth cap, and an assertion
  that a maximum-depth path still fits SQL Server's index key limit.
- **Value objects** — the exact field rules from the assignment, plus a catastrophic-backtracking
  check on the e-mail pattern.
- **Aggregates** — root/reply invariants, one attachment per comment, the 320×240 rule enforced when
  an image is marked processed, file-name traversal neutralised.
- **Upload sniffing** — magic bytes for PNG/JPEG/GIF, text detection, rejection of executables,
  archives, PDFs and UTF-16 payloads.

## Integration tests

These start **real** SQL Server, Redis, RabbitMQ, Elasticsearch and Azurite containers with
Testcontainers and drive the application over HTTP through `WebApplicationFactory`. Nothing is
mocked except the CAPTCHA, which a test cannot solve by definition — it uses the same scoped bypass
as the load test.

The host runs with `Messaging:HostWorkerConsumers=true`, so one process exercises the whole pipeline:
HTTP → SQL + outbox → RabbitMQ → indexer → Elasticsearch → cache → HTTP. Assertions on the list wait
for the index to catch up rather than assuming it already has — the read model is eventually
consistent by design, and a test that asserts on the first read tests timing, not the system.

Between tests every store is reset: SQL by Respawn, Elasticsearch by `_delete_by_query`, the cache by
the application's own invalidation — after waiting for the previous test's pipeline to drain, so no
in-flight projection leaks a document into the next test.

### What they caught

Running against real engines found defects no unit test could have:

| Defect | Symptom | Fix |
|---|---|---|
| ASP.NET JSON depth defaults to 32 | any thread deeper than ~15 levels returned 500 | threads are now sent flat (and paged), so response depth no longer grows with the tree |
| Increment-based reply counter | a reply saved before its root was indexed was counted twice | re-project from SQL with external versioning |
| Closing tag of a demoted tag | `<a href="javascript:…">x</a>` refused the whole comment | the closing tag is demoted with its opening |
| Model-binding errors in PascalCase | the form could not attach `UserName` errors to its `userName` field | camelCase everywhere |
| Test configuration read too late | the API under test silently connected to the developer's local stack | `UseSetting`, applied before `Program` runs |

Running the full Docker stack by hand found the rest — see the commit history around
`fix: defects found by running the stack end to end`.

## Frontend

Angular 22 with Vitest (`npm test`). The components are thin — validation rules come from the
server, rendering goes through two sanitisers — so the high-value frontend checks are the
end-to-end ones: the manual QA script in [CHECKLIST.md](CHECKLIST.md) and the demo in
[DEMO-SCRIPT.md](DEMO-SCRIPT.md). Browser-level E2E tests (Playwright) are listed in
[IMPROVEMENTS.md](IMPROVEMENTS.md).

## Load tests

See [LOAD-TESTING.md](LOAD-TESTING.md).
