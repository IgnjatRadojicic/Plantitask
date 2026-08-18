<p align="center">
  <img src="Plantitask/docs/logo.png" alt="Plantitask Logo" width="120" />
</p>

<h1 align="center">Plantitask</h1>

<p align="center">
  <strong>Small Teams who Plant Trees</strong>
</p>

<p align="center">
  A nature-themed gamified task management platform where completing tasks grows virtual trees on your field, and a portion of revenue plants real ones.
</p>

<p align="center">
  <a href="https://www.codefactor.io/repository/github/ignjatradojicic/plantitask"><img src="https://www.codefactor.io/repository/github/ignjatradojicic/plantitask/badge" alt="CodeFactor" /></a>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet" alt=".NET 10" />
  <img src="https://img.shields.io/badge/Blazor-WASM-512BD4?logo=blazor" alt="Blazor WASM" />
  <img src="https://img.shields.io/badge/EF%20Core-10.0-512BD4" alt="EF Core 10" />
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL" />
  <img src="https://img.shields.io/badge/SignalR-Realtime-512BD4" alt="SignalR" />
  <img src="https://img.shields.io/badge/Redis-Sessions-DC382D?logo=redis&logoColor=white" alt="Redis" />
  <img src="https://img.shields.io/badge/Hangfire-Jobs-5C2D91" alt="Hangfire" />
  <img src="https://img.shields.io/badge/PixiJS-8-E91E63?logo=pixijs" alt="PixiJS" />
  <img src="https://img.shields.io/badge/MudBlazor-v9-7B1FA2" alt="MudBlazor" />
  <img src="https://img.shields.io/badge/xUnit-real%20Postgres-512BD4" alt="xUnit" />
</p>

---

## What is Plantitask?

Plantitask is a full-stack SaaS application that reimagines project management for small teams. Instead of spreadsheets and complex enterprise tools, teams organize work through a visual field where each project is a tree. As tasks get completed, the tree grows from a seed to a flowering tree.

The platform is built on a real mission: a portion of all future revenue will go to tree-planting foundations like One Tree Planted and Trees for the Future.

---

## Screenshots

### Landing Page
<p align="center">
  <img src="Plantitask/docs/screenshots/landing-page.PNG" alt="Landing Page" width="100%" />
</p>

### The Field
> Each tree represents a project group. Plant seeds to create groups, drag trees to rearrange, and watch them grow as tasks are completed.
<p align="center">
  <img src="Plantitask/docs/screenshots/field.PNG" alt="The Field" width="100%" />
</p>

### Kanban Board
> Drag-and-drop task management with optimistic concurrency. Move tasks between columns or reorder within a column. The backend handles simultaneous edits gracefully with automatic retry logic.
<p align="center">
  <img src="Plantitask/docs/screenshots/kanban-board.PNG" alt="Kanban Board" width="100%" />
</p>

### My Garden (Dashboard)
> Personal overview with active tasks, overdue alerts, completion trends, and group statistics.
<p align="center">
  <img src="Plantitask/docs/screenshots/my-garden.PNG" alt="My Garden Dashboard" width="100%" />
</p>

<details>
<summary><strong>More Screenshots</strong></summary>

### Task Creation
<p align="center">
  <img src="Plantitask/docs/screenshots/create-task.PNG" alt="Create Task" width="80%" />
</p>

### Real-time Notifications
<p align="center">
  <img src="Plantitask/docs/screenshots/notifications.PNG" alt="Notifications" width="100%" />
</p>

### Authentication
<p align="center">
  <img src="Plantitask/docs/screenshots/login.PNG" alt="Login" width="45%" />
  &nbsp;&nbsp;
  <img src="Plantitask/docs/screenshots/register.PNG" alt="Register" width="45%" />
</p>

</details>

---

## Tech Stack

### Backend

| Layer | Technology |
|-------|-----------|
| Framework | ASP.NET Core 10 Web API |
| Architecture | Clean Architecture (Contracts / Core / Infrastructure / API) |
| ORM | Entity Framework Core 10 (Npgsql) |
| Database | PostgreSQL 16 |
| Authentication | JWT access tokens (15 min) + rotating refresh tokens with reuse detection |
| Session Store | Redis (refresh tokens, verification codes, email verification flags) |
| Real-time | SignalR (notification, field and kanban hubs) |
| Background Jobs | Hangfire on PostgreSQL storage |
| Email | Pluggable provider: Resend over SMTP in production, SendGrid client retained |
| Payments | PayPal orders and subscriptions with signed webhooks |
| Entitlements | Versioned plan catalogue plus dated grants, resolved per request |
| Concurrency | Optimistic concurrency (`xmin`) with a bounded retry loop on Kanban moves |
| Soft Delete | Flat `IsDeleted` filters plus an explicit transactional write cascade |
| Audit Trail | Separate `DbContextFactory` connection so audit rows survive a rollback |
| Testing | xUnit against a real PostgreSQL and a real Redis |
| API Documentation | Swagger / OpenAPI |
| Rate Limiting | Fixed window: 60/min general, 15/min auth, 10 per 5 min verification |

