using System.Reflection;
using Plantitask.Core.Events;

namespace Plantitask.Infrastructure.Events
{
    public class EventTypeRegistry
    {
        private readonly Dictionary<string, Type> _byName;

        public EventTypeRegistry(params Assembly[] assemblies)
        {
            _byName = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IEvent).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToDictionary(t => EventNames.For(t), t => t);
        }

        public Type? Find(string eventType) => _byName.GetValueOrDefault(eventType);
    }
}
