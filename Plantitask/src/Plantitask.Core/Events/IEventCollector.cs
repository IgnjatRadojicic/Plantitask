
namespace Plantitask.Core.Events
{
    /// <summary>
    /// Where a service puts facts its command produced,
    /// nothing runs at Raise. The events wait for SaveChanges
    /// that commits the change and leave with it or die with if it fails.
    /// Raise the last step before SaveChanges after every check that
    /// can still produce a failure.
    /// </summary>
    public interface IEventCollector
    {
        void Raise<TEvent>(TEvent e) where TEvent : IEvent; 
    }
}
