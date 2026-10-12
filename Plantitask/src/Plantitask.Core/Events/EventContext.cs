
namespace Plantitask.Core.Events
{
    /// <summary>
    /// Metadata about any event, captured when it's raised.
    /// It lives on the outbox row instead of each record. Becomes Service Bus message properties later.
    /// Actor is a snapshot because audit stores names denormalised and a username can change before the drain.
    /// </summary>
    public record EventContext
    {
        public required DateTime OccurredAt { get; init; }
        public required EventSource Source { get; init; }
        // Null when nobody authenticated caused it: a webhook, a job, a login before the token exists.
        public EventActor? Actor { get; init; }
        public string? TraceParent { get; init; }
    }
        public record EventActor(Guid Id, string UserName, string Email);
        public enum EventSource
        {
            Request = 1,
            Webhook = 2,
            Job = 3,
        }
    }

