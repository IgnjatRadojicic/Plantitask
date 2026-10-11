

using System.Reflection;

namespace Plantitask.Core.Events
{
    public static class EventNames
    {
        // Get event name into a readable string
        public static string For<TEvent>() where TEvent : IEvent => For(typeof(TEvent));

        public static string For(Type eventType) =>
            eventType.GetCustomAttribute<EventNameAttribute>()?.Name
              ?? throw new InvalidOperationException($"{eventType.Name} has no [EventName]");

        /// <summary>
        /// Called once at startup. A missing or duplicated name
        /// is a deploy time failure rather than a message that silently
        /// stops matching a subscription filter.
        /// </summary>
        public static void ValidateAll()
        {

            // Get all event types in Assembly that defines IEvent and keep only the ones which are concrete classes
            var events = typeof(IEvent).Assembly.GetTypes()
                .Where(t => typeof(IEvent).IsAssignableFrom(t) && !t.IsAbstract &&  !t.IsInterface)
                .ToList();

            var missing = events
                .Where(t => t.GetCustomAttribute<EventNameAttribute>() is null)
                .Select(t => t.Name)
                .ToList();

            if (missing.Count > 0)
                throw new InvalidOperationException("Events without [EventName]: " + string.Join(", ", missing));

            var duplicates = events
                .GroupBy(t =>
                t.GetCustomAttribute<EventNameAttribute>()!.Name)
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key} on {string.Join(" and ", g.Select(t => t.Name))}")
                .ToList();

            if (duplicates.Count > 0)
                throw new InvalidOperationException(
                    "Duplicate [EventName]: " + string.Join("; ", duplicates));
        }
    }
}
