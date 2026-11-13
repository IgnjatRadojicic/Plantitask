using Plantitask.Core.Events;

namespace Plantitask.Tests.Helpers
{
    /// <summary>
    /// Stands in for the real collector in service tests, which assert what a method raised.
    /// Whether a raised event reaches the outbox is EventDispatchTests' job, not theirs.
    /// </summary>
    public class RecordingEventCollector : IEventCollector
    {
        public List<IEvent> Raised { get; } = new();

        public void Raise<TEvent>(TEvent e) where TEvent : IEvent => Raised.Add(e);
    }
}
