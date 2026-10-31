using Hangfire;
using Microsoft.Extensions.Logging;

namespace Plantitask.Infrastructure.Events
{
    /// <summary>
    /// Enqueues one drain run per signal. A failed enqueue is only logged, the one minute sweep
    /// picks the rows up.
    /// </summary>
    public class HangfireOutboxSignal : IOutboxSignal
    {
        private readonly IBackgroundJobClient _jobs;
        private readonly ILogger<HangfireOutboxSignal> _logger;

        public HangfireOutboxSignal(IBackgroundJobClient jobs, ILogger<HangfireOutboxSignal> logger)
        {
            _jobs = jobs;
            _logger = logger;
        }

        public void RowsCommitted()
        {
            try
            {
                // Hangfire swaps CancellationToken.None for its own shutdown token when the job runs.
                _jobs.Enqueue<OutboxDrainJob>(job => job.RunAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not enqueue an outbox drain, the sweep will pick the rows up");
            }
        }
    }
}
