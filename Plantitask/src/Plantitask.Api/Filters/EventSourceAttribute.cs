using Plantitask.Core.Events;

namespace Plantitask.Api.Filters
{
    /// <summary>
    /// Marks an endpoint as a door other than a user request, so events it raises say where
    /// they come from. Only the PayPal webhook carries it today
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class EventSourceAttribute : Attribute
    {
        public EventSourceAttribute(EventSource source) => Source = source;
        
        public EventSource Source { get; }
    }
}
