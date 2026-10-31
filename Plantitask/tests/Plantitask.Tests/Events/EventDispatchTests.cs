using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Data;
using Plantitask.Infrastructure.Events;
using Plantitask.Tests.Helpers;

namespace Plantitask.Tests.Events
{
    /// <summary>
    /// Proves the sync dispatch path against real Postgres. Everything in this machinery fails by
    /// quietly doing nothing, so a green build says nothing about it and these tests are the proof.
    /// </summary>
    public class EventDispatchTests : DbTestBase
    {
        private readonly RecordingHandler _handler;
        private readonly RecordingSignal _signal;

        public EventDispatchTests(PostgresFixture fixture) : base(fixture)
        {
            _handler = new RecordingHandler(NewContext);
            _signal = new RecordingSignal(NewContext);
        }

        [EventName("test.user-seeded.v1")]
        public record UserSeeded(Guid UserId) : IEvent;

        public sealed record Call(Guid UserId, bool CommittedWhenRun, EventContext Context);

        /// <summary>
        /// Records every call and asks a second connection whether the row exists at that moment.
        /// Another connection can only see a committed row, so this is the proof the handler ran
        /// after the commit rather than an assumption about which hook fired.
        /// </summary>
        private sealed class RecordingHandler : IEventHandler<UserSeeded>
        {
            private readonly Func<ApplicationDbContext> _newContext;

            public RecordingHandler(Func<ApplicationDbContext> newContext) => _newContext = newContext;

            public List<Call> Calls { get; } = new();

            public async Task HandleAsync(UserSeeded e, EventContext context, CancellationToken ct)
            {
                await using var other = _newContext();
                var committed = await other.Users.AnyAsync(u => u.Id == e.UserId, ct);
                Calls.Add(new Call(e.UserId, committed, context));
            }
        }

        private sealed class ThrowingHandler : IEventHandler<UserSeeded>
        {
            public Task HandleAsync(UserSeeded e, EventContext context, CancellationToken ct)
                => throw new InvalidOperationException("Handler failure on purpose");
        }

        private sealed class CountingDurableHandler : IDurableEventHandler<UserSeeded>
        {
            public string Name => "test-counter";

            public int Calls { get; private set; }

            public Task HandleAsync(UserSeeded e, EventContext context, CancellationToken ct)
            {
                Calls++;
                return Task.CompletedTask;
            }
        }

        /// <summary>Records, for every signal, how many outbox rows another connection could see.</summary>
        private sealed class RecordingSignal : IOutboxSignal
        {
            private readonly Func<ApplicationDbContext> _newContext;

            public RecordingSignal(Func<ApplicationDbContext> newContext) => _newContext = newContext;

            public List<int> RowsVisible { get; } = new();

            public void RowsCommitted()
            {
                using var other = _newContext();
                RowsVisible.Add(other.OutboxMessages.Count());
            }
        }

        private sealed class FixedActor : ICurrentActor
        {
            public EventActor? Actor => new(TestIds.LeadId, "lead", "lead@example.com");
            public EventSource Source => EventSource.Request;
        }

