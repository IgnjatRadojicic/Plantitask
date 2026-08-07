using Plantitask.Core.Common;

namespace Plantitask.Core.Entities
{
    public class ProcessedWebhookEvent : ImmutableEntity
    {
        public string EventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
    }
}
