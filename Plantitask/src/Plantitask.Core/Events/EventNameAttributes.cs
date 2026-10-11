
namespace Plantitask.Core.Events
{
    /// <summary>
    /// The name the event travels under. Not the c# name
    /// once a Service Bus subscribtion filters on it, a rename becomes a breaking change
    /// accross services.
    /// </summary>

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EventNameAttribute : Attribute
    {
        public EventNameAttribute(string name) => Name = name;

        public string Name { get; }
    }
}
