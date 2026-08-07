using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plantitask.Core.Interfaces;

namespace Plantitask.Infrastructure.Services
{
    public class PremiumBackgroundJob
    {
        private const int FreeMaxGroups = 5;

        private readonly IApplicationDbContext _context;
        private readonly ILogger<PremiumBackgroundJob> _logger;

        public PremiumBackgroundJob(IApplicationDbContext context, ILogger<PremiumBackgroundJob> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// One time premium used to end only when somebody asked, because the revoke lived
        /// inside GetPremiumStatusAsync. A user who never came back kept IsPremium true and
        /// MaxGroups ten forever. Features were still denied correctly since HasActivePremium
        /// computes the answer, but the stored row rotted and MaxGroups is denormalised so any
        /// code reading it directly saw ten.
        ///
        /// Recurring subscriptions are excluded on purpose. A null PremiumExpiresAt means PayPal
        /// is still charging and only a webhook may end those.
        /// </summary>
        [AutomaticRetry(Attempts = 2)]
        public async Task ExpireOneTimePremiumAsync()
        {
            var now = DateTime.UtcNow;

            var expiredCount = await _context.Users
                .Where(u => u.IsPremium
                    && u.SubscriptionType == "onetime"
                    && u.PremiumExpiresAt.HasValue
                    && u.PremiumExpiresAt.Value <= now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.IsPremium, false)
                    .SetProperty(u => u.SubscriptionType, (string?)null)
                    .SetProperty(u => u.PremiumStartedAt, (DateTime?)null)
                    .SetProperty(u => u.PremiumExpiresAt, (DateTime?)null)
                    .SetProperty(u => u.PayPalOrderId, (string?)null)
                    .SetProperty(u => u.PayPalSubscriptionId, (string?)null)
                    .SetProperty(u => u.MaxGroups, FreeMaxGroups)
                    .SetProperty(u => u.UpdatedAt, now));

            _logger.LogInformation("Expired one time premium for {Count} users", expiredCount);
        }
    }
}
