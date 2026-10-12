

using Plantitask.Core.Events;

namespace Plantitask.Core.Interfaces
{
    /// <summary>
    /// Who is causing the change being made atm, and through which door.
    /// Read once when a event is raised. It's never read from a handler, the drainer has no request
    /// so the answer there is always Job. Handlers get the stored EventContext instead
    /// </summary>
    public interface ICurrentActor
    {
        EventActor? Actor { get; }
        EventSource Source { get; }
    }
}
