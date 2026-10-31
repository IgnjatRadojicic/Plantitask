using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plantitask.Core.Configuration;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Deletes already proccessed outbox and inbox rows older than the specified retention days.
    /// </summary>
    public class OutboxCleanupJob
    {
        private readonly ApplicationDbContext _context;
        private readonly IOptions<OutboxSettings> _outboxSettings;
        private readonly ILogger<OutboxCleanupJob> _logger;

        public OutboxCleanupJob(
            ApplicationDbContext context,
            IOptions<OutboxSettings> outboxSettings,
            ILogger<OutboxCleanupJob> logger)
        {
            _context = context;
            _outboxSettings = outboxSettings;
            _logger = logger;
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow.AddDays(-_outboxSettings.Value.RetentionDays);

            var outbox = await _context.OutboxMessages
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            var inbox = await _context.InboxMessages
                .Where(m => m.CreatedAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation(
                "Outbox cleanup deleted {OutboxRows} processed outbox rows and {InboxRows} inbox rows older than {Cutoff}",
                outbox, inbox, cutoff);
        }
    }
}