        /// <summary>One request's worth of wiring, shaped like Program.cs.</summary>
        private sealed record RequestScope(
            ServiceProvider Root, IServiceScope Scope, IEventCollector Events, ApplicationDbContext Db)
            : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                await Db.DisposeAsync();
                Scope.Dispose();
                await Root.DisposeAsync();
            }
        }

        // Extra handlers are registered before the recording one so a test can put a handler ahead of it.
        private RequestScope OpenScope(Action<IServiceCollection>? extraHandlers = null)
        {
            var services = new ServiceCollection()
                .AddSingleton<ICurrentActor, FixedActor>()
                .AddScoped<EventCollector>()
                .AddScoped<IEventCollector>(sp => sp.GetRequiredService<EventCollector>())
                .AddScoped<EventDispatchInterceptor>()
                .AddSingleton<IOutboxSignal>(_signal)
                .AddSingleton<ILogger<EventDispatchInterceptor>>(NullLogger<EventDispatchInterceptor>.Instance);

            extraHandlers?.Invoke(services);
            services.AddSingleton<IEventHandler<UserSeeded>>(_handler);

            var root = services.BuildServiceProvider(validateScopes: true);
            var scope = root.CreateScope();

            var interceptor = scope.ServiceProvider.GetRequiredService<EventDispatchInterceptor>();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>(ContextOptions)
                .AddInterceptors(interceptor)
                .Options;

            return new RequestScope(
                root, scope, scope.ServiceProvider.GetRequiredService<IEventCollector>(), new ApplicationDbContext(options));
        }

        [Fact]
        public async Task SaveChanges_PlainSave_RunsTheHandlerOnceAfterTheRowIsCommitted()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            var call = _handler.Calls.Should().ContainSingle().Which;
            call.UserId.Should().Be(TestIds.LeadId);
            call.CommittedWhenRun.Should().BeTrue("a second connection must already see the row");
        }

        [Fact]
        public async Task Raise_CapturesTheContextAtRaiseTime()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            var context = _handler.Calls.Should().ContainSingle().Which.Context;
            context.Source.Should().Be(EventSource.Request);
            context.Actor.Should().Be(new EventActor(TestIds.LeadId, "lead", "lead@example.com"));
            context.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Several statements can make both hooks fire for one save, the transaction commit and then
        /// SavedChanges. TakeAll is what keeps that from dispatching twice.
        /// </summary>
        [Fact]
        public async Task SaveChanges_SeveralStatements_RunsEachEventExactlyOnce()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));
            await request.Db.SaveChangesAsync();

            _handler.Calls.Should().HaveCount(2);
            _handler.Calls.Select(c => c.UserId).Should().BeEquivalentTo(new[] { TestIds.LeadId, TestIds.MemberId });
            _handler.Calls.Should().OnlyContain(c => c.CommittedWhenRun);
        }

        [Fact]
        public async Task SaveChanges_InsideExplicitTransaction_WaitsForTheCommit()
        {
            await using var request = OpenScope();
            await using var transaction = await request.Db.BeginTransactionAsync();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            _handler.Calls.Should().BeEmpty("SaveChanges inside an explicit transaction is not the commit");

            await transaction.CommitAsync();

            _handler.Calls.Should().ContainSingle().Which.CommittedWhenRun.Should().BeTrue();
        }

        [Fact]
        public async Task SaveChanges_TransactionRolledBack_NeverRunsTheHandler()
        {
            await using var request = OpenScope();

            await using (var transaction = await request.Db.BeginTransactionAsync())
            {
                request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                request.Events.Raise(new UserSeeded(TestIds.LeadId));
                await request.Db.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            request.Db.ChangeTracker.Clear();
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            await request.Db.SaveChangesAsync();

            _handler.Calls.Should().BeEmpty("the rolled back event must not ride along with a later save");
        }

        [Fact]
        public async Task SaveChanges_FailedSave_DiscardsTheEvent()
        {
            await using (var seed = NewContext())
            {
                seed.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                await seed.SaveChangesAsync();
            }

            await using var request = OpenScope();

            // Same user name and email as the seeded lead, so the unique indexes reject it.
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));

            var save = () => request.Db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();
            _handler.Calls.Should().BeEmpty();

            request.Db.ChangeTracker.Clear();
            request.Db.Users.Add(TestData.User(TestIds.OutsiderId, "outsider"));
            await request.Db.SaveChangesAsync();

            _handler.Calls.Should().BeEmpty("the failed save's event must not ride along with the next one");
        }

        [Fact]
        public async Task SaveChanges_ThrowingAndDurableHandlers_DoNotStopTheOthers()
        {
            var durable = new CountingDurableHandler();

            await using var request = OpenScope(s => s
                .AddSingleton<IEventHandler<UserSeeded>>(new ThrowingHandler())
                .AddSingleton<IEventHandler<UserSeeded>>(durable));

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));

            var save = () => request.Db.SaveChangesAsync();
            await save.Should().NotThrowAsync("a failed reaction never fails the request");

            _handler.Calls.Should().ContainSingle("the handler after the throwing one still runs");
            durable.Calls.Should().Be(0, "durable handlers wait for the outbox");
        }

        [Fact]
        public async Task SaveChanges_WritesOneOutboxRowPerEventWithItsEnvelope()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            await using var check = NewContext();
            var row = await check.OutboxMessages.SingleAsync();

            row.EventType.Should().Be("test.user-seeded.v1");
            row.Payload.Should().Contain("userId").And.Contain(TestIds.LeadId.ToString());
            row.Source.Should().Be(EventSource.Request);
            row.ActorId.Should().Be(TestIds.LeadId);
            row.ActorName.Should().Be("lead");
            row.ActorEmail.Should().Be("lead@example.com");
            row.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
            row.ProcessedAt.Should().BeNull("nothing has drained it yet");
            row.Attempts.Should().Be(0);
        }

        /// <summary>
        /// The row and the change share one transaction, so a save that fails writes neither. The
        /// next save must not write the failed one's row either, which is what detaching it is for.
        /// </summary>
        [Fact]
        public async Task SaveChanges_FailedSave_LeavesNoOutboxRowBehind()
        {
            await using (var seed = NewContext())
            {
                seed.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                await seed.SaveChangesAsync();
            }

            await using var request = OpenScope();

            // Same user name and email as the seeded lead, so the unique indexes reject it.
            var duplicate = TestData.User(TestIds.MemberId, "lead");
            request.Db.Users.Add(duplicate);
            request.Events.Raise(new UserSeeded(TestIds.MemberId));

            var save = () => request.Db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();

            // Only the rejected user is taken out. Clearing the whole tracker would hide a leaked row.
            request.Db.Entry(duplicate).State = EntityState.Detached;
            request.Db.Users.Add(TestData.User(TestIds.OutsiderId, "outsider"));
            await request.Db.SaveChangesAsync();

            await using var check = NewContext();
            (await check.OutboxMessages.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task SaveChanges_TwoSavesInOneTransaction_WriteEachEventOnce()
        {
            await using var request = OpenScope();
            await using var transaction = await request.Db.BeginTransactionAsync();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));
            await request.Db.SaveChangesAsync();

            await transaction.CommitAsync();

            await using var check = NewContext();
            var payloads = await check.OutboxMessages.Select(m => m.Payload).ToListAsync();

            payloads.Should().HaveCount(2, "the first save's event must not be written a second time");
            payloads.Should().ContainSingle(p => p.Contains(TestIds.LeadId.ToString()));
            payloads.Should().ContainSingle(p => p.Contains(TestIds.MemberId.ToString()));
        }

        [Fact]
        public async Task SaveChanges_TransactionRolledBack_LeavesNoOutboxRow()
        {
            await using var request = OpenScope();

            await using (var transaction = await request.Db.BeginTransactionAsync())
            {
                request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                request.Events.Raise(new UserSeeded(TestIds.LeadId));
                await request.Db.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            await using var check = NewContext();
            (await check.OutboxMessages.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task SaveChanges_TwoPlainSavesInOneRequest_EachWritesItsOwnEvent()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));
            await request.Db.SaveChangesAsync();

            await using var check = NewContext();

            var payloads = await check.OutboxMessages.Select(m => m.Payload).ToListAsync();

            payloads.Should().HaveCount(2);
            payloads.Should().ContainSingle(p => p.Contains(TestIds.MemberId.ToString()), "the second save's event needs its own row");

        }

        [Fact]
        public async Task SaveChanges_EventsRaised_SignalsOnceAfterTheRowsAreCommitted()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));
            await request.Db.SaveChangesAsync();

            _signal.RowsVisible.Should().ContainSingle("one signal per commit")
                .Which.Should().Be(2, "both rows must already be committed when it fires");
        }

        [Fact]
        public async Task SaveChanges_NoEventsRaised_DoesNotSignal()
        {
            await using var request = OpenScope();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            await request.Db.SaveChangesAsync();

            _signal.RowsVisible.Should().BeEmpty();
        }

        [Fact]
        public async Task SaveChanges_TwoSavesInOneTransaction_SignalOnceAtTheCommit()
        {
            await using var request = OpenScope();
            await using var transaction = await request.Db.BeginTransactionAsync();

            request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.LeadId));
            await request.Db.SaveChangesAsync();

            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));
            await request.Db.SaveChangesAsync();

            _signal.RowsVisible.Should().BeEmpty("nothing is committed yet");

            await transaction.CommitAsync();

            _signal.RowsVisible.Should().ContainSingle().Which.Should().Be(2);
        }

        [Fact]
        public async Task SaveChanges_FailedSave_DoesNotSignal()
        {
            await using (var seed = NewContext())
            {
                seed.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                await seed.SaveChangesAsync();
            }

            await using var request = OpenScope();

            // Same user name and email as the seeded lead, so the unique indexes reject it.
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "lead"));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));

            var save = () => request.Db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateException>();

            _signal.RowsVisible.Should().BeEmpty();
        }

        [Fact]
        public async Task SaveChanges_TransactionRolledBack_DoesNotSignalOnTheNextSave()
        {
            await using var request = OpenScope();

            await using (var transaction = await request.Db.BeginTransactionAsync())
            {
                request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                request.Events.Raise(new UserSeeded(TestIds.LeadId));
                await request.Db.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            request.Db.ChangeTracker.Clear();
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            await request.Db.SaveChangesAsync();

            _signal.RowsVisible.Should().BeEmpty("the rolled back rows are gone and there is nothing to drain");
        }

        [Fact]
        public async Task SaveChanges_TransactionDisposedWithoutCommit_NeverRunsTheHandler()
        {
            await using var request = OpenScope();

            await using (var transaction = await request.Db.BeginTransactionAsync())
            {
                request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                request.Events.Raise(new UserSeeded(TestIds.LeadId));
                await request.Db.SaveChangesAsync();
            }

            request.Db.ChangeTracker.Clear();
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            await request.Db.SaveChangesAsync();

            _handler.Calls.Should().BeEmpty("the abandoned transaction's event must not ride along with a later save");
            _signal.RowsVisible.Should().BeEmpty();
        }

        [Fact]
        public async Task Commit_AfterATransactionDisposedWithoutCommit_NeverRunsTheHandler()
        {
            await using var request = OpenScope();

            await using (var abandoned = await request.Db.BeginTransactionAsync())
            {
                request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                request.Events.Raise(new UserSeeded(TestIds.LeadId));
                await request.Db.SaveChangesAsync();
            }

            await using (var next = await request.Db.BeginTransactionAsync())
                await next.CommitAsync();

            _handler.Calls.Should().BeEmpty("a commit with no save of its own must not dispatch the abandoned event");
        }

        [Fact]
        public async Task SaveChanges_EventRaisedAfterAnAbandonedTransaction_StillRuns()
        {
            await using var request = OpenScope();

            await using (var transaction = await request.Db.BeginTransactionAsync())
            {
                request.Db.Users.Add(TestData.User(TestIds.LeadId, "lead"));
                request.Events.Raise(new UserSeeded(TestIds.LeadId));
                await request.Db.SaveChangesAsync();
            }

            request.Db.ChangeTracker.Clear();
            request.Db.Users.Add(TestData.User(TestIds.MemberId, "member"));
            request.Events.Raise(new UserSeeded(TestIds.MemberId));
            await request.Db.SaveChangesAsync();

            _handler.Calls.Should().ContainSingle().Which.UserId.Should().Be(TestIds.MemberId);
            _signal.RowsVisible.Should().ContainSingle().Which.Should().Be(1);

            await using var check = NewContext();
            (await check.OutboxMessages.Select(m => m.Payload).SingleAsync())
                .Should().Contain(TestIds.MemberId.ToString());
        }
    }
}