### Frontend

| Layer | Technology |
|-------|-----------|
| Framework | Blazor WebAssembly (.NET 10) |
| Component Library | MudBlazor v9 |
| Canvas Engine | PixiJS 8 (via JS interop) |
| Shared Types | `Plantitask.Contracts` referenced directly, no hand-copied models |
| Session | `SessionService` plus a cross-tab `navigator.locks` mutex |
| Local Storage | Blazored.LocalStorage |
| Styling | Custom CSS with a dark mode toggle (Sora + DM Sans typography) |

---

## Architecture

Five projects. The dependency arrows only point one way.

```
src/
├── Plantitask.Contracts/        # Wire shapes both sides reference. Zero dependencies.
│   ├── Enums/                   # GroupRole, PlanTier, GrantSource, NotificationType, TreeStage
│   ├── Groups|Tasks|Plans|...   # Every DTO the browser deserializes
│   └── Kanban/                  # Typed SignalR event payloads
│
├── Plantitask.Core/             # Entities, interfaces, Result/Error, projections
│   ├── Entities/                # User, Group, TaskItem, PlanVersion, UserPlanGrant
│   ├── Interfaces/              # IGroupService, ITaskService, IEntitlementService, ...
│   ├── Plans/                   # UserEntitlements, the resolved "what may this user do"
│   ├── Projections/             # Expression trees EF translates to SQL
│   └── Common/                  # Result pattern, Error types, pagination extension
│
├── Plantitask.Infrastructure/   # EF Core, migrations, service implementations
│   ├── Data/                    # DbContext, configurations, 21 migrations
│   └── Services/                # Auth, Group, Task, Entitlement, PayPal, jobs, storage, email
│
├── Plantitask.Api/              # Controllers, middleware, hubs, Program.cs
│
└── Plantitask.Web/              # Blazor WebAssembly frontend
    ├── Pages/                   # Landing, Field, Kanban, Dashboard, Pricing, Settings
    ├── Services/                # API clients on BaseApiService, SessionService, SignalR clients
    └── wwwroot/                 # PixiJS field engine, session-lock.js, CSS

tests/
└── Plantitask.Tests/            # xUnit suite against a real Postgres and a real Redis
```

**Why Contracts exists.** The frontend used to hand maintain its own copies of every DTO and enum. They drifted. The members screen broke the moment `GroupRole` was renumbered, because the web had the role ranks hardcoded as `1 2 3 4`. A dependency free leaf project that both sides reference turns that class of drift into a compile error. Server only shapes stayed in Core on purpose: `UploadAttachmentDto` carries `IFormFile`, the PayPal webhook shapes never reach a client, and `GoogleAuthSettings` holds a secret.

**Result pattern, not exceptions.** Services return `Result<T>`. Controllers convert with `ToActionResult()`. The frontend mirrors it with `ServiceResult<T>`, so every page handles success and failure in the same two lines.

**One place per rule.** `IEntitlementService` is the only code that decides what a user may do. `IGroupService` is the only code that answers a membership question. Enforcement and display read the same number, which removes the class of bug where the API refuses something the UI says is allowed.

---

## Decisions We Own

The last month was a rewrite pass, not a feature sprint. These are the calls that were made and the reasoning behind them.

### Premium is a versioned catalogue, not columns on the user

`User` carried `MaxGroups` and six premium columns. Three tables replace them.

- **`Plans`** holds identity and display copy and is freely editable. Renaming Premium should reach every user at once.
- **`PlanVersions`** holds what a plan grants and is append only. Changing `MaxGroups` must not reprice anyone who already bought, so it becomes a new version and existing grants keep pointing at the old one.
- **`UserPlanGrants`** says who holds which version and until when.

The split is by mutation rule, not by convenience. `EntitlementService` resolves the active grant by tier first, so a 30 day pass bought during a live subscription can never downgrade anybody. No grant means the free plan, which is why free users need no row and the migration needed no backfill for them.

