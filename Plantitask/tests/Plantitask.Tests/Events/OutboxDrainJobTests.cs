using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Entities;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Events;
using Plantitask.Tests.Helpers;

namespace Plantitask.Tests.Events
{
    /// <summary>
    /// Proves the drainer against real Postgres: the claim, the order, the inbox skip, the retry
    /// schedule, parking and the lock. Rows are seeded straight into the outbox and the job is run
    /// directly, the way Hangfire would build it from a job scope.
    /// </summary>
    public class OutboxDrainJobTests : DbTestBase
    {
        private readonly DrainLog _log = new();

        public OutboxDrainJobTests(PostgresFixture fixture) : base(fixture)
        {
        }

        [EventName("test.drained.v1")]
        public record Drained(Guid UserId) : IEvent;

        [EventName("test.drain-broken.v1")]
        public record Broken : IEvent;

        [EventName("test.drain-flaky.v1")]
        public record Flaky : IEvent;

        /// <summary>Shared by every scope, because the handlers themselves are rebuilt per scope.</summary>
        private sealed class DrainLog
        {
            public List<string> Runs { get; } = new();
            public int SecondFailuresLeft { get; set; }
            public int FlakyBuilds { get; set; }
        }

        /// <summary>Writes a user, so a test can see its work committed together with its inbox row.</summary>
        private sealed class FirstHandler : IDurableEventHandler<Drained>
        {
            private readonly ApplicationDbContext _db;
            private readonly DrainLog _log;

            public FirstHandler(ApplicationDbContext db, DrainLog log)
            {
                _db = db;
                _log = log;
            }

            public string Name => "first";

            public Task HandleAsync(Drained e, EventContext context, CancellationToken ct)
            {
                _log.Runs.Add($"first {e.UserId}");
                _db.Users.Add(TestData.User(e.UserId, "u" + e.UserId.ToString("N")[..12]));
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Fails while the log says so. It saves a user before throwing, so a test can see that a
        /// failing handler's own transaction rolls its work back.
        /// </summary>
        private sealed class SecondHandler : IDurableEventHandler<Drained>
        {
            private readonly ApplicationDbContext _db;
            private readonly DrainLog _log;

            public SecondHandler(ApplicationDbContext db, DrainLog log)
            {
                _db = db;
                _log = log;
            }

            public string Name => "second";

            public async Task HandleAsync(Drained e, EventContext context, CancellationToken ct)
            {
                _log.Runs.Add("second");

                if (_log.SecondFailuresLeft > 0)
                {
                    _log.SecondFailuresLeft--;
                    _db.Users.Add(TestData.User(TestIds.MemberId, "member"));
                    await _db.SaveChangesAsync(ct);
                    throw new InvalidOperationException("second failed on purpose");
                }
            }
        }

        private sealed class UnbuildableHandler : IDurableEventHandler<Broken>
        {
            public UnbuildableHandler() => throw new InvalidOperationException("cannot be built on purpose");

            public string Name => "unbuildable";

            public Task HandleAsync(Broken e, EventContext context, CancellationToken ct) => Task.CompletedTask;
        }

        private sealed class FlakyHandler : IDurableEventHandler<Flaky>
        {
            // The lookup builds it once to read the name. Every build after that fails.
            public FlakyHandler(DrainLog log)
            {
                if (++log.FlakyBuilds > 1)
                    throw new InvalidOperationException("flaky build on purpose");
            }

            public string Name => "flaky";

            public Task HandleAsync(Flaky e, EventContext context, CancellationToken ct) => Task.CompletedTask;
        }

        // Shaped like Program.cs: the context is scoped, the handlers go through AddEventHandler.
        private ServiceProvider Services(Action<IServiceCollection> handlers, int maxMessagesPerRun = 100)
        {
            var services = new ServiceCollection()
                .AddScoped(_ => new ApplicationDbContext(ContextOptions))
                .AddSingleton(new EventTypeRegistry(typeof(OutboxDrainJobTests).Assembly))
                .AddSingleton(Options.Create(new OutboxSettings { MaxMessagesPerRun = maxMessagesPerRun }))
                .AddSingleton(_log)
                .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                .AddScoped<OutboxDrainJob>();

            handlers(services);
            return services.BuildServiceProvider(validateScopes: true);
        }

        // One drain run in its own job scope, the way Hangfire runs it.
        private async Task DrainAsync(
            Action<IServiceCollection> handlers, int maxMessagesPerRun = 100, CancellationToken token = default)
        {
            await using var root = Services(handlers, maxMessagesPerRun);
            await using var job = root.CreateAsyncScope();
            await job.ServiceProvider.GetRequiredService<OutboxDrainJob>().RunAsync(token);
        }

        private static void FirstAndSecond(IServiceCollection s) =>
            s.AddEventHandler<FirstHandler>().AddEventHandler<SecondHandler>();

        private async Task<Guid> SeedAsync(
            IEvent e, DateTime? occurredAt = null, Action<OutboxMessage>? state = null) =>
            await SeedRawAsync(EventNames.For(e.GetType()), EventSerializer.Serialize(e), occurredAt, state);

        private async Task<Guid> SeedRawAsync(
            string eventType, string payload, DateTime? occurredAt = null, Action<OutboxMessage>? state = null)
        {
            var message = new OutboxMessage
            {
                EventType = eventType,
                Payload = payload,
                OccurredAt = occurredAt ?? DateTime.UtcNow,
                Source = EventSource.Request
            };
            state?.Invoke(message);

            await using var db = NewContext();
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync();
            return message.Id;
        }

        private async Task<OutboxMessage> RowAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.OutboxMessages.SingleAsync(m => m.Id == id);
        }

