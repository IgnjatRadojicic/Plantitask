using Microsoft.EntityFrameworkCore;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Tests.Helpers
{

    [CollectionDefinition(Name)]
    public class DbCollection : ICollectionFixture<PostgresFixture>
    {
        public const string Name = "database";
    }

    [Collection(DbCollection.Name)]
    public abstract class DbTestBase : IAsyncLifetime
    {
        private readonly PostgresFixture _fixture;

        protected DbTestBase(PostgresFixture fixture) => _fixture = fixture;

        protected ApplicationDbContext NewContext() => _fixture.NewContext();

        /// <summary>For the services that take IDbContextFactory rather than a context.</summary>
        protected IDbContextFactory<ApplicationDbContext> ContextFactory
            => new TestDbContextFactory(_fixture.Options);

        public Task InitializeAsync() => _fixture.ResetAsync();

        public Task DisposeAsync() => Task.CompletedTask;
    }

}
