

namespace Plantitask.Core.Events
{
    public interface IEventHandler<in TEvent> where TEvent : IEvent
    {
        Task HandleAsync(TEvent e, CancellationToken ct);
    }
}
