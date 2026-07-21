# Tier 1 (SQLite in-memory) — starter kit

> Reference, not compiled source. Type this into real `.cs` files when you reach the testing
> phase (after services + controllers). Written 2026-07-20 alongside the E2 soft-delete fix.
> Rationale for Tier 1 lives in `docs/rewrite/tests/00-testing-strategy.md` §B.
>
> **The one discipline that makes these worth writing:** see the test go RED against the
> unfixed code before you trust the green. A test you've only ever seen pass proves nothing.

---

## 0. Package

```
dotnet add tests/Plantitask.Tests package Microsoft.EntityFrameworkCore.Sqlite
```

`Microsoft.Data.Sqlite` comes in transitively.

---

## 1. `Helpers/DbTestBase.cs`

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Tests.Helpers;

public abstract class DbTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    protected readonly ApplicationDbContext Db;

    protected DbTestBase()
    {
        // "Foreign Keys=True" makes SQLite ENFORCE FK constraints — the whole reason
        // we left the mock. Without it SQLite silently ignores them.
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open(); // an in-memory SQLite DB exists only while a connection is held open

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new ApplicationDbContext(options);
        Db.Database.EnsureCreated(); // builds schema from the model: query filters, indexes, HasData seed
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose(); // closing the last connection drops the in-memory DB
        GC.SuppressFinalize(this);
    }
}
```

**Why the connection is a field, not a local:** its lifetime *is* the database's lifetime.
Let it fall out of scope and the GC closes it mid-test → confusing "no such table" errors.

---

## 2. `Services/TaskSoftDeleteFilterTests.cs` — the E2 regression

```csharp
using Microsoft.EntityFrameworkCore;
using Plantitask.Core.Entities;
using Plantitask.Tests.Helpers;
using Xunit;

namespace Plantitask.Tests.Services;

public class TaskSoftDeleteFilterTests : DbTestBase
{
    private static readonly Guid UserId  = TestDataBuilder.UserId1;
    private static readonly Guid GroupId = TestDataBuilder.GroupId1;
    private static readonly Guid TaskId  = TestDataBuilder.TaskId1;

    // Seed the minimum FK chain the filter needs: User -> Group -> Task -> Comment.
    // Order matters now — SQLite rejects a child whose parent isn't inserted yet.
    private void SeedTaskWithComment()
    {
        Db.Users.Add(new User
        {
            Id = UserId, UserName = "u1", Email = "u1@x.com",
            PasswordHash = "h", FirstName = "T", LastName = "U"
        });
        Db.Groups.Add(new Group
        {
            Id = GroupId, Name = "G", GroupCode = "CODE1",
            OwnerId = UserId, CreatedBy = UserId   // required FKs the mock never checked
        });
        Db.Tasks.Add(new TaskItem
        {
            Id = TaskId, Title = "T", GroupId = GroupId,
            StatusId = 1, PriorityId = 2, CreatedBy = UserId
            // no Group/Status/Priority navigation objects — those rows already exist (HasData)
        });
        Db.TaskComments.Add(new TaskComment
        {
            Id = Guid.NewGuid(), TaskId = TaskId, Content = "hello", CreatedBy = UserId
        });
        Db.SaveChanges();
        Db.ChangeTracker.Clear(); // force the next query to hit the DB, not return tracked instances
    }

    [Fact]
    public async Task LiveTask_CommentIsVisible()
    {
        SeedTaskWithComment();

        var comments = await Db.TaskComments.Where(c => c.TaskId == TaskId).ToListAsync();

        Assert.Single(comments); // sanity: the filter isn't hiding everything
    }

    [Fact]
    public async Task TaskSoftDeleted_CommentIsHidden()
    {
        SeedTaskWithComment();

        var task = await Db.Tasks.FirstAsync(t => t.Id == TaskId);
        task.IsDeleted = true;
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var comments = await Db.TaskComments.Where(c => c.TaskId == TaskId).ToListAsync();

        Assert.Empty(comments);              // the E2 fix: hidden via !c.Task.IsDeleted
        // And the row is untouched, not mutated — the whole point of approach 1:
        var raw = await Db.TaskComments.IgnoreQueryFilters().CountAsync(c => c.TaskId == TaskId);
        Assert.Equal(1, raw);
    }
}
```

### Two traps baked into the code above

1. **The builder fights the real context.** `TestDataBuilder.CreateTask` populates navigation
   objects (`Group = ...`, `Status = ...`). Great for the mock; poison here — inserting it tries
   to insert a *second* copy of a seeded lookup → duplicate-PK crash. Tier 1 seeds by scalar FK
   only, navigations null, parents first.
2. **`ChangeTracker.Clear()` is non-negotiable.** Without it, `ToListAsync` can hand back the
   instance already tracked in memory, bypassing the SQL filter → false green.

### How to see it go red (do this once)

Temporarily revert the `TaskComment` filter to `!tc.IsDeleted` only, run
`TaskSoftDeleted_CommentIsHidden`, watch it fail. That failure is the proof the test has teeth.

---

## 3. `EnsureCreated` caveat — Postgres-only model bits

The model has `HasPostgresExtension("pg_trgm")` and a GIN index on `Title`
(`ApplicationDbContext.cs`). SQLite ignores the extension annotation. If `EnsureCreated` throws
on the GIN index method, guard it by provider so one model stays honest across both:

```csharp
if (Database.IsNpgsql())
{
    entity.HasIndex(e => e.Title).HasMethod("gin").HasOperators("gin_trgm_ops");
}
```

`Database.IsNpgsql()` / `IsSqlite()` are the seam. This is the same boundary as the doc's
"SQLite can't test ILIKE" caveat — Postgres-specific behavior goes to the Testcontainers
mini-project (§B), not here.

---

## 4. Going up to service-level tests

The test above exercises the filter at the context layer (tightest possible E2 guard). To test a
whole service method (e.g. a cross-tenant denial on `GetGroupTasksAsync`), construct the SUT with
the **real** `Db` plus its other deps. `TaskService` needs `IApplicationDbContext` (pass `Db`),
`ILogger<TaskService>` (`NullLogger<TaskService>.Instance`), `IGroupService` (real, so seeded
memberships drive permission — heavier but honest), `IMemoryCache`
(`new MemoryCache(new MemoryCacheOptions())`), and `IBackgroundJobService` (mock — no real jobs
in a test). That real-`IGroupService` wiring is why §E.2 says stand up Tier 1 before rewriting the
service suites.
