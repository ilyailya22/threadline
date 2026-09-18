-- =====================================================================================
--  Threadline Comments — database schema, in MySQL dialect, for MySQL Workbench
-- =====================================================================================
--
--  WHY THIS FILE EXISTS
--  --------------------
--  The assignment asks for "a database schema file that can be opened in MySQL Workbench so we
--  can compare what you designed with what you implemented". The application runs on MS SQL
--  Server, and Workbench cannot reverse-engineer T-SQL — so this is the same design expressed in
--  MySQL DDL, which Workbench turns into an EER diagram in three clicks.
--
--  HOW TO OPEN IT IN MYSQL WORKBENCH
--  ---------------------------------
--    1. File → Import → Reverse Engineer MySQL Create Script…
--    2. Choose this file
--    3. Tick "Place imported objects on a diagram"
--    4. Execute → Next → Finish
--
--  THIS IS NOT THE MIGRATION. The authoritative schema is the EF Core migration in
--  src/Comments.Infrastructure/Persistence/Migrations. Where the two dialects differ, the
--  difference and the reason are noted inline below. See also docs/DATABASE.md.
--
--  Generated for: ThreadlineComments, schema version InitialSchema
-- =====================================================================================

CREATE SCHEMA IF NOT EXISTS `threadline_comments`
    DEFAULT CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE `threadline_comments`;

