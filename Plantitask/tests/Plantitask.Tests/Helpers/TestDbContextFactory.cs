using Microsoft.EntityFrameworkCore;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Tests.Helpers
{
    /// <summary>
    /// AuditService takes IDbContextFactory rather than IApplicationDbContext because its writes
    /// must not ride on the request's context. Tests need to hand it something real, so this
    /// serves contexts off the same fixture options every other context in the test comes from -
    /// same database, separate connection, exactly like production.
    /// </summary>
    public class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;

        public TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;

        public ApplicationDbContext CreateDbContext() => new(_options);
    }
}
