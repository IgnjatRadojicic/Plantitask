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
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Blazor-WASM-512BD4?logo=blazor" alt="Blazor WASM" />
  <img src="https://img.shields.io/badge/EF%20Core-8.0-512BD4" alt="EF Core" />
  <img src="https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white" alt="PostgreSQL" />
  <img src="https://img.shields.io/badge/SignalR-Realtime-512BD4" alt="SignalR" />
  <img src="https://img.shields.io/badge/Redis-Caching-DC382D?logo=redis&logoColor=white" alt="Redis" />
  <img src="https://img.shields.io/badge/Hangfire-Jobs-5C2D91" alt="Hangfire" />
  <img src="https://img.shields.io/badge/PixiJS-8-E91E63?logo=pixijs" alt="PixiJS" />
  <img src="https://img.shields.io/badge/MudBlazor-v9-7B1FA2" alt="MudBlazor" />
  <img src="https://img.shields.io/badge/xUnit-Testing-512BD4" alt="xUnit" />
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
> Drag-and-drop task management with a living tree that grows behind the board as work gets done.
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
| Framework | ASP.NET Core 8 Web API |
| Architecture | Clean Architecture (Core / Infrastructure / API) |
| ORM | Entity Framework Core 8 |
| Database | PostgreSQL 16 |
| Authentication | JWT Access Tokens + Refresh Token Rotation |
| Token & Code Storage | Redis (JWT blacklist, verification code cache) |
| Real-time | SignalR (NotificationHub) |
| Background Jobs | Hangfire (overdue task checks, scheduled notifications) |
| Email | SendGrid (SMTP templates) |
| Concurrency | Optimistic Concurrency Control (RowVersion / ConcurrencyStamp) |
| Testing | xUnit + Moq (unit and integration tests) |
| API docs/ | Swagger / OpenAPI |
| Rate Limiting | ASP.NET Core Fixed Window (60 req/min general, 10 req/min auth) |

### Frontend

| Layer | Technology |
|-------|-----------|
| Framework | Blazor WebAssembly (.NET 9) |
| Component Library | MudBlazor v9 |
| Canvas Engine | PixiJS 8 (via JS Interop) |
| State Management | Cascading Parameters + Service Events |
| Local Storage | Blazored.LocalStorage |
| Styling | Custom CSS (Sora + DM Sans typography) |

---

## Architecture

The backend follows **Clean Architecture** with strict dependency rules. The domain layer has zero external dependencies. Infrastructure depends on Core. The API layer orchestrates everything.

```
src/
├── TaskManagement.Core/             # Entities, DTOs, Interfaces, Enums
│   ├── Entities/                    # Domain models (User, Group, TaskItem, etc.)
│   ├── DTO/                         # Data transfer objects per feature
│   ├── Interfaces/                  # Service contracts (IGroupService, ITaskService)
│   ├── Enums/                       # GroupRole, TreeStage, TaskStatus
│   ├── Common/                      # Result pattern, Error types
│   └── Constants/                   # TreeThresholds, business rules
│
├── TaskManagement.Infrastructure/   # EF Core, Service implementations
│   ├── Data/                        # DbContext, Configurations, Migrations
│   └── Services/                    # AuthService, GroupService, DashboardService
│
├── TaskManagement.Api/              # Controllers, Middleware, Program.cs
│   ├── Controllers/                 # REST endpoints per aggregate
│   ├── Middleware/                   # Exception handling, rate limiting
│   └── Hubs/                        # SignalR NotificationHub
│
└── TaskManagement.Web/              # Blazor WebAssembly frontend
    ├── Pages/                       # Razor pages (Landing, Login, Field)
    ├── Layout/                      # MainLayout, PublicLayout
    ├── Services/                    # API client services (inherit BaseApiService)
    ├── Models/                      # Frontend DTOs and view models
    └── wwwroot/                     # Static assets, PixiJS engine, CSS
```

### Key Design Patterns & SOLID Principles

**Clean Architecture** with strict dependency inversion. Core has zero external dependencies and defines all interfaces. Infrastructure implements them. The API layer only orchestrates. No project references flow inward.

