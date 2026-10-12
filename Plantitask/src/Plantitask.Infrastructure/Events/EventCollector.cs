using System.Diagnostics;
using Plantitask.Core.Events;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Holds the events raised in this scope until SaveChanges that commits them.
    /// The context is captured at Raise bacause that is the only moment the request still exists.
    /// </summary>
    public class EventCollector : IEventCollector
    {
        private readonly ICurrentActor _actor;
        private readonly List<PendingEvent> _pending = new();

        public EventCollector(ICurrentActor actor)
        {
            _actor = actor;
        }

        public void Raise<TEvent>(TEvent e) where TEvent : IEvent
        {
            var context = new EventContext
            {
                OccurredAt = DateTime.UtcNow,
                Source = _actor.Source,
                Actor = _actor.Actor,
                TraceParent = Activity.Current?.Id
            };

            _pending.Add(new PendingEvent<TEvent>(e, context));
        }

        public IReadOnlyList<PendingEvent> TakeAll()
        {
            var taken = _pending.ToList();
            _pending.Clear();
            return taken;
        }
        public void Discard() => _pending.Clear();
    }
}