Two indexes sit on `PayPalRef` on purpose. The plain one serves the lookup across every grant, open or closed, which is how a captured order is stopped from being granted twice. The partial unique one allows at most one open grant per reference, which is what makes a redelivered `ACTIVATED` webhook harmless. An application check cannot close that race, because two deliveries can both read "no grant" before either inserts.

The migration seeds the catalogue, copies live premium into grants, then drops the columns. That order is load bearing. The scaffolded version dropped first, which would have deleted every subscription. `Down` is the mirror, so a rollback is not lossy either.

**What this deleted.** The nightly premium expiry job is gone. Premium now ends when a grant's `EndsAt` passes, so there is no boolean to flip and no 24 hour window where a lapsed user still held premium capacity while the status endpoint told them they did not.

### Storage quota is counted, never stored

Free gets 50 MB, Premium gets 500 MB, checked after validation so the size is known and before the file reaches storage. A file rejected after upload is one we are paying to keep.

The quota is per uploader, not per tree, so your files count against you wherever you put them and no tree owner can be filled up by their members.

Usage is `SUM(FileSize)` over live rows. A cached counter column would need decrementing in a delete path whose storage failure is deliberately swallowed, so the counter and the disk would diverge on every failed delete. The `long?` cast is not optional either: SQL `SUM` over zero rows is `NULL`, and mapping that onto a non-nullable `long` throws for the first user who never uploaded anything.

### Soft deleted attachments now free their bytes

Deleting a tree or a task soft deleted every attachment row in one `ExecuteUpdateAsync` and never touched storage. That was a slow leak until the quota started counting live rows, at which point it became a way around the cap entirely: upload to the limit, delete the tree, upload again. The quota freed, the disk did not.

`FilePurgedAt` separates "row deleted" from "bytes gone". That is what makes the purge job safe to run twice and lets a file that could not be deleted come back around on the next pass instead of being lost. A failed file logs and moves on rather than throwing, so one unreachable blob does not cost the rest of the batch.

`IX_TaskAttachments_PendingPurge` is a partial index acting as a worklist, so the every fifteen minutes check reads an empty index instead of scanning every attachment ever uploaded to prove there is nothing to do.

Fifteen minutes is not arbitrary. It is the window in which the quota and the disk disagree, because deleting a tree frees quota at once and the bytes go when the job runs.

### The auth graph stopped being a circle

`AuthTokenHandler` and `AuthService` both depended on `CustomAuthStateProvider` only to push a notification into it, which meant the provider could never depend on anything that refreshes. `SessionService` now owns the token pair and the refresh call, and the provider subscribes to `OnTokensChanged` instead of being pushed into.

Falling out of that shape:

- An expired access token used to mean "delete both keys and go anonymous", so idling past 60 minutes logged you out while the refresh token still had days left. It now spends the refresh token first, which is the prerequisite for dropping the access token from 60 minutes to 15.
- The refresh call goes through a bare named client with no handler in its pipeline. A refresh sent through the handler that owns it would recurse into a `SemaphoreSlim` the outer frame already holds, and WASM is single threaded, so that is a hard deadlock rather than a wasted retry.
- `SessionService` is registered Scoped. Transient would give each consumer its own lock and its own event, so a refresh would write storage and never reach the UI.

**Two tabs used to log you out of everything.** Tabs share one localStorage and therefore one single-use refresh token. Both notice the expiry at the same moment, both hand in the same token, the server rotates the first and sees the second arrive already used. That is the signature of theft, so reuse detection fired and revoked every session on every device. The `SemaphoreSlim` could never catch it: Scoped in WASM means one instance per tab, so the two locks had never heard of each other. `session-lock.js` wraps `navigator.locks`, whose scope is exactly one browser profile, which is exactly the scope that shares localStorage. It degrades rather than fails if the script is missing.

### Refresh tokens are SHA-256, and rotation marks instead of deletes

BCrypt embeds a random salt, so hashing the same token twice gives different outputs. You can verify against it but you can never look it up by it. A lookup key needs a deterministic hash, and SHA-256 is right here because a 512-bit random token cannot be brute forced, so the deliberate slowness of BCrypt buys nothing.

Rotation marks the old token revoked and leaves it readable. That is the only reason a replayed token can be told apart from an unknown one. Logout deletes outright, because nothing is being detected there. There is a test pinning that pair, because if rotation ever tidied up by deleting, reuse detection would go quiet with nothing failing.

A just-rotated token arriving in the grace window is treated as a race, not as theft.

### Enumeration and timing are part of the contract

