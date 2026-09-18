# ADR 0002 — Transactional outbox instead of publishing inline

**Status:** accepted

## Context

A new comment triggers indexing, thumbnailing, cache invalidation and a live update. Publishing to
RabbitMQ from the request creates two unfixable failure modes: saved-but-never-published (the comment
is missing from search forever) and published-but-rolled-back (a ghost in the index).

## Decision

Write the event as a row in the same transaction as the comment. A background publisher claims rows
with `UPDLOCK, READPAST`, publishes them, and marks them processed. Consumers are idempotent through
an inbox table keyed on (message id, consumer).

## Consequences

- Atomicity between state and events; at-least-once delivery.
- Publishers scale out with no coordination — `READPAST` makes them take disjoint batches.
- Every consumer must tolerate duplicates and reordering, which shaped ADR 0003's projection design.
- Latency from commit to publish is one poll interval under idle load (500 ms), zero under load.