**Result Pattern** replaces exception-based error handling across the entire backend. Services return `Result<T>` instead of throwing. Controllers convert results via a `ToActionResult()` extension method. The frontend mirrors this with `ServiceResult<T>` for consistent error display on every page, following the same two-line pattern: check success, use data or show error.

**Dependency Inversion Principle (SOLID "D")** applied everywhere. Every service has an interface (`IAuthService`, `IGroupService`, `ITaskService`, `IDashboardService`, `IFieldUIService`). Pages and controllers depend on abstractions, never concrete classes. This enables unit testing with Moq and makes swapping implementations trivial.

**BaseApiService Inheritance** (Open/Closed Principle). All frontend HTTP services inherit from a shared abstract base class providing `GetAsync<T>`, `PostAsync<T>`, `PutAsync<T>`, `DeleteAsync<T>`, and unified error parsing. Adding a new API service means writing only the public methods. The base class is closed for modification, open for extension.

**DelegatingHandler Pipeline** for cross-cutting authentication concerns. `AuthTokenHandler` sits in the HTTP pipeline and automatically attaches JWT tokens to outgoing requests, handles 401 responses with silent token refresh, and clones failed requests for retry. Individual services never touch authorization headers. This follows the Single Responsibility Principle by keeping auth logic out of business services.

**Refresh Token Rotation** with automatic revocation. Each token refresh invalidates the previous refresh token and issues a new pair. The `ReplacedByToken` chain allows detection of token reuse attacks. Tokens are hashed before storage using BCrypt.

**Optimistic Concurrency Control** for conflict resolution. Entities carry a `RowVersion` (timestamp/byte array) checked by EF Core on every update. When two users modify the same task simultaneously, the second write receives a `DbUpdateConcurrencyException` which surfaces as a conflict response. No database locks are held during user think-time.

**Observer Pattern (Event Bus)** for cross-component communication on the frontend. `FieldUIService` decouples the MainLayout navigation buttons from the Field page. The layout fires events, the page subscribes. No tight coupling, no query parameter hacks, no shared mutable state.

**Background Job Processing** via Hangfire with Redis storage. Recurring jobs detect overdue tasks and fire notifications. Scheduled jobs handle deferred email delivery and expired token cleanup. The Hangfire dashboard provides operational visibility.

**Soft Delete Pattern** across all major entities. Deleted records are marked inactive rather than removed, enabling account recovery within a 90-day window and maintaining audit trail integrity. Queries filter soft-deleted records automatically.

**Audit Logging** on all state-changing operations. The `IAuditService` records who changed what, when, and in which group. Controllers call `LogAuditAsync` after successful mutations. The audit trail is queryable per group, per task, or per user.

**Custom Authentication State Provider** bridges JWT tokens with Blazor's built-in authorization framework. It reads the JWT from localStorage, parses claims without signature validation (that is the backend's job), checks expiration, and exposes the user identity to `AuthorizeView` and `[Authorize]` attributes throughout the component tree.

**Factory Pattern** for database context creation. `ApplicationDbContextFactory` provides design-time context instantiation for EF Core migrations, completely separate from the runtime DI pipeline.

**Seeded Random Generation** for deterministic PixiJS field layouts. Decorations are randomly placed using a hash of the user ID as the seed, so the field looks identical across sessions and devices without storing positions.

---

## Features

### Implemented

- JWT authentication with refresh token rotation and silent renewal
- Redis-backed token blacklist and verification code cache
- Email verification flow with 6-digit codes
- Google OAuth placeholder (ready for integration)
- Group creation with auto-generated join codes and optional passwords
- Role-based access control (Owner, Manager, Team Lead, Member)
- Interactive PixiJS canvas field where trees represent groups
- Drag-and-drop tree repositioning with localStorage persistence
- Seed planting flow: drag from inventory, click field, create group
- Tree growth stages tied to task completion percentage (7 stages from EmptySoil to FloweringTree)
- Real-time updates via SignalR for live tree growth
- Optimistic concurrency on task operations (prevents silent overwrites on simultaneous edits)
- Hangfire background jobs for overdue task detection and scheduled notifications
- SendGrid email integration for verification, password reset, and task alerts
- Responsive design with hideable top and bottom navigation
- Notification system with slide-in panel and background delivery
- Dashboard statistics (tasks by status, completion trends, member workload)
- Kanban board backend with drag-and-drop ordering
- File attachments with local and Azure Blob storage support
- Task comments and activity feed
- Rate limiting (60 req/min general, 10 req/min auth)
- Audit logging for compliance tracking
- Comprehensive test suite (xUnit + Moq)

