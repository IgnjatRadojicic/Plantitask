using System.ComponentModel.DataAnnotations;

namespace Plantitask.Core.Common
{
    public class AppSettings
    {
        public const string SectionName = "App";

        [Required, Url]
        public string FrontendUrl { get; init; } = string.Empty;
    }
}
