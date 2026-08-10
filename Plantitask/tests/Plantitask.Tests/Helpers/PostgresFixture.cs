using Microsoft.EntityFrameworkCore;
using Npgsql;
using Plantitask.Core.Entities.Lookups;
using Plantitask.Infrastructure.Data;

namespace Plantitask.Tests.Helpers
{
    public class PostgresFixture : IAsyncLifetime
    {
        private const string DbName = "plantitask_test";

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
        }

        public ApplicationDbContext NewContext() => new(Options);

        public async Task ResetAsync()
        {
            await using var db = NewContext();

            var tables = db.Model.GetEntityTypes()
                .Where(e => e.ClrType.Namespace != typeof(TaskStatusLookup).Namespace)
                .Select(e => e.GetTableName())
                .Where(name => name is not null)
                .Distinct()
                .Select(name => $"\"{name}\"");

            await db.Database.ExecuteSqlRawAsync(
                $"TRUNCATE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;");
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }
}

