using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
namespace Plantitask.Infrastructure.Events
{
    public class EventDispatchInterceptor : ISaveChangesInterceptor, IDbTransactionInterceptor
    {
        private EventCollector _collector;
        private readonly IServiceProvider _services;
        private readonly ILogger<EventDispatchInterceptor> _logger;

        public EventDispatchInterceptor(
            EventCollector collector,
            IServiceProvider services,
            ILogger<EventDispatchInterceptor> logger)
        {
            _collector = collector;
            _services = services;
            _logger = logger;
        }

        public async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, 
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.Database.CurrentTransaction == null)
                await DispatchAsync();
            return result;
        }

        public Task TransactionCommittedAsync(
             DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
             => DispatchAsync();

        public Task TransactionRolledBackAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            _collector.Discard();
            return Task.CompletedTask;
        }

        public Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            _collector.Discard();
            return Task.CompletedTask;
        }

        private async Task DispatchAsync()
        {
            foreach (var pending in _collector.TakeAll())
            {
                await pending.DispatchSyncAsync(_services, _logger);
            }
        }
    }
}
