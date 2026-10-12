
namespace Plantitask.Core.Events
{
    // Marker. The dispatcher runs plain handlers immediately and parks these in the outbox.
    public interface IDurableEventHandler<in TEvent> : IEventHandler<TEvent> where TEvent : IEvent
    {
    }
}
