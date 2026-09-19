using Microsoft.EntityFrameworkCore;
using Npgsql;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Tests.Helpers
{
    public class PostgresFixture : IAsyncLifetime
    {
        private const string DbName = "plantitask_test";
        private const string SeedSchema = "test_seed";

        private string[] _truncated = [];
        private string[] _seeded = [];

        private static string Base =>
            Environment.GetEnvironmentVariable("PLANTITASK_TEST_DB")
            ?? throw new InvalidOperationException(
                "PLANTITASK_TEST_DB is not set. Expected a connection string WITHOUT a Database= entry.");
        public DbContextOptions<ApplicationDbContext> Options { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            await using (var admin = new NpgsqlConnection($"{Base};Database=postgres"))
            {
                await admin.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                     $"DROP DATABASE IF EXISTS {DbName} WITH (FORCE); CREATE DATABASE {DbName};", admin);
                await cmd.ExecuteNonQueryAsync();
            }
            Options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql($"{Base};Database={DbName}")
                .Options;

            await using var db = NewContext();
            await db.Database.MigrateAsync();

            _truncated = db.Model.GetEntityTypes()
                .Where(e => e.ClrType.Namespace != typeof(TaskStatusLookup).Namespace)
                .Select(e => e.GetTableName())
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct()
                .ToArray();

            _seeded = await SnapshotSeededRowsAsync(db);
        }

        public ApplicationDbContext NewContext() => new(Options);

        public async Task ResetAsync()
        {
            await using var db = NewContext();

            var restore = _seeded.Select(t => $"INSERT INTO \"{t}\" SELECT * FROM {SeedSchema}.\"{t}\";");

            await db.Database.ExecuteSqlRawAsync(
                $"TRUNCATE {string.Join(", ", _truncated.Select(t => $"\"{t}\""))} RESTART IDENTITY CASCADE; " +
                string.Join(" ", restore));
        }

        public Task DisposeAsync() => Task.CompletedTask;

        /// <summary>
        /// Some migrations seed rows into tables that are not lookups. PlanVersions is the first:
        /// it is append only catalogue data that grants point at, so it cannot live in the
        /// Lookups namespace, but truncating it would leave every entitlement lookup throwing.
        ///
        /// Whatever the migrations left in a truncated table is copied aside once, straight after
        /// migrating, and put back after every truncate. The list is derived from what the
        /// migrations actually wrote, so a table seeded by some later migration is kept without
        /// anybody having to name it here.
        /// </summary>
        private async Task<string[]> SnapshotSeededRowsAsync(ApplicationDbContext db)
        {
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA {SeedSchema};");

            var seeded = new List<string>();

            foreach (var table in _truncated)
            {
                var hasRows = await db.Database
                    .SqlQueryRaw<bool>($"SELECT EXISTS (SELECT 1 FROM \"{table}\") AS \"Value\"")
                    .SingleAsync();

                if (!hasRows)
                    continue;

                await db.Database.ExecuteSqlRawAsync(
                    $"CREATE TABLE {SeedSchema}.\"{table}\" AS TABLE \"{table}\";");

                seeded.Add(table);
            }

            return seeded.ToArray();
        }
    }
}
