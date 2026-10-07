using System.ComponentModel.DataAnnotations;

namespace Plantitask.Api.Configuration
{
    public class ForwardedHeadersSettings
    {
        public const string SectionName = "ForwardedHeaders";

        /// <summary>
        /// The header the trusted hop writes the real client address into. Cloudflare sets
        /// CF-Connecting-IP and strips any client supplied copy. Azure uses X-Forwarded-For
        /// so this is deployment data and not a constant.
        /// </summary>
        [Required]
        public string ClientIpHeader { get; init; } = "X-Forwarded-For";

        /// <summary>
        /// CIDR ranges whose forwarded header is believed. Empty means believe nobody and use
        /// the real connection address so an unconfigured environment is honest rather than
        /// forgeable.
        /// </summary>
        public List<string> KnownNetworks { get; init; } = new();

        [Range(1, 8)]
        public int ForwardLimit { get; init; } = 1;
    }
}
