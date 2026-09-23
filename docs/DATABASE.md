# Database

MS SQL Server 2022, accessed through EF Core 10. The authoritative schema is the migration in
[`src/Comments.Infrastructure/Persistence/Migrations`](../src/Comments.Infrastructure/Persistence/Migrations).

## Opening the diagram in MySQL Workbench

The assignment asks for a schema file MySQL Workbench can open.
[`db/schema.mysql.sql`](../db/schema.mysql.sql) is the same design expressed in MySQL DDL, with
comments explaining where the two dialects differ.

1. **File → Import → Reverse Engineer MySQL Create Script…**
2. Select `db/schema.mysql.sql`
3. Tick **Place imported objects on a diagram**
4. **Execute → Next → Finish**

No database connection is needed. For a browser view, paste
[`db/schema.dbml`](../db/schema.dbml) into <https://dbdiagram.io>.

## Tables

```
Users 1 ──── * Comments * ──── 1 Comments (ParentId, self-reference)
                  │
                  1
                  │
                  * Attachments

OutboxMessages      InboxMessages      (messaging infrastructure, no foreign keys)
```

| Table | Purpose |
|---|---|
| `Users` | Comment authors, identified by the (user name, e-mail) pair typed into the form |
| `Comments` | The message tree — materialised path, one row per comment at any depth |
| `Attachments` | One optional image or text file per comment, with its processing status |
| `OutboxMessages` | Events written in the same transaction as the change they announce |
| `InboxMessages` | Which consumer has handled which message — idempotency under at-least-once delivery |

## Design decisions

### Keys are UUID v7

`Guid.CreateVersion7()` embeds a 48-bit timestamp in the leading bytes, so ids are time-ordered.
That makes them a sane clustered key — new rows append at the end of the index instead of
splitting pages all over it, which is the usual objection to GUID keys — and it means ordering by
id is ordering by creation time, which the LIFO sort and every tie-break rely on.

### The comment tree is a materialised path

`Path` is a concatenation of fixed-width 16-character segments, one per ancestor, each segment
taken from that comment's UUID v7. A whole thread is `WHERE RootId = @id ORDER BY Path` — one index
range scan, already in display order, no recursion. Building a path needs no reads, so concurrent
replies never contend. Full reasoning and the rejected alternatives:
[ADR 0001](adr/0001-materialised-path.md).

### No reply counter on the row

Every reply would write to the parent's row, and on a popular thread that is a hot row every writer
queues behind. Counts live in the Elasticsearch read model and are updated asynchronously.

### Text is stored twice

`TextHtml` is the sanitised XHTML that is rendered. `TextPlain` is the tag-free projection used for
search and previews. Storing both avoids stripping tags on every read and every index operation.

### Latin-only columns are `varchar`

`UserName` is restricted to latin letters and digits, so it is `varchar` rather than `nvarchar`:
half the storage and faster comparisons on a column that is indexed and sorted.

## Indexes

| Index | Columns | Filter | Serves |
|---|---|---|---|
| `IX_Comments_TopLevel_CreatedAt` | `CreatedAt` INCLUDE `AuthorId, RootId` | `ParentId IS NULL` | Default LIFO page; date sorts; SQL fallback |
| `IX_Comments_RootId_Path` | `RootId, Path` | — | Whole thread in one range scan |
| `IX_Comments_ParentId_CreatedAt` | `ParentId, CreatedAt` | — | Direct replies (GraphQL DataLoader) |
| `IX_Comments_AuthorId` | `AuthorId` | — | FK lookups |
| `UX_Users_UserName_Email` | `UserName, Email` | — | Identity lookup; prevents duplicate authors under concurrency |
| `IX_OutboxMessages_Pending` | `NextAttemptAt, OccurredAt` | `ProcessedAt IS NULL` | Publisher claim query — near-empty in steady state |

The filtered indexes are the point. With a million comments and ~4% top-level, the top-level index
holds about 40,000 entries rather than a million. The outbox and attachment indexes contain only
pending work, so they stay tiny no matter how much history accumulates.

## Migrations

Applied automatically at API start-up, so `docker compose up` needs no manual step.

```bash
# add a migration
dotnet dotnet-ef migrations add <Name> \
  --project src/Comments.Infrastructure \
  --startup-project src/Comments.Infrastructure \
  --output-dir Persistence/Migrations

# produce an idempotent SQL script, e.g. for a DBA-reviewed release
dotnet dotnet-ef migrations script --idempotent \
  --project src/Comments.Infrastructure \
  --startup-project src/Comments.Infrastructure \
  --output artifacts/migrate.sql
```

## Rebuilding the search index

Elasticsearch is derived data. If it is lost, or a message exhausted its retries and ended up in an
`_error` queue, it can be rebuilt from SQL:

```bash
dotnet run --project tools/Comments.Seeder -- --reindex true
```

This walks the thread roots by keyset and uses the same projector as the live indexer, so the
result is identical to what the event-driven path produces.
