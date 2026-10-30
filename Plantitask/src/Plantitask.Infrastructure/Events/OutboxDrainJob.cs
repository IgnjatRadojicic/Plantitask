using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Core.Entities;
using Plantitask.Core.Events;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Works through the outbox one message at a time. Each message is claimed with a row lock,
    /// each of its durable handlers runs in its own scope with its inbox row, and the outcome is
    /// written back: processed, retry later, or parked.
    /// </summary>
    public class OutboxDrainJob
    {
        private const int MaxErrorLength = 2000;

        private readonly ApplicationDbContext _context;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly EventTypeRegistry _typeRegistry;
        private readonly IOptions<OutboxSettings> _outboxSettings;
        private readonly ILogger<OutboxDrainJob> _logger;

        public OutboxDrainJob(
            ApplicationDbContext context,
            IServiceScopeFactory scopeFactory,
            EventTypeRegistry typeRegistry,
            IOptions<OutboxSettings> outboxSettings,
            ILogger<OutboxDrainJob> logger)
        {
            _context = context;
            _scopeFactory = scopeFactory;
            _typeRegistry = typeRegistry;
            _outboxSettings = outboxSettings;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            var settings = _outboxSettings.Value;

            for (var i = 0; i < settings.MaxMessagesPerRun; i++)
            {
                if (!await DrainOneAsync(settings))
                    break;
            }
        }
        // Claims a singular owed message, handles it and records the outcome. False when nothing is owed.
        private async Task<bool> DrainOneAsync(OutboxSettings settings)
        {
            var now = DateTime.UtcNow;
            await using var transaction = await _context.BeginTransactionAsync();

            // Everything that holds the decision wether a row is locked is inside this SQL.
            // LINQ written after to get a singular element or default to null if there is none.
            var message = (await _context.OutboxMessages
                .FromSql($"""
                    SELECT * FROM "OutboxMessages"
                    WHERE "ProcessedAt" IS NULL
                      AND "ParkedAt" IS NULL
                      AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {now})
                    ORDER BY "OccurredAt"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync())
                .SingleOrDefault();

            if (message == null)
            {
                await transaction.CommitAsync();
                return false;
            }

            var eventType = _typeRegistry.Find(message.EventType);

            if (eventType == null)
            {
                Park(message, now, $"Unknown event type {message.EventType}");
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            IEvent theEvent;
            try
            {
                theEvent = EventSerializer.Deserialize(message.Payload, eventType);
            }
            catch (JsonException ex)
            {
                Park(message, now, $"Payload does not read as {message.EventType}: {ex.Message}");
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            var context = new EventContext
            {
                OccurredAt = message.OccurredAt,
                Source = message.Source,
                Actor = message.ActorId is { } actorId
                    ? new EventActor(actorId, message.ActorName!, message.ActorEmail!)
                    : null,
                TraceParent = message.TraceParent,
            };

            var failures = await RunDurableHandlersAsync(message, theEvent, eventType, context);

            RecordOutcome(message, failures, settings, now);

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;

        }



        // Each durable handlers needs its own scope and transaction together with its index row
        // proviidng us with the possability  of a failing handler rollbacking itself and retry skipping
        // the finished one.
        private async Task<List<string>> RunDurableHandlersAsync(
            OutboxMessage message, IEvent theEvent, Type eventType, EventContext context)
        {
            var handlerType = typeof(IDurableEventHandler<>).MakeGenericType(eventType);

            // HandleAsync is declared on IEventHandler<T>. Asking the dirable interface finds nothing,
            // because GetMethod does not look into inherited interfaces.
            var handleMethod = typeof(IEventHandler<>).MakeGenericType(eventType)
                .GetMethod(nameof(IEventHandler<IEvent>.HandleAsync))!;

            List<string> names;

            using (var lookup = _scopeFactory.CreateScope())
            {
                names = lookup.ServiceProvider.GetServices(handlerType)
                    .Cast<INamedEventHandler>()
                    .Select(h => h.Name)
                    .ToList();
            }

            var failures = new List<string>();

            foreach (var name in names)
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // It checks whether there’s a row in the InboxMessages table for this message and this handler together
                if (await db.InboxMessages.AnyAsync(m => m.MessageId == message.Id &&
                m.Handler == name))
                    continue;

                var handler = scope.ServiceProvider.GetServices(handlerType)
                    .Cast<INamedEventHandler>()
                    .Single(h => h.Name == name);

                await using var transaction = await db.BeginTransactionAsync();

                try
                {
                    await (Task)handleMethod.Invoke(handler, [theEvent, context, CancellationToken.None])!;
                    db.InboxMessages.Add(new InboxMessage { MessageId = message.Id, Handler = name });

                    await db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;

                    failures.Add($"{name}: {cause.Message}");
                    _logger.LogWarning(cause, "Handler {Handler} failed for outbox message {MessageId}",
                        name, message.Id);
                }
            }
            return failures;
        }

        

        private void RecordOutcome(OutboxMessage message, List<string> failures, OutboxSettings settings, DateTime now)
        {
            if (failures.Count == 0)
            {
                message.ProcessedAt = now;
                return;
            }

            message.Attempts++;
            var error = string.Join("; ", failures);

            if (message.Attempts >= settings.MaxAttempts)
            {
                Park(message, now, error);
                return;
            }

            message.LastError = Truncate(error);
            message.NextAttemptAt = now + settings.EffectiveRetryDelays[message.Attempts - 1];
        }

        private void Park(OutboxMessage message, DateTime now, string reason)
        {
            message.ParkedAt = now;
            message.LastError = Truncate(reason);
            _logger.LogError("Outbox message {MessageId} parked: {Reason}", message.Id, message.LastError);
        }

        private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];

    }
}