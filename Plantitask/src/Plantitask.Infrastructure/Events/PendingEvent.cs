using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Events;

namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// One raised event and the context captured with it, generic
    /// so the event's real type is still known when its handlers are resolved without reflection
    /// </summary>
    public abstract class PendingEvent
    {
        public abstract Task DispatchSyncAsync(IServiceProvider services, ILogger logger);

    }
        public sealed class PendingEvent<TEvent> : PendingEvent where TEvent : IEvent
        {
            private readonly TEvent _event;
            private readonly EventContext _context;

            public PendingEvent(TEvent e, EventContext context)
            {
                _event = e;
                _context = context;
            }

            public override async Task DispatchSyncAsync(IServiceProvider services, ILogger logger)
            {
                foreach (var handler in services.GetServices<IEventHandler<TEvent>>())
                {
                    if (handler is IDurableEventHandler<TEvent>)
                        continue;

                    try
                    {
                        await handler.HandleAsync(_event, _context, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Handler {Handler} failed for {Event}",
                            handler.GetType().Name, typeof(TEvent).Name);
                    }
                }
            }
        }
    }

