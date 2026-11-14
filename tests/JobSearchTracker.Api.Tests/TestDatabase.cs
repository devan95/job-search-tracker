using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JobSearchTracker.Api.Tests;

/// <summary>
/// An isolated PostgreSQL database created for one test and dropped afterwards, even when the test fails.
/// There is deliberately no skip path: when PostgreSQL is not reachable the test fails (see docs/development.md).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    public const string AdminConnectionVariable = "JOBSEARCH_TEST_ADMIN_CONNECTION";

    private readonly string _adminConnectionString;
    private bool _dropped;

    private TestDatabase(string adminConnectionString, string name, string connectionString)
    {
        _adminConnectionString = adminConnectionString;
        Name = name;
        ConnectionString = connectionString;
    }

    public string Name { get; }
    public string ConnectionString { get; }

    /// <summary>Creates an empty database with a unique name. The migration is not applied.</summary>
    public static async Task<TestDatabase> CreateEmptyAsync()
    {
        var admin = Environment.GetEnvironmentVariable(AdminConnectionVariable);
        if (string.IsNullOrWhiteSpace(admin))
        {
            throw new InvalidOperationException(
                $"Database tests need PostgreSQL. Set {AdminConnectionVariable} to a connection string for a server where " +
                "the user may create and drop databases (see docs/development.md). This test is not skipped on purpose.");
        }

        var name = $"jst_test_{Guid.NewGuid():N}";
        var testConnection = new NpgsqlConnectionStringBuilder(admin) { Database = name, Pooling = false }.ConnectionString;

        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new TestDatabase(admin, name, testConnection);
    }

    /// <summary>Creates an isolated database and applies every migration. Drops it again if that fails.</summary>
    public static async Task<TestDatabase> CreateMigratedAsync()
    {
        var database = await CreateEmptyAsync();
        try
        {
            await using var context = database.CreateContext();
            await context.Database.MigrateAsync();
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    /// <summary>Migrates to the named migration; "0" reverts every migration.</summary>
    public async Task MigrateToAsync(string? targetMigration)
    {
        await using var context = CreateContext();
        var migrator = ((IInfrastructure<IServiceProvider>)context).Instance.GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> read)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync())
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    public async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_dropped)
        {
            return;
        }

        _dropped = true;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }
}