        private async Task<List<string>> InboxHandlersAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.InboxMessages.Where(m => m.MessageId == id).Select(m => m.Handler).ToListAsync();
        }

        private async Task<bool> UserExistsAsync(Guid id)
        {
            await using var db = NewContext();
            return await db.Users.AnyAsync(u => u.Id == id);
        }

        [Fact]
        public async Task Run_NothingOwed_EndsQuietly()
        {
            var run = () => DrainAsync(FirstAndSecond);

            await run.Should().NotThrowAsync();
            _log.Runs.Should().BeEmpty();
        }

        [Fact]
        public async Task Run_OwedRow_EveryDurableHandlerRunsOnceAndTheRowIsProcessed()
        {
            var userId = Guid.NewGuid();
            var id = await SeedAsync(new Drained(userId));

            await DrainAsync(FirstAndSecond);

            _log.Runs.Should().Equal($"first {userId}", "second");
            (await InboxHandlersAsync(id)).Should().BeEquivalentTo("first", "second");
            (await UserExistsAsync(userId)).Should().BeTrue("the handler's work commits with its inbox row");
             
            var row = await RowAsync(id);
            row.ProcessedAt.Should().NotBeNull();
            row.Attempts.Should().Be(0);
        }

        [Fact]
        public async Task Run_SeveralOwedRows_DrainsTheOldestFirst()
        {
            var now = DateTime.UtcNow;
            var (oldest, middle, newest) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            await SeedAsync(new Drained(middle), now.AddMinutes(-2));
            await SeedAsync(new Drained(newest), now.AddMinutes(-1));
            await SeedAsync(new Drained(oldest), now.AddMinutes(-3));

            await DrainAsync(s => s.AddEventHandler<FirstHandler>());

            _log.Runs.Should().Equal($"first {oldest}", $"first {middle}", $"first {newest}");
        }

        [Fact]
        public async Task Run_RowsNotDueParkedOrProcessed_AreLeftAlone()
        {
            var now = DateTime.UtcNow;
            var notDue = await SeedAsync(new Drained(Guid.NewGuid()), state: m => m.NextAttemptAt = now.AddMinutes(5));
            var parked = await SeedAsync(new Drained(Guid.NewGuid()), state: m => m.ParkedAt = now);
            var processed = await SeedAsync(new Drained(Guid.NewGuid()), state: m => m.ProcessedAt = now);

            await DrainAsync(s => s.AddEventHandler<FirstHandler>());

            _log.Runs.Should().BeEmpty();
            (await RowAsync(notDue)).ProcessedAt.Should().BeNull();
            (await RowAsync(parked)).ProcessedAt.Should().BeNull();
            (await RowAsync(processed)).Attempts.Should().Be(0);
        }

        [Fact]
        public async Task Run_OneHandlerFails_TheRowRetriesInAMinuteAndOnlyTheFailedWorkIsRolledBack()
        {
            _log.SecondFailuresLeft = 1;
            var userId = Guid.NewGuid();
            var id = await SeedAsync(new Drained(userId));
            var before = DateTime.UtcNow;

            await DrainAsync(FirstAndSecond);

            var row = await RowAsync(id);
            row.ProcessedAt.Should().BeNull();
            row.Attempts.Should().Be(1);
            row.LastError.Should().StartWith("second: second failed on purpose");
            row.NextAttemptAt.Should().BeCloseTo(before.AddMinutes(1), TimeSpan.FromSeconds(10));

            (await InboxHandlersAsync(id)).Should().Equal("first");
            (await UserExistsAsync(userId)).Should().BeTrue("the handler that succeeded keeps its work");
            (await UserExistsAsync(TestIds.MemberId)).Should().BeFalse("the failing handler's save was rolled back");
        }

