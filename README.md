# Threadline Comments

A comments board: anyone can post, anyone can reply to anything, and the replies nest without limit.
Top-level comments appear in a sortable table, 25 to a page, newest first.

Built for the Threadline test assignment at the **Middle+** level — every requirement from the base
level through Middle+ is implemented, and the architecture is sized for the target the assignment
sets: **1,000,000 comments and 100,000 users in 24 hours**.

```bash
git clone <repository-url>
cd threadline-comments
docker compose up -d --build
```

Then open **http://localhost:8080**. That is the whole setup — no database to create, no index to
configure, no seed script to remember. First run takes 3–6 minutes while images are pulled and
built; after that it is seconds.

---

## Contents

- [What it does](#what-it-does)
- [Stack](#stack)
- [Architecture in one picture](#architecture-in-one-picture)
- [Running it](#running-it)
- [Developing](#developing)
- [Testing](#testing)
- [Load testing](#load-testing)
- [Deploying to Azure](#deploying-to-azure)
- [Documentation](#documentation)
- [Project layout](#project-layout)

---

## What it does

**Posting**

- A form with User Name (latin letters and digits), E-mail, Home page (optional), CAPTCHA and Text.
- Validation on the client and on the server, from a single definition — the client fetches the
  server's own rules from `/api/validation-rules` and builds its validators from them, so the two
  cannot drift apart.
- A server-rendered **preview** with no page reload, produced by the same sanitiser that will
  process the real submission.
- A toolbar for the allowed tags: `[i]`, `[strong]`, `[code]`, `[a]`. It wraps the current
  selection rather than dumping tags at the caret.
- An image or a text file can be attached. Images larger than 320×240 are scaled down
  proportionally; text files are limited to 100 KB.

**Reading**

- Top-level comments in a table, sortable by **User Name**, **E-mail** and **date added**, both
  directions, 25 per page. Default order is LIFO.
- Sorting and paging live in the URL, so a view can be bookmarked, shared and reached with the
  back button.
- Click a row to expand its thread: every reply, nested, to any depth.
- Attachments open in a lightbox with a fade-and-zoom transition — images inline, text files
  fetched and shown as text.
- New comments arrive over a WebSocket. They are offered behind a "3 new comments — show" banner
  rather than being spliced into the table under the reader's cursor.

**Security** — the assignment calls out XSS and SQL injection specifically; see
[docs/SECURITY.md](docs/SECURITY.md) for what is done about each.

---

## Stack

Every "мы предпочитаем" option in the assignment was taken.

| Concern | Choice |
|---|---|
| Backend | .NET 10, ASP.NET Core, Clean Architecture + CQRS |
| ORM | Entity Framework Core 10 |
| Relational database | **MS SQL Server 2022** |
| Frontend | **Angular 22** — standalone, signals, zoneless |
| Message broker | **RabbitMQ** via MassTransit |
| Search / NoSQL | **Elasticsearch 9** |
| Cache | **Redis 7** through `HybridCache` (in-process L1 + Redis L2) |
| Graph | **GraphQL** (HotChocolate 16) with DataLoader batching |
| WebSocket | **SignalR** with a Redis backplane |
| Cloud | **Azure** — Container Apps, Azure SQL, Blob Storage, Key Vault, Monitor |
| Object storage | Azure Blob Storage (Azurite locally) |
| Images & CAPTCHA | SkiaSharp |
| Infrastructure as code | Bicep + GitHub Actions with OIDC |
| Load testing | k6 and NBomber |
| Containers | Docker + Docker Compose |

---

## Architecture in one picture

```
                       ┌──────────────────────────────────────────┐
  Browser (Angular)    │        nginx  ·  serves the SPA,         │
        │  HTTPS       │        proxies /api and /hubs            │
        │  WebSocket   └──────────────────────────────────────────┘
        ▼
┌───────────────────────────────────────────────────────────────────┐
│  API (stateless, N replicas)                                      │
│  REST /api/*   ·   GraphQL /graphql   ·   SignalR /hubs/comments  │
│  rate limiting · output cache · response compression              │
└───────┬───────────────┬───────────────┬───────────────┬───────────┘
        │ write         │ read (list)   │ cache         │ events
        ▼               ▼               ▼               ▼
  ┌──────────┐   ┌──────────────┐  ┌─────────┐   ┌──────────────┐
  │ MS SQL   │   │Elasticsearch │  │  Redis  │   │  RabbitMQ    │
  │ truth +  │   │ read model   │  │ L2 cache│   │              │
  │ outbox   │   │              │  │ captcha │   │              │
  └────┬─────┘   └──────▲───────┘  │backplane│   └──────┬───────┘
       │ poll           │ index    └─────────┘          │ consume
       ▼                │                                ▼
  ┌─────────────────────┴────────────────────────────────────────┐
  │  Worker (N replicas)                                          │
  │  OutboxPublisher · CommentIndexer · AttachmentProcessor       │
  └───────────────────────────────────────────────────────────────┘
                              │
                              ▼
                   Azure Blob Storage (attachments)
```

**The one idea that makes the scale target work:** the write path never fans out, and the read path
never touches SQL for a list query.

- **Write** — validate, sanitise, then one transaction that inserts the comment *and* an outbox
  row. No indexing, no image processing, no notification on the request thread. It returns after a
  single database round trip.
- **Async** — the outbox publisher moves committed events to RabbitMQ; idempotent consumers index
  into Elasticsearch, downscale images and push live updates.
- **Read** — the sortable table is served from Elasticsearch, fronted by Redis. SQL only serves a
  single thread, one page at a time, as an indexed range scan on a materialised path.

The reasoning behind each of those choices — and the ones that were rejected — is in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Running it

### Requirements

- Docker Desktop (or Docker Engine) with Compose v2
- About 6 GB of RAM available to Docker — SQL Server and Elasticsearch are the hungry ones

### Start

```bash
docker compose up -d --build
```

| Service | URL | Notes |
|---|---|---|
| **Application** | http://localhost:8080 | this is the one you want |
| API | http://localhost:5080 | direct, bypassing nginx |
| API reference | http://localhost:5080/scalar/v1 | Development environment only |
| GraphQL IDE | http://localhost:5080/graphql | Nitro, built into HotChocolate |
| RabbitMQ management | http://localhost:15672 | `guest` / `guest` |
| Elasticsearch | http://localhost:9200 | |

### Check it is healthy

```bash
docker compose ps
curl http://localhost:5080/health/ready
```

### Stop

```bash
docker compose down            # keep the data
docker compose down -v         # and delete it
```

### Configuration

Copy `.env.example` to `.env` to change ports or credentials. Everything has a working default, so
the file is optional.

---

## Developing

Running the backing services in Docker and the applications on the host gives the fastest loop:

```bash
# 1. Backing services only
docker compose up -d sqlserver redis rabbitmq elasticsearch azurite

# 2. API (http://localhost:5080)
dotnet run --project src/Comments.Api

# 3. Worker
dotnet run --project src/Comments.Worker

# 4. Angular with hot reload (http://localhost:4200, proxying to the API)
cd src/Comments.Web && npm start
```

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 24](https://nodejs.org/).

Database migrations are applied automatically at API start-up. To create a new one:

```bash
dotnet dotnet-ef migrations add <Name> \
  --project src/Comments.Infrastructure \
  --startup-project src/Comments.Infrastructure \
  --output-dir Persistence/Migrations
```

---

## Testing

```bash
dotnet test tests/Comments.UnitTests          # fast, no dependencies
dotnet test tests/Comments.IntegrationTests   # real engines via Testcontainers, needs Docker
cd src/Comments.Web && npm test               # Angular
```

The integration tests start real SQL Server, Redis, RabbitMQ, Elasticsearch and Azurite containers
rather than mocking them. Slower, and the only way to know that the EF mappings, the outbox claim
query and the Elasticsearch sort actually behave against the engines they target.

See [docs/TESTING.md](docs/TESTING.md).

---

## Load testing

The Middle+ requirement. The system is benchmarked against a seeded million-comment database.

```bash
# 1. Generate the dataset (about a minute, plus ~30 s to build the search index)
dotnet run --project tools/Comments.Seeder -- --comments 1000000 --users 100000 --truncate

# 2. Read-path benchmark
k6 run -e BASE_URL=http://localhost:5080 loadtests/k6/browse.js

# 3. Or without installing k6
dotnet run --project tests/Comments.LoadTests -- --url http://localhost:5080
```

Scenarios, service level objectives and measured results are in
[docs/LOAD-TESTING.md](docs/LOAD-TESTING.md).

---

## Deploying to Azure

Infrastructure is Bicep; delivery is GitHub Actions using OIDC federated credentials, so no secret
is stored in the repository.

```bash
az deployment sub create \
  --location westeurope \
  --template-file deploy/bicep/main.bicep \
  --parameters applicationName=threadline environmentName=dev \
               sqlAdminPassword="$SQL_PASSWORD" ipHashPepper="$IP_PEPPER"
```

Step by step, including the Azure Free Trial path and a teardown that leaves nothing billable
behind: [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).

---

## Documentation

| Document | What is in it |
|---|---|
| [PLAN.md](PLAN.md) | The work plan this was built from |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | How it is put together and why, including rejected options |
| [docs/CHECKLIST.md](docs/CHECKLIST.md) | **Every assignment requirement → where it lives → how to verify it** |
| [docs/API.md](docs/API.md) | REST and GraphQL reference |
| [docs/DATABASE.md](docs/DATABASE.md) | Schema, indexes, and how to open the diagram in MySQL Workbench |
| [docs/SECURITY.md](docs/SECURITY.md) | XSS, SQL injection, uploads, CAPTCHA, rate limiting, privacy |
| [docs/LOAD-TESTING.md](docs/LOAD-TESTING.md) | Scenarios, SLOs, results and where the ceiling is |
| [docs/TESTING.md](docs/TESTING.md) | Test strategy and how to run each layer |
| [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) | Azure deployment, costs, teardown |
| [docs/IMPROVEMENTS.md](docs/IMPROVEMENTS.md) | What was deliberately left out, and what would come next |
| [docs/adr/](docs/adr/) | Architecture decision records |

Reviewers: [docs/CHECKLIST.md](docs/CHECKLIST.md) is the fastest way to verify the assignment
point by point.

---

## Project layout

```
.
├── src/
│   ├── Comments.Domain/          # entities, value objects, domain events — no framework references
│   ├── Comments.Application/     # CQRS handlers, validation, sanitiser, ports
│   ├── Comments.Infrastructure/  # EF Core, Elasticsearch, Redis, RabbitMQ, Blob, Skia
│   ├── Comments.Api/             # REST, GraphQL, SignalR, composition root
│   ├── Comments.Worker/          # outbox publisher and message consumers
│   └── Comments.Web/             # Angular SPA + nginx container
├── tests/
│   ├── Comments.UnitTests/
│   ├── Comments.IntegrationTests/
│   └── Comments.LoadTests/       # NBomber
├── tools/Comments.Seeder/        # million-comment data generator
├── loadtests/k6/                 # k6 scenarios
├── deploy/bicep/                 # Azure infrastructure
├── db/                           # schema for MySQL Workbench + DBML
├── docs/
└── docker-compose.yml
```

Dependencies point inwards: `Api → Infrastructure → Application → Domain`. The domain project
references nothing but the base class library, which is what keeps the business rules testable
without a database.

---

## Licence and third-party notices

Written for the Threadline test assignment.

- Roboto Mono (SIL Open Font License 1.1) is embedded for CAPTCHA rendering —
  `src/Comments.Infrastructure/Captcha/Fonts/OFL.txt`.
- SkiaSharp (MIT) is used for image processing instead of ImageSharp, whose 3.x releases moved to a
  commercial Six Labors licence.
- MediatR is pinned to 12.5.0, the last Apache-2.0 release.
