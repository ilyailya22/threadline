# ADR 0001 — Materialised path for the comment tree

**Status:** accepted

## Context

Comments nest without limit and are displayed as a cascade. Reading a whole thread must be cheap at
a million rows, and writing a reply must not contend with other replies under the assignment's
100k-users/day target.

## Options

| Option | Read a thread | Insert a reply |
|---|---|---|
| Adjacency list only | recursive CTE or one query per level | trivial |
| Nested sets | one range scan | rewrites a large part of the table |
| `hierarchyid` | one range scan | needs the existing siblings (`GetDescendant`) — contention; SQL Server only |
| Closure table | one join | one row per ancestor per insert — write amplification grows with depth |
| **Materialised path** | **one range scan** | **one insert, no reads** |

## Decision

`Path` = concatenation of fixed-width 16-character segments, one per ancestor plus self. Each segment
is the first 16 hex characters of that comment's UUID v7, i.e. its creation timestamp. Keep
`ParentId` and `RootId` alongside it.

## Consequences

- `WHERE RootId = @id ORDER BY Path` returns a thread in display order from one index range scan.
- Siblings sort chronologically for free; building a path needs no counter and no reads.
- Depth is capped at 64 so the column fits SQL Server's 1700-byte index key; breadth is unlimited.
- JSON serialisation depth must follow the cap — a lesson learnt the hard way (see TESTING.md).