- An unknown email burns the same BCrypt work as a wrong password, so timing cannot say which case it was.
- Unknown email, wrong password and unconfirmed account return the identical message, not merely all fail.
- Forgot password writes nothing and sends nothing for an address with no account, and still reports success.
- Logout returns success on a token that is not yours, so the endpoint cannot be used to probe which refresh tokens exist.
- Refresh failures are all 401 with one message. Different messages per branch told a caller which tokens existed and in what state.

### Roles are one number

The rank lived in three places at once: the `GroupRole` enum, a `GroupRoleLookup.PermissionLevel` column and a `PermissionLevels` constants class. Every guard converted between them. Now the enum value **is** the rank and **is** the lookup primary key. Owner 100, Manager 75, TeamLead 50, Member 25, with the gaps at 10, 40 and 60 left free on purpose so adding a Viewer later is a data change and not a renumber of everything below it.

`GroupMembers.RoleId` is a `Restrict` FK, so the migration inserts the new rows, remaps every membership, then deletes the old ones. The mapping was verified against the dev database before it shipped.

The unique index on `(GroupId, UserId)` was restored in the same pass. `JoinGroupAsync` guards duplicate membership in code, but code guards race and the unique index is the real guarantee.

### Uploads assume the client is hostile

- Client text never reaches a path, and the invariant is verified anyway. Containment is checked in every public method that touches disk, not only in download, because the three can regress independently.
- Three escapes beyond the obvious dot segments are covered. An absolute key makes `Path.Combine` discard the base entirely, and a sibling directory sharing a string prefix with the root is only caught because the check appends a separator.
- Magic bytes are checked against the extension. A Windows executable named `photo.png` and a real PNG named `report.pdf` are both rejected, because an extension allowlist alone cannot see either.
- Attachments and avatars live in separate folders. Avatars are public, attachments go through an authorized endpoint, and the directory split makes crossing between them a hard block rather than a policy.
- Downloads stream. Buffering meant ten concurrent 100 MB downloads cost 1 GB of heap; streamed they cost about 640 KB of buffers.

### Notifications know who caused them

The actor only existed as interpolated text inside `Message`, so a rename left every past notification showing the old name and the UI had no way to reach an avatar. `ActorId` is a real nullable column, nullable because the due soon and overdue jobs have no actor, and separate from `UserId` because `UserId` is the recipient and is also the authorization key on `MarkAsRead`.

Fan outs used to `Add` plus `SaveChangesAsync` per recipient, so a 20 member group cost 20 sequential round trips inside the request. They collect, `AddRange` and save once now. Status changes notify the assignee and the creator instead of all 19 other members on every drag to Done.

The overdue check sends one digest per user instead of one notification and one email per task, and dedupes through a dedicated `NotificationDigestLogs` table so a user with in-app notifications turned off cannot be double mailed. Notifications and markers commit together and emails go afterwards, so losing an email is the intended trade against notifying somebody twice.

### Writes that span statements are transactions

`ExecuteUpdateAsync` executes immediately instead of deferring to `SaveChangesAsync`, so group and task deletion were running as four independent autocommit transactions. A failure between them left the data permanently inconsistent, and concurrent readers could observe a half deleted group. `IApplicationDbContext` now exposes `BeginTransactionAsync` and nothing else from the database facade, so raw SQL stays off the interface.

`ExecuteUpdate` also bypasses the change tracker, which means the `SaveChangesAsync` override never stamps `UpdatedAt`. Every `SetProperty` chain sets it by hand.

### Query filters read rows, not navigations

Filters traversing navigations (`!t.Group.IsDeleted`) added a join to every read to answer a question about something that happens rarely, and left child rows physically marked `IsDeleted = false`, so retention and export jobs would have to reimplement the hierarchy walk to find them. The write cascade already flags every descendant, so a flat `!e.IsDeleted` filter is sufficient and cheaper.

### Reads project, they do not materialize

A pass over every service replaced `Include` chains with projections:

- The personal dashboard loaded every task ever assigned to a user, with three joined entities and no `AsNoTracking`, then filtered five ways in memory. Two years in that is hundreds of tracked entities to render six rows, and it gets slower every month the account exists.
- Group statistics loaded whole tasks to count them, and its trend was silently always zero, because the points were built from a timestamp while the dictionary was keyed on midnight.
- Mark all as read loaded every unread row to flip two booleans. It is one statement now.
- Where a method mutates the entity, the projection carries the entity alongside the scalar, because EF still tracks entities returned inside a projection. Two things change when you do that: the null check has to test the projected row rather than the entity inside it, and the navigation is no longer populated, so every read through it has to move onto the projected columns.

### The PayPal webhook status code is a protocol

The webhook returns 401 on a bad signature, 400 on a malformed body, and lets processing exceptions throw so the middleware answers 500 and PayPal redelivers. Telling PayPal a failed delivery landed is how a payment silently goes missing.