-- -------------------------------------------------------------------------------------
--  Users — the person who left a comment.
--
--  Identity is the (user name, e-mail) pair typed into the form; there is no login. The unique
--  index over that pair is what stops two concurrent first-time posts from creating two rows for
--  the same person.
-- -------------------------------------------------------------------------------------
CREATE TABLE `Users` (
    -- MS SQL: UNIQUEIDENTIFIER. UUID v7, so the key is time-ordered and inserts stay at the end
    -- of the clustered index instead of fragmenting it across the whole table.
    `Id`           BINARY(16)   NOT NULL,

    -- Latin letters and digits only, so the column is non-Unicode in MS SQL (VARCHAR, not
    -- NVARCHAR): half the storage and faster comparisons on a column that is indexed and sorted.
    `UserName`     VARCHAR(64)  NOT NULL,
    `Email`        VARCHAR(254) NOT NULL,
    `HomePage`     VARCHAR(2048)    NULL,

    -- MS SQL: DATETIMEOFFSET(7). The application stores UTC instants, never local time.
    `CreatedAt`    DATETIME(6)  NOT NULL,
    `LastPostedAt` DATETIME(6)  NOT NULL,

    PRIMARY KEY (`Id`),
    UNIQUE  KEY `UX_Users_UserName_Email` (`UserName`, `Email`),
            KEY `IX_Users_Email`          (`Email`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'Comment authors, identified by the (user name, e-mail) pair they type.';

-- -------------------------------------------------------------------------------------
--  Comments — the message tree.
--
--  The design decision worth looking at is `Path`: a materialised path built from fixed-width
--  16-character segments, one per ancestor, each segment being the first 16 hex characters of the
--  row's own UUID v7.
--
--  Three consequences:
--    * a whole thread is ONE range scan — WHERE RootId = ? ORDER BY Path — with no recursive CTE
--      and no query per level;
--    * lexicographic ordering of Path equals depth-first display order, so the database returns
--      rows already in the order the page renders them;
--    * building a path needs no counter and no MAX(...)+1, so concurrent replies to the same
--      parent never contend — which is what breaks first at the assignment's 100k-users/24h target.
--
--  Deliberately absent: a reply counter. Incrementing one would make every reply write to a row
--  that a popular thread shares. Counts live in the Elasticsearch read model and are updated
--  asynchronously.
-- -------------------------------------------------------------------------------------
CREATE TABLE `Comments` (
    `Id`              BINARY(16)     NOT NULL,
    `AuthorId`        BINARY(16)     NOT NULL,

    -- NULL for a top-level ("заглавный") comment — the ones the sortable table shows.
    `ParentId`        BINARY(16)         NULL,

    -- The top-level comment this one belongs to; equals Id at the root.
    `RootId`          BINARY(16)     NOT NULL,

    -- 16 chars per level x 64 levels. Stays inside SQL Server's 1700-byte index key limit.
    `Path`            VARCHAR(1024)  NOT NULL,
    `Depth`           INT            NOT NULL,

    -- Sanitised XHTML, safe to render: only <a href title>, <code>, <i>, <strong> can appear.
    `TextHtml`        VARCHAR(20000) NOT NULL,
    -- Tag-free projection, used for search indexing and previews.
    `TextPlain`       VARCHAR(20000) NOT NULL,

    `CreatedAt`       DATETIME(6)    NOT NULL,

    -- "Data that helps identify the client", as the assignment requires. The IP is stored as an
    -- HMAC-SHA256 with a server-side pepper, never in clear text: still groupable for abuse
    -- investigation, useless to anyone who only has the database.
    `ClientIpHash`    CHAR(64)       NOT NULL,
    `ClientUserAgent` VARCHAR(512)       NULL,
    `ClientId`        BINARY(16)         NULL,

    PRIMARY KEY (`Id`),

    -- The default LIFO page and both date sorts. In MS SQL this is a FILTERED index
    -- (WHERE ParentId IS NULL) with AuthorId and RootId INCLUDEd, so it contains only the ~4% of
    -- rows that are top-level and the query never touches the base table. MySQL has neither
    -- filtered nor covering-include indexes, hence the plain index here.
    KEY `IX_Comments_TopLevel_CreatedAt` (`ParentId`, `CreatedAt`),

    -- Whole-thread retrieval in one range scan, already in display order.
    KEY `IX_Comments_RootId_Path`        (`RootId`, `Path`(255)),

    -- Direct replies of one comment — what the GraphQL DataLoader batches on.
    KEY `IX_Comments_ParentId_CreatedAt` (`ParentId`, `CreatedAt`),

    KEY `IX_Comments_AuthorId`           (`AuthorId`),

    CONSTRAINT `FK_Comments_Users_AuthorId`
        FOREIGN KEY (`AuthorId`) REFERENCES `Users` (`Id`)
        ON DELETE RESTRICT ON UPDATE RESTRICT,

    -- RESTRICT, not CASCADE: SQL Server refuses a cascade on a self-referencing foreign key, and
    -- silently deleting a subtree is not a thing this system does.
    CONSTRAINT `FK_Comments_Comments_ParentId`
        FOREIGN KEY (`ParentId`) REFERENCES `Comments` (`Id`)
        ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'Messages. Unlimited cascading replies via a materialised path.';

-- -------------------------------------------------------------------------------------
--  Attachments — one optional image or text file per comment.
--
--  Status exists because acceptance and processing are separate. The upload is validated and
--  stored synchronously (fast); downscaling it to 320x240 and building a thumbnail happens on a
--  worker (slow). The row is Pending in between, and the UI says so rather than showing a broken
--  image.
-- -------------------------------------------------------------------------------------
CREATE TABLE `Attachments` (
    `Id`               BINARY(16)   NOT NULL,
    `CommentId`        BINARY(16)   NOT NULL,

    -- 1 = Image, 2 = TextFile
    `Kind`             TINYINT UNSIGNED NOT NULL,
    -- 0 = Pending, 1 = Ready, 2 = Failed
    `Status`           TINYINT UNSIGNED NOT NULL,

    `ContentType`      VARCHAR(127) NOT NULL,

    -- The name the user's file had. Display only: it never participates in a storage path, which
    -- is what makes a file called "../../web.config" a curiosity rather than a traversal bug.
    `OriginalFileName` VARCHAR(260) NOT NULL,

    `SizeBytes`        BIGINT       NOT NULL,

    -- Blob paths in Azure Blob Storage (Azurite locally), built entirely from server-side values.
    `StoragePath`      VARCHAR(512) NOT NULL,
    `ThumbnailPath`    VARCHAR(512)     NULL,

    `Width`            INT              NULL,
    `Height`           INT              NULL,
    `CreatedAt`        DATETIME(6)  NOT NULL,
    `ProcessedAt`      DATETIME(6)      NULL,
    `FailureReason`    VARCHAR(1024)    NULL,

    PRIMARY KEY (`Id`),
    KEY `IX_Attachments_CommentId` (`CommentId`),

    -- MS SQL: filtered on Status = 0, so the index stays near-empty once the backlog is drained,
    -- which is the normal state.
    KEY `IX_Attachments_Pending`   (`Status`),

    CONSTRAINT `FK_Attachments_Comments_CommentId`
        FOREIGN KEY (`CommentId`) REFERENCES `Comments` (`Id`)
        ON DELETE CASCADE ON UPDATE RESTRICT
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'Images and text files attached to comments.';

-- -------------------------------------------------------------------------------------
--  OutboxMessages — the transactional outbox.
--
--  A comment and the event announcing it are written in ONE transaction. Publishing to RabbitMQ
--  from inside the request would create two failure modes that cannot be reasoned about: a saved
--  comment with no event (missing from search forever) and an event for a transaction that rolled
--  back (a ghost in the index). A row here removes both.
-- -------------------------------------------------------------------------------------
CREATE TABLE `OutboxMessages` (
    `Id`            BINARY(16)   NOT NULL,
    `Type`          VARCHAR(200) NOT NULL,
    `Payload`       LONGTEXT     NOT NULL,
    `OccurredAt`    DATETIME(6)  NOT NULL,

    -- NULL until published. In MS SQL the index below is filtered on this being NULL, so it
    -- shrinks back to near-empty as the queue drains instead of growing with all history.
    `ProcessedAt`   DATETIME(6)      NULL,

    `Attempts`      INT          NOT NULL DEFAULT 0,
    `NextAttemptAt` DATETIME(6)      NULL,
    `Error`         VARCHAR(2000)    NULL,

    PRIMARY KEY (`Id`),
    KEY `IX_OutboxMessages_Pending` (`ProcessedAt`, `NextAttemptAt`, `OccurredAt`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'Side effects promised by a committed transaction, not yet performed.';

-- -------------------------------------------------------------------------------------
--  InboxMessages — consumer idempotency.
--
--  Delivery is at-least-once, so every consumer will eventually see the same message twice. The
--  (MessageId, ConsumerType) primary key turns "have I handled this?" into an insert that either
--  succeeds once or violates a unique constraint — the cheapest correct idempotency check there is.
-- -------------------------------------------------------------------------------------
CREATE TABLE `InboxMessages` (
    `MessageId`    BINARY(16)   NOT NULL,
    `ConsumerType` VARCHAR(200) NOT NULL,
    `ProcessedAt`  DATETIME(6)  NOT NULL,

    PRIMARY KEY (`MessageId`, `ConsumerType`),
    KEY `IX_InboxMessages_ProcessedAt` (`ProcessedAt`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci
  COMMENT = 'Record of messages a given consumer has already handled.';

-- -------------------------------------------------------------------------------------
--  __EFMigrationsHistory — EF Core's own bookkeeping, included so the diagram matches reality.
-- -------------------------------------------------------------------------------------
CREATE TABLE `__EFMigrationsHistory` (
    `MigrationId`    VARCHAR(150) NOT NULL,
    `ProductVersion` VARCHAR(32)  NOT NULL,

    PRIMARY KEY (`MigrationId`)
) ENGINE = InnoDB
  DEFAULT CHARSET = utf8mb4
  COLLATE = utf8mb4_unicode_ci;
