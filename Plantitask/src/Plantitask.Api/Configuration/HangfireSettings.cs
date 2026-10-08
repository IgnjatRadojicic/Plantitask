using System.ComponentModel.DataAnnotations;

namespace Plantitask.Api.Configuration
{
    public class HangfireSettings
    {
        public const string SectionName = "Hangfire";

        /// <summary>
        /// Emails allowed to open the dashboard. Empty means nobody gets in, so an
        /// unconfigured environment locks the dashboard rather than opening it.
        /// </summary>
        public List<string> AdminEmails { get; init; } = new();

        [Range(1, 32)]
        public int WorkerCount { get; init; } = 2;
    }
}
