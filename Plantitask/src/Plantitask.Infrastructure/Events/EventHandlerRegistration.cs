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
    }
}