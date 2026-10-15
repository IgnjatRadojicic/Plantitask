using Plantitask.Core.Common;

namespace Plantitask.Core.Entities
{
    /// <summary>
    /// Proof that one handler finished exactly one outbox message,
    /// written in the same transaction as the handler's own work,
    /// no retry can redo it. CreatedAt is the moment it is finished
    /// </summary>
    public class InboxMessage : ImmutableEntity
    {
        public required Guid MessageId { get; init; }
        public required string Handler { get; init; }
    }
}