Duplicate delivery is keyed on the PayPal event id in `ProcessedWebhookEvents` with a unique index, because two concurrent deliveries can both pass an application read. The marker is staged with the premium change and one save commits both, so a handler that throws leaves no marker, and a forged event cannot consume the id of a real one arriving later.

Capture verifies the order belongs to the caller. Without that check, anybody holding somebody else's approved order id could capture that payment onto their own account. Both known positions of the `custom_id` stamp are handled, because PayPal moved it between API versions.

A failed payment does not revoke. PayPal retries for days and only sends suspended, cancelled or expired once it gives up, and those already revoke, so a bounced charge no longer costs a user their features while the retry is still pending.

The OAuth token is cached in `IMemoryCache` and expires five minutes early. It has to be `IMemoryCache` and not a field, because `AddHttpClient` registers the service as transient, so a field cache would be born and die inside one request.

### Cache headers, learned the hard way

`_framework/dotnet.js` holds the manifest of fingerprinted asset names and `blazor.webassembly.js` is what loads it. Neither filename is fingerprinted, so both keep a stable URL while their contents change every build. nginx served them `max-age=31536000, immutable`, so browsers and the Cloudflare edge pinned one build's manifest for a year. After a redeploy the cached manifest named hashes that no longer existed and every `_framework` request 404'd. The identical SRI digest reported across all of them was just the shared 404 page.

The old no-cache rule covered `index.html` and `blazor.boot.json`, which .NET 10 no longer emits. The two unfingerprinted loaders are matched explicitly now, and genuinely fingerprinted assets stay immutable, which is correct for them.

---

## Testing

The suite is 24 test classes and just over 500 test methods, which expand to roughly 600 cases once the theories are counted. It runs against a real PostgreSQL and a real Redis.

### Why the mocked DbContext was deleted

The old harness mocked `DbSet`. It could not see global query filters, transactions or `ExecuteUpdate`, and those are exactly where the tenancy bugs live. It was never going to test the thing worth testing. The four service test classes built on it had also drifted past repair: `TestDataBuilder` invented its own role ids (`1 2 3 4` instead of the real `25 50 75 100`), so every role assertion was being graded against ranks that do not exist, and still reporting green.

### Why not SQLite

Measured and dropped. It costs the same per test as a real Postgres here, and it needs the `xmin` concurrency token stripped to insert at all, which would leave the `MoveTaskAsync` retry loop permanently untested while the suite stayed green. A harness that cannot fail on the hardest code path is worse than no harness there.

### How isolation works

- `PostgresFixture` drops and recreates `plantitask_test` once per run and builds the schema with `MigrateAsync`, so every run also proves all 21 migrations still apply from empty.
- `DbTestBase` truncates every non-lookup table before each test, so a test only ever sees the seeded lookups plus rows it created itself.
- Arrange, act and assert each get their own `DbContext`, so nothing passes on a change tracker instead of on the database.
- `RedisFixture` uses database index 15 rather than a separate server, since the app uses 0 and Redis ships with 16. The index is asserted immediately before the flush rather than trusted from configuration, because `FlushDatabase` is irreversible.
- A shared seed world (`TestIds`, `TestData`, `SeedWorldAsync`) replaces per-class arrangement. It contains two groups on purpose: a cross-tenant denial test needs a caller who really exists and really belongs somewhere else, and a group filter cannot be asserted at all until there is other data around for a query to wrongly return.

### What gets two denial tests

Every group scoped method. An outsider who belongs nowhere and an owner of the wrong group fail differently, and only the second one catches an authorization check that forgot to scope itself. Every denial in `AttachmentService` also asserts the storage mock was never touched, because a version that fetched the bytes and then decided the caller was not allowed to have them returns exactly the same `Forbidden`, and nothing else would notice.

### What is real and what is mocked

| Real | Mocked | Reason |
|------|--------|--------|
| PostgreSQL, in every service test | Redis outside `RedisServiceTests` | Query filters, transactions and `ExecuteUpdate` only exist in the real database |
| Redis, in `RedisServiceTests` | The password hasher, stubbed reversibly for speed | Everything worth asserting there is Redis semantics, and a mocked `IDatabase` only confirms what the mock was told |
| The filesystem, in `LocalFileStorageServiceTests` | Storage, in `AttachmentServiceTests` | The filesystem is the thing under test there and is already covered, so the service tests do not pay for it twice |
| PayPal's HTTP surface, through a stub handler | The mailer and the token generator | `HttpClient` takes its handler as a constructor argument, which is the seam that makes an outbound API testable without a network |

