using System.ComponentModel.DataAnnotations;

namespace Plantitask.Core.Configuration
{
    public class OutboxSettings
    {
        public const string SectionName = "Outbox";


        /// <summary>
        /// How the outbox retries drains and cleans up. Defaults are the values decided on 2026-10-15.
        /// An environment with no outbox section runs exactly as decided.
        /// </summary>
        private static readonly TimeSpan[] DefaultRetryDelays =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(6),


        ];
        // Left empty on purpose. The configuration binder adds configured entries to whatever a
        // collection already holds, so a default list here would grow instead of being replaced.
        public List<TimeSpan> RetryDelays { get; init; } = [];

        [Range(1, 1000)]
        public int MaxMessagesPerRun { get; init; } = 100;

        [Range(1, 365)]
        public int RetentionDays { get; init; } = 7;

        /// <summary>
        /// The delay before each retry, in order. The first attempt runs at once, so a message gets
        /// one attempt plus one per delay and is parked when the last of them fails.
        /// </summary>
        public IReadOnlyList<TimeSpan> EffectiveRetryDelays =>
            RetryDelays.Count > 0 ? RetryDelays : DefaultRetryDelays;

        public int MaxAttempts => EffectiveRetryDelays.Count + 1;

        public TimeSpan TotalRetrySpan =>
            EffectiveRetryDelays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay);
    }
}

