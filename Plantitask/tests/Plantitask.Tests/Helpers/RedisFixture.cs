using StackExchange.Redis;

namespace Plantitask.Tests.Helpers
{
    /// <summary>
    /// Connects to the real Redis rather than mocking IDatabase. Everything worth asserting here
    /// is Redis semantics key expiry, hash fields, set membership and a mock would only ever
    /// confirm what the mock was told to do.
    ///
    /// Isolation is a dedicated database index rather than a separate server. Redis ships with
    /// sixteen numbered databases and the app uses the default zero, so index fifteen is ours to
    /// flush between tests without touching anything the running app cares about.
    /// </summary>
    public class RedisFixture : IAsyncLifetime
    {
        private const int TestDatabaseIndex = 15;

        public IConnectionMultiplexer Connection { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            var endpoint = Environment.GetEnvironmentVariable("PLANTITASK_TEST_REDIS") ?? "localhost:6379";

            var options = ConfigurationOptions.Parse(endpoint);
            options.AllowAdmin = true;
            options.DefaultDatabase = TestDatabaseIndex;
            options.AbortOnConnectFail = true;

            Connection = await ConnectionMultiplexer.ConnectAsync(options);
        }

        public async Task FlushAsync()
        {
            var database = Connection.GetDatabase();

            // FlushDatabase is irreversible so the index is asserted immediately before it runs
            // rather than trusted from the configuration above
            if (database.Database != TestDatabaseIndex)
                throw new InvalidOperationException($"Refusing to flush Redis database {database.Database}.");

            foreach (var endpoint in Connection.GetEndPoints())
                await Connection.GetServer(endpoint).FlushDatabaseAsync(TestDatabaseIndex);
        }

        public async Task DisposeAsync() => await Connection.DisposeAsync();
    }

    [CollectionDefinition(Name)]
    public class RedisCollection : ICollectionFixture<RedisFixture>
    {
        public const string Name = "redis";
    }

    [Collection(RedisCollection.Name)]
    public abstract class RedisTestBase : IAsyncLifetime
    {
        private readonly RedisFixture _fixture;

        protected RedisTestBase(RedisFixture fixture) => _fixture = fixture;

        protected IConnectionMultiplexer Connection => _fixture.Connection;
        protected IDatabase Database => _fixture.Connection.GetDatabase();

        public Task InitializeAsync() => _fixture.FlushAsync();

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