Azure Blob Storage is deliberately untested. It connects on construction, so it needs Azurite or a real account, and there is no containment problem to prove because blob names are keys in a flat namespace with no parent directory to escape to. The SendGrid client is untested for the same structural reason: it builds its client internally with no seam.

### Tests that pin behaviour rather than correctness

Three tests in `AuditServiceTests` are named `KnownHole`. They document that `GetEntityHistoryAsync` currently defaults to allow for unrecognised entity types, and that `GetUserHistoryAsync` hands out groupless login rows with IP addresses. All three are why the `AuditController` routes are `NonAction` today. When the audit rework inverts those defaults, these flip to asserting `Forbidden` and become the regression guards.

One source change came out of the rebuild. `BackgroundJobService` takes `IBackgroundJobClient` and `IRecurringJobManager` instead of calling Hangfire's static facades, because a static call that reaches for `JobStorage.Current` is a global invisible dependency with nothing to substitute.

---

## CI/CD

One workflow, `.github/workflows/deploy.yml`, on push to `main`, running on a self-hosted runner next to the production stack.

```
push to main
  └─ sync the deploy directory to the pushed commit
  └─ docker compose build
  └─ start throwaway Postgres and Redis containers
  └─ dotnet test          (a non-zero exit stops here, Deploy never runs)
  └─ tear down the test datastores
  └─ docker compose up -d --remove-orphans
```

**The deploy directory is synced explicitly.** The compose stack lives in a fixed directory because it holds the `.env` and the named volumes. `actions/checkout` writes to the runner's `_work` directory, which the compose steps never touch, so without an explicit `git reset --hard origin/main` every deploy rebuilt stale code and reported success. That bug shipped old images behind green checkmarks until it was found.

**Build before teardown.** Images build first so the running containers keep serving until the new ones are ready, which avoids the downtime of a `compose down` up front.

**The test datastores are throwaway and unreachable from production.** The suite calls `DROP DATABASE` and `FLUSHDB` by design, so the containers are published on loopback only and on non-standard ports, well away from the production Postgres on 5432 and the production Redis on 6391. The port choice has its own scar: the first attempt used 55432, which sits in the ephemeral range, and an unrelated established connection on the host had already claimed it as a source port and blocked the bind. Both test ports now sit below 32768.

**Readiness is probed over TCP.** Postgres restarts once during init and listens on a unix socket before the TCP port opens, so `pg_isready` is pointed at `127.0.0.1` explicitly. That is the transport the tests connect on, and it is the only one worth waiting for.

**`.dockerignore` earns its place.** `Dockerfile.api` restores inside the image and then copies source over the top. Without the ignore, that copy drags the host's `obj/` in and overwrites the image's own restore output, so `dotnet publish --no-restore` fails with `NETSDK1064` on packages that only exist in the host NuGet cache. `obj/` is gitignored, which hides it from git but not from the build context, and the CI test step runs `dotnet test` in that exact directory on every run. The two interacted, and the fix belongs in the build context.

**Failing tests block the deploy.** That is the entire point of putting the step between build and `up -d`.

---

## Features

### Implemented

- JWT authentication with 15 minute access tokens, rotating refresh tokens, reuse detection and cross-tab safe silent renewal
- Google sign-in requiring a verified Google email, with username collision handling
- Email verification with 6-digit codes cached in Redis, and password reset with hashed single-use tokens
- Group creation with derived join codes, optional passwords, and a role hierarchy of Owner, Manager, TeamLead, Member
- Interactive PixiJS field where trees represent groups, with drag-to-rearrange and seven growth stages tied to completion
- Kanban board with drag and drop across and within columns, optimistic concurrency, bounded retry and gap-free `DisplayOrder`
- Real-time updates over SignalR for notifications, field growth and Kanban moves, with typed event payloads
- Task comments with role-aware moderation: authors edit their own, Managers and above can remove someone else's
- File attachments with local and Azure Blob backends, magic-byte validation, a 5 MB cap and a per-user storage quota
- Premium via PayPal, one-time passes and recurring subscriptions, backed by a versioned plan catalogue and dated grants
- Entitlements endpoint exposing plan, limits and current usage together, so a quota is visible before it refuses an upload
- Overdue digests, due-soon reminders and a weekly notification cleanup on Hangfire
- Notification preferences per type and per channel, with an in-app and email split
- Dashboard statistics, completion trends and per-group charts
- Audit logging on a separate connection, so a rolled-back transaction still leaves its trail
- Rate limiting: 60/min general, 15/min auth, 10 per 5 min on verification
- Dark mode

