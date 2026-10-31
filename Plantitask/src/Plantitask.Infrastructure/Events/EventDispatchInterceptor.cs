using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Entities;

namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Hangs on the DbContext and follows each and every save. Before the write it turns the raised events into outbox rows
    /// so they commit with the change. After the commit it runs the sync handlers.
    /// A failed save, a rollback or a transaction that ended without a commit throws the events and their rows away.
    /// </summary>
    public class EventDispatchInterceptor : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private readonly EventCollector _collector;
        private readonly IOutboxSignal _signal;
        private readonly IServiceProvider _services;
        private readonly ILogger<EventDispatchInterceptor> _logger;
        private readonly List<OutboxMessage> _addedThisSave = new();
        private bool _rowsWritten;
        private Guid? _waitingOnTransaction;

        public EventDispatchInterceptor(
            EventCollector collector,
            IOutboxSignal signal,
            IServiceProvider services,
            ILogger<EventDispatchInterceptor> logger)
        {
            _collector = collector;
            _signal = signal;
            _services = services;
            _logger = logger;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            DropEventsOfAbandonedTransaction(eventData.Context?.Database.CurrentTransaction?.TransactionId);
            _addedThisSave.Clear();

            if (eventData.Context is { } context)
            {
                foreach (var pending in _collector.TakeNotYetInOutbox())
                {
                    var message = pending.ToOutboxMessage();
                    context.Add(message);
                    _addedThisSave.Add(message);
                }
            }

            return ValueTask.FromResult(result);
        }

        public async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            var transaction = eventData.Context?.Database.CurrentTransaction;

            if (_addedThisSave.Count > 0)
            {
                _rowsWritten = true;
                _waitingOnTransaction = transaction?.TransactionId;
            }

            if (transaction == null)
                await DispatchAsync();
            return result;
        }

        public Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            DropEventsOfAbandonedTransaction(eventData.TransactionId);
            return DispatchAsync();
        }

        public Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } context)
            {
                foreach (var message in _addedThisSave)
                    context.Entry(message).State = EntityState.Detached;
            }

            _addedThisSave.Clear();
            _waitingOnTransaction = null;
            _collector.Discard();
            return Task.CompletedTask;
        }

        public Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            _rowsWritten = false;
            _waitingOnTransaction = null;
            _collector.Discard();
            return Task.CompletedTask;
        }

        private async Task DispatchAsync()
        {
            _waitingOnTransaction = null;

            // One signal per commit however many events it carried, sent before the broadcasts so
            // the drain never waits on them.
            if (_rowsWritten)
            {
                _rowsWritten = false;
                _signal.RowsCommitted();
            }

            foreach (var pending in _collector.TakeAll())
            {
                await pending.DispatchSyncAsync(_services, _logger);
            }
        }

        // A transaction disposed without a commit or rollback fires no hook at all. Seeing another
        // transaction, or none, while events still wait on the old one means it never committed.
        private void DropEventsOfAbandonedTransaction(Guid? current)
        {
            if (_waitingOnTransaction is not { } waiting || waiting == current)
                return;

            _collector.DiscardInOutbox();
            _rowsWritten = false;
            _waitingOnTransaction = null;
        }
    }
}
