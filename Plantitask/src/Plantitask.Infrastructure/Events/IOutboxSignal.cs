namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Tells the drainer that a commit just wrote outbox rows, so it can start now instead of
    /// waiting for the sweep. Only a hint, a lost signal costs latency and never a message.
    /// </summary>
    public interface IOutboxSignal
    {
        void RowsCommitted();
    }
}