### Planned

- Admin panel, which is what unlocks the currently closed audit routes
- Sprint planning
- Task dependencies
- Advanced filtering and search
- Cosmetic store (custom trees, seasonal items, team themes)
- Real tree counter on the landing page

---

## Authentication Flow

The login experience adapts based on whether the user already has an account:

1. User enters their email address
2. The system checks if the email exists
3. **Existing user** is prompted for their password and sent directly to The Field
4. **New user** receives a 6-digit verification code, completes email verification, sets up their account, and is then sent to The Field

Every failure along that path returns the same message and burns the same work, so the flow cannot be used to enumerate accounts.

Verification codes live in Redis with a short TTL. Access tokens expire after 15 minutes and are refreshed silently by a `DelegatingHandler`, serialized across browser tabs by a `navigator.locks` mutex. Refresh tokens rotate on every use, are stored as SHA-256, and a replay revokes every session the user has.

---

## API Overview

| Endpoint Group | Description |
|---------------|-------------|
| `POST /api/auth/*` | Registration, login, Google sign-in, refresh, logout, verification, password reset |
| `GET/POST /api/groups` | Create, join by code, list, manage members and roles, transfer ownership |
| `GET/POST /api/tasks/*` | CRUD, assignment, status and priority transitions |
| `GET /api/kanban/*` | Board reads and drag-and-drop moves |
| `GET/POST /api/comments/*` | Task comments with role-aware edit and delete |
| `GET/POST /api/attachments/*` | Authorized upload, download and delete |
| `GET /api/dashboard/*` | Personal dashboard, field tree data, group statistics |
| `GET /api/notifications` | Notifications with read state, plus preferences |
| `GET/POST /api/premium/*` | PayPal orders, capture, subscriptions, cancellation, webhook |
| `GET /api/user/profile/entitlements` | Plan, limits and current usage in one payload |
| `/api/audit/*` | Present but `NonAction` until the admin panel lands |

Success returns the data with a 200. Failure returns `{ status: int, message: string }`.

---

## Performance Considerations

**Queries**
- Projection with `.Select()` on every read path, including reads that mutate, where the entity rides alongside the scalar
- Composite indexes on `(GroupId, StatusId, DisplayOrder)` for Kanban, and a trigram index on task titles for search
- A partial index used as a purge worklist, so a job that usually has nothing to do reads an empty index
- `.AsNoTracking()` on read-only paths

**Caching**
- Redis for refresh tokens, verification codes and verification flags
- `IMemoryCache` for the PayPal OAuth token, expiring five minutes early
- Precompressed framework files served directly instead of being recompressed per request
- Fingerprinted assets immutable, unfingerprinted loaders explicitly not

**Concurrency**
- Optimistic concurrency on `xmin`, with no pessimistic locks held during user think-time
- Bounded retry with exponential backoff (50ms, 100ms, 150ms) and a cleared change tracker between attempts

**Scalability**
- Stateless API
- SignalR over a Redis backplane
- Hangfire jobs distributed across workers, every recurring job registered idempotently by id

## Database Performance Metrics

---

Real metrics captured from PostgreSQL using `pg_stat_statements` and system statistics views during active application usage.

### Cache Hit Ratio

<p align="center">
  <img src="Plantitask/docs/metrics/cache_hit_ratio.png" alt="Cache Hit Ratio - 99.98%" width="480" />
</p>

99.98% of all data reads are served from memory. Out of 352,966 total block requests, only 61 required disk access.

### Query Execution Times

<p align="center">
  <img src="Plantitask/docs/metrics/query_execution_times.png" alt="Query Execution Times" width="640" />
</p>

All application queries execute under 3ms. Task updates average 0.40ms thanks to targeted index usage and `.AsNoTracking()` on read paths. Audit log inserts are the heaviest write operation at 1.48ms average, due to denormalized snapshot creation.

### Index Usage

<p align="center">
  <img src="Plantitask/docs/metrics/index_usage.png" alt="Index Usage by Table" width="640" />
</p>

