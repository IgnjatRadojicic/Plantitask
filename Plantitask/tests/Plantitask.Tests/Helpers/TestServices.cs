using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Plantitask.Core.Interfaces;
using Plantitask.Infrastructure.Services;

namespace Plantitask.Tests.Helpers
{
    public static class TestServices
    {
        /// <summary>
        /// The real resolver over the context the calling service uses, with a cache of its own.
        /// Real because the plan limit is the thing GroupService and AttachmentService enforce, so
        /// stubbing it would stop those tests proving the limit comes from the catalogue. A fresh
        /// cache per call because the current plan version is cached, and a shared instance would
        /// carry a version one test published into the next.
        /// </summary>
        public static EntitlementService Entitlements(IApplicationDbContext context) =>
            new(context, new MemoryCache(new MemoryCacheOptions()), NullLogger<EntitlementService>.Instance);
    }
}