### In Progress

- Payment Page
- News
- User Control Menu
- Dark mode

### Planned

- Sprint planning
- Task dependencies
- Advanced filtering and search
- Cosmetic store (custom trees, seasonal items, team themes)
- Stripe payment integration
- Real tree counter on landing page

---

## Authentication Flow

The login experience adapts based on whether the user already has an account:

1. User enters their email address
2. The system checks if the email exists
3. **Existing user** is prompted for their password and sent directly to The Field
4. **New user** receives a 6-digit verification code via SendGrid, completes email verification, sets up their account (username, password), and is then sent to The Field

Verification codes are cached in Redis with a short TTL. JWT access tokens expire after 15 minutes and are silently refreshed via a `DelegatingHandler` in the HTTP pipeline. Refresh tokens use rotation with automatic revocation of the previous token on each renewal.

---

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (backend)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (frontend)
- [PostgreSQL 16](https://www.postgresql.org/download/)
- [Redis](https://redis.io/download/) (token cache, verification codes)

### Setup

1. **Clone the repository**

```bash
git clone https://github.com/IgnjatRadojicic/TaskManagement.git
cd TaskManagement
```

2. **Configure the backend**

Copy `appsettings.Development.json.example` to `appsettings.Development.json` in the API project and update:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=plantitask;Username=postgres;Password=yourpassword"
  },
  "JwtSettings": {
    "Secret": "your-256-bit-secret-key-here-make-it-long",
    "Issuer": "Plantitask",
    "Audience": "Plantitask",
    "AccessTokenExpirationMinutes": 15,
    "RefreshTokenExpirationDays": 7
  },
  "App": {
    "FrontendUrl": "https://localhost:7110"
  }
}
```

3. **Apply database migrations**

```bash
cd src/TaskManagement.Api
dotnet ef database update --project ../TaskManagement.Infrastructure
```

4. **Run the backend**

```bash
cd src/TaskManagement.Api
dotnet run
```

The API will be available at `http://localhost:5212` with Swagger at `/swagger`.

5. **Run the frontend**

```bash
cd src/TaskManagement.Web
dotnet run
```

The app will be available at `https://localhost:7110`.

---

## API Overview

The backend exposes a RESTful API organized by aggregate:

| Endpoint Group | Description |
|---------------|-------------|
| `POST /api/auth/*` | Registration, login, token refresh, email verification, password reset |
| `GET/POST /api/groups` | Create groups, join via code, list user groups, manage members and roles |
| `GET/POST /api/tasks/*` | CRUD operations, Kanban ordering, assignments, status transitions |
| `GET /api/dashboard/*` | Personal dashboard, field tree data, group statistics |
| `GET /api/notifications` | User notifications with read/unread state |

All endpoints use the Result pattern: success returns the raw data with a 200 status, failure returns `{ status: int, message: string }`.

---

## The Tree Growth System

Trees on the field visually represent project health. Growth is calculated from the percentage of completed tasks within a group:

| Completion | Stage | Visual |
|-----------|-------|--------|
| 0% | Empty Soil | Seed sprite |
| 1-19% | Seed | Small sprout |
| 20-39% | Sprout | Sprouting plant |
| 40-59% | Sapling | Small bush |
| 60-79% | Young Tree | Medium tree |
| 80-99% | Full Tree | Large tree |
| 100% | Flowering Tree | Full tree with flowers |

All tree sprites are original pixel art assets rendered on an HTML5 canvas via PixiJS 8.

---

## Project Status

This project is under active development. The backend is feature-complete for the MVP with comprehensive test coverage. The frontend is in Phase 10D (The Field) with the core interactive canvas working. The next milestones are the Kanban board frontend and the dashboard page.

---

## Author

**Ignjat Radojicic**

- GitHub: [@IgnjatRadojicic](https://github.com/IgnjatRadojicic)

---

## License

This project is proprietary. All rights reserved.