Core lookup tables (GroupMembers, Groups, Users) achieve 96-100% index hit rates. Tables showing lower percentages reflect PostgreSQL's query planner correctly choosing sequential scans on small datasets. Index usage scales naturally as data grows.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for every project
- [PostgreSQL 16](https://www.postgresql.org/download/)
- [Redis](https://redis.io/download/)
- Docker, if you want to run the full stack from `docker-compose.yml`

### Setup

1. **Clone**

```bash
git clone https://github.com/IgnjatRadojicic/Plantitask.git
cd Plantitask
```

2. **Configure the API**

`appsettings.Development.json` is gitignored. Create it in `Plantitask/src/Plantitask.Api` with at least:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=PlantitaskDb;Username=postgres;Password=yourpassword",
    "RedisConnection": "localhost:6379",
    "HangfireConnection": "Host=localhost;Port=5432;Database=PlantitaskDb;Username=postgres;Password=yourpassword"
  },
  "JwtSettings": {
    "Secret": "your-256-bit-secret-key-here-make-it-long",
    "Issuer": "PlantitaskApi",
    "Audience": "PlantitaskClient",
    "AccessTokenExpiryInMinutes": 15,
    "RefreshTokenExpiryInDays": 7
  },
  "App": { "FrontendUrl": "https://localhost:7110" }
}
```

`JwtSettings` and `FileStorage` are both bound with `ValidateOnStart`, so a missing or empty key stops the app at startup with a message that names the key, instead of surfacing later as a confusing runtime failure. `FileStorage.AllowedExtensions` must not be empty, and every entry needs a magic-byte signature in `FileUploadRules`.

3. **Apply migrations**

```bash
cd Plantitask/src/Plantitask.Api
dotnet ef database update --project ../Plantitask.Infrastructure
```

4. **Run the API**

```bash
dotnet run --project Plantitask/src/Plantitask.Api
```

Available on `http://localhost:5212`, with Swagger at `/swagger`. The Hangfire dashboard opens from localhost in Development, since bearer auth never applies to a browser navigation.

5. **Run the frontend**

```bash
dotnet run --project Plantitask/src/Plantitask.Web
```

### Running the tests

The suite needs a real Postgres and a real Redis, and it drops its database and flushes its Redis index on every run. Point it at throwaway instances, never at your dev database.

```bash
export PLANTITASK_TEST_DB="Host=127.0.0.1;Port=15432;Username=postgres;Password=plantitask_test_pw"
export PLANTITASK_TEST_REDIS="127.0.0.1:16379"

dotnet test Plantitask/tests/Plantitask.Tests/Plantitask.Tests.csproj
```

`PLANTITASK_TEST_DB` must not carry a `Database=` entry. The fixture appends its own. If the variable is missing, the suite fails loudly rather than guessing a connection string.

### Running the full stack

```bash
cp .env.example .env   # then fill in POSTGRES_PASSWORD, JWT_SECRET and RESEND_API_KEY
docker compose up -d --build
```

---

## What I Learned Building This

**Mocks can only confirm what you told them.** The biggest single lesson of this stretch. A mocked `DbContext` cannot see query filters, transactions or `ExecuteUpdate`, which is precisely where the tenancy bugs live, and a test data builder that invents its own role ids grades every authorization assertion against ranks that do not exist. Deleting the whole harness and rebuilding on a real Postgres found bugs the green suite had been hiding.

**Two copies of a number will drift.** The role rank lived in an enum, a column and a constants class. The frontend kept its own copy of every DTO. Premium limits lived on the user row and in a job that maintained them. Every one of those produced a real bug where two parts of the system disagreed. The fix is always the same shape: one place owns it, everyone else derives.

**Derived beats stored, when the derivation is cheap.** Premium expiry stopped needing a nightly job the moment expiry became "is `EndsAt` in the past" instead of "has the job flipped the boolean yet". Storage usage is a `SUM` rather than a counter, because a counter has to be decremented by a code path whose failure is deliberately swallowed.

**Status codes are a protocol, not politeness.** Answering 200 to a PayPal webhook you failed to process is how a payment silently vanishes. Answering 403 to a refresh failure tells the client the wrong thing to do next.

**Scope is a real thing, and locks live inside it.** A `SemaphoreSlim` in a Scoped WASM service cannot protect localStorage, because localStorage sits above every tab and the service does not. Getting that wrong logged users out of every device for the crime of having two tabs open.

**Caching is a distributed system.** An `immutable` header on a file whose name never changes is a promise you cannot keep, and the browser and the CDN will both hold you to it for a year.

**Infrastructure bugs hide behind green checkmarks.** A deploy pipeline that built the wrong directory, and a build context that dragged the host's `obj/` into the image, both reported success while doing the wrong thing. Anything that can silently succeed deserves the same scrutiny as anything that can fail.

**Write the reasoning down.** The commit log for this month carries the why next to the what, and more than once the act of writing out why a change was safe is what revealed that it was not.

---

## Author

**Ignjat Radojicic**

- GitHub: [@IgnjatRadojicic](https://github.com/IgnjatRadojicic)

---

## License

This project is proprietary. All rights reserved.