        [Fact]
        public async Task Run_RetryOnceDue_SkipsTheHandlerThatFinishedAndCompletesTheRow()
        {
            _log.SecondFailuresLeft = 1;
            var userId = Guid.NewGuid();
            var id = await SeedAsync(new Drained(userId));
            await DrainAsync(FirstAndSecond);

            await using (var db = NewContext())
            {
                var row = await db.OutboxMessages.SingleAsync(m => m.Id == id);
                row.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
                await db.SaveChangesAsync();
            }

            await DrainAsync(FirstAndSecond);

            _log.Runs.Should().Equal($"first {userId}", "second", "second");
            (await InboxHandlersAsync(id)).Should().BeEquivalentTo("first", "second");
            (await RowAsync(id)).ProcessedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task Run_SixthFailedAttempt_ParksTheRow()
        {
            _log.SecondFailuresLeft = int.MaxValue;
            var id = await SeedAsync(new Drained(Guid.NewGuid()), state: m => m.Attempts = 5);

            await DrainAsync(s => s.AddEventHandler<SecondHandler>());

            var row = await RowAsync(id);
            row.Attempts.Should().Be(6);
            row.ParkedAt.Should().NotBeNull();
            row.ProcessedAt.Should().BeNull();
            row.LastError.Should().Contain("second failed on purpose");
        }

        [Fact]
        public async Task Run_UnknownEventType_ParksAtOnceWithoutAnAttempt()
        {
            var id = await SeedRawAsync("test.nobody-knows.v1", "{}");

            await DrainAsync(FirstAndSecond);

            var row = await RowAsync(id);
            row.ParkedAt.Should().NotBeNull();
            row.Attempts.Should().Be(0);
            row.LastError.Should().Contain("Unknown event type");
            _log.Runs.Should().BeEmpty();
        }

        [Fact]
        public async Task Run_PayloadThatDoesNotRead_ParksAtOnceWithoutAnAttempt()
        {
            var id = await SeedRawAsync(EventNames.For<Drained>(), """{"userId":"not-a-guid"}""");

            await DrainAsync(FirstAndSecond);

            var row = await RowAsync(id);
            row.ParkedAt.Should().NotBeNull();
            row.Attempts.Should().Be(0);
            row.LastError.Should().StartWith("Payload does not read");
            _log.Runs.Should().BeEmpty();
        }

        [Fact]
        public async Task Run_EventWithNoDurableHandler_IsMarkedProcessedAtOnce()
        {
            var id = await SeedAsync(new Drained(Guid.NewGuid()));

            await DrainAsync(_ => { });

            (await RowAsync(id)).ProcessedAt.Should().NotBeNull("zero handlers means zero failures, which is the silent case");
            (await InboxHandlersAsync(id)).Should().BeEmpty();
        }

        [Fact]
        public async Task Run_MoreRowsThanTheCap_TakesOnlyTheCap()
        {
            for (var i = 0; i < 3; i++)
                await SeedAsync(new Drained(Guid.NewGuid()));

            await DrainAsync(s => s.AddEventHandler<FirstHandler>(), maxMessagesPerRun: 2);

            await using var db = NewContext();
            (await db.OutboxMessages.CountAsync(m => m.ProcessedAt != null)).Should().Be(2);
            (await db.OutboxMessages.CountAsync(m => m.ProcessedAt == null)).Should().Be(1);
        }

        [Fact]
        public async Task Run_TokenAlreadyCancelled_DrainsNothing()
        {
            var id = await SeedAsync(new Drained(Guid.NewGuid()));

            await DrainAsync(FirstAndSecond, token: new CancellationToken(canceled: true));

            _log.Runs.Should().BeEmpty();
            (await RowAsync(id)).ProcessedAt.Should().BeNull();
        }

        [Fact]
        public async Task Run_OldestRowLockedByAnotherConnection_IsSkippedForTheNext()
        {
            var now = DateTime.UtcNow;
            var locked = await SeedAsync(new Drained(Guid.NewGuid()), now.AddMinutes(-2));
            var next = await SeedAsync(new Drained(Guid.NewGuid()), now.AddMinutes(-1));

            await using var other = NewContext();
            await using var holding = await other.BeginTransactionAsync();
            await other.OutboxMessages
                .FromSql($"""SELECT * FROM "OutboxMessages" WHERE "Id" = {locked} FOR UPDATE""")
                .ToListAsync();

            await DrainAsync(s => s.AddEventHandler<FirstHandler>());

            (await RowAsync(locked)).ProcessedAt.Should().BeNull("another drain holds it");
            (await RowAsync(next)).ProcessedAt.Should().NotBeNull();
        }

        [Fact]
        public async Task Run_HandlerThatCannotBeBuilt_FailsTheAttemptAndTheRowsBehindItStillDrain()
        {
            var now = DateTime.UtcNow;
            var broken = await SeedAsync(new Broken(), now.AddMinutes(-2));
            var behind = await SeedAsync(new Drained(Guid.NewGuid()), now.AddMinutes(-1));

            await DrainAsync(s => s.AddEventHandler<UnbuildableHandler>().AddEventHandler<FirstHandler>());

            var row = await RowAsync(broken);
            row.Attempts.Should().Be(1);
            row.NextAttemptAt.Should().NotBeNull();
            row.LastError.Should().StartWith("handler lookup: ");
            (await RowAsync(behind)).ProcessedAt.Should().NotBeNull("one bad row must not block the outbox");
        }

        [Fact]
        public async Task Run_HandlerThatFailsToBuildInItsOwnScope_CountsAsThatHandlersFailure()
        {
            var id = await SeedAsync(new Flaky());

            await DrainAsync(s => s.AddEventHandler<FlakyHandler>());

            var row = await RowAsync(id);
            row.Attempts.Should().Be(1);
            row.LastError.Should().StartWith("flaky: flaky build on purpose");
            row.ProcessedAt.Should().BeNull();
        }
    }
}
