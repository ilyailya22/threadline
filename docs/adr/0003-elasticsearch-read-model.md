# ADR 0003 — Elasticsearch as the read model for the list

**Status:** accepted

## Context

The table sorts by user name, e-mail or date, both directions, 25 per page. User name and e-mail
live on `Users`; in SQL every non-date sort is a join plus a sort over all top-level rows, and deep
offsets are expensive on any engine.

## Decision

Maintain one denormalised document per thread root in Elasticsearch — author fields, text, reply
count, attachments — updated asynchronously from the outbox. Serve the list from it through a
two-level cache. Keep SQL as a fallback.

Every event re-projects the thread root from SQL (rather than patching a counter), written with
`version = replyCount, version_type = external_gte` and `refresh=wait_for`.

## Consequences

- Every sort costs the same; no join on the read path.
- The list is eventually consistent. The author's own comment is inserted optimistically in the UI;
  other visitors see a "new comments" banner via SignalR.
- Out-of-order or duplicate projections cannot regress a document: the reply count only grows, and
  a lower version is rejected.
- A lost index is rebuilt from SQL with `--reindex`.
- An initial increment-based design double-counted replies that overtook their root; the integration
  test `The_reply_count_is_exact_when_replies_race_the_indexer` guards the replacement.
