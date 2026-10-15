using Plantitask.Core.Common.Interfaces;
using Plantitask.Core.Events;

namespace Plantitask.Core.Entities
{
    /// <summary>
    /// One raised event owed to its durable handlers while being written in the same transaction
    /// as the change that raised it. No soft delete, retention removes processed rows for good.
    /// Id is generated once and later becomes the Service Bus MessageId on Azure.
    /// </summary>
    public class OutboxMessage : IEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedAt { get; set; }
        public required string EventType { get; init; }
        public required string Payload { get; init; }

        // Envelope, it's one column per EventContext field. Fixed at insert
        public required DateTime OccurredAt { get; init; }
        public required EventSource Source { get; init; }
        public Guid? ActorId { get; init; }
        public string? ActorName { get; init; }
        public string? ActorEmail { get; init; }
        public string? TraceParent { get; init; }

        // The delivery state, the only thing the drainer changes.
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public DateTime? ParkedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
    }
}
