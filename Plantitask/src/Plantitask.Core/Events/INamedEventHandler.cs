namespace Plantitask.Core.Events
{
    /// <summary>
    /// The fixed name a durable handler is recorded under in the inbox. Not the class name, so
    /// renaming the class never makes the inbox forget which messages it already finished. Not
    /// generic, so the drainer can read it without knowing which event the handler is for.
    /// </summary>
    public interface INamedEventHandler
    {
        public string Name { get; }
    }
}
