using Microsoft.Extensions.DependencyInjection;
using Plantitask.Core.Events;

namespace Plantitask.Infrastructure.Events
{
    public static class EventHandlerRegistration
    {
        /// <summary>
        /// Registers a handler class once for every event it handles. A durable handler goes under
        /// IDurableEventHandler of that event, where the drainer looks. A sync handler goes under
        /// IEventHandler of that event, where the request looks.
        /// </summary>
        public static IServiceCollection AddEventHandler<THandler>(this IServiceCollection services)
            where THandler : class
        {
            var interfaces = typeof(THandler).GetInterfaces().Where(i => i.IsGenericType).ToList();

            var durable = interfaces
                .Where(i => i.GetGenericTypeDefinition() == typeof(IDurableEventHandler<>))
                .ToList();

            // A durable handler also implements IEventHandler of the same event through inheritance.
            // Leaving that one out keeps the request from building it only to skip it.
            var sync = interfaces
                .Where(i => i.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                .Where(i => durable.All(d => d.GenericTypeArguments[0] != i.GenericTypeArguments[0]))
                .ToList();

            if (durable.Count == 0 && sync.Count == 0)
                throw new InvalidOperationException($"{typeof(THandler).Name} handles no event");

            foreach (var service in durable.Concat(sync))
                services.AddScoped(service, typeof(THandler));

            return services;
        }

        /// <summary>
        /// Fails the boot when two durable handlers of one event share a Name. The drainer picks a
        /// handler by its name, so a duplicate would fail every attempt and park the message.
        /// </summary>
        public static void ValidateDurableHandlerNames(IServiceProvider services, IEnumerable<Type> eventTypes)
        {
            var duplicates = eventTypes
                .SelectMany(eventType => services
                    .GetServices(typeof(IDurableEventHandler<>).MakeGenericType(eventType))
                    .Cast<INamedEventHandler>()
                    .GroupBy(h => h.Name)
                    .Where(g => g.Count() > 1)
                    .Select(g => $"{g.Key} on {eventType.Name}"))
                .ToList();

            if (duplicates.Count > 0)
                throw new InvalidOperationException(
                    "Durable handlers share a name: " + string.Join("; ", duplicates));
        }
    }
}