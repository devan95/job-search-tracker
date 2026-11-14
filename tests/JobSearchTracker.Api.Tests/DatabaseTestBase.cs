using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobSearchTracker.Api.Tests;

/// <summary>
/// xUnit creates one instance per test, so every test gets its own migrated database. Tests in different classes
/// run in parallel safely because no database is shared. Dispose runs even when the test fails.
/// </summary>
[Trait("Category", "Database")]
public abstract class DatabaseTestBase : IAsyncLifetime
{
    protected TestDatabase Db { get; private set; } = null!;

    public async Task InitializeAsync() => Db = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync()
    {
        if (Db is not null)
        {
            await Db.DisposeAsync();
        }
    }

    /// <summary>Runs the action and returns the PostgreSQL SQLSTATE of the failure; fails if nothing was thrown.</summary>
    protected static async Task<string> SqlStateOfAsync(Func<Task> action)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(action);
        var postgres = ex as PostgresException ?? ex.InnerException as PostgresException;
        Assert.True(postgres is not null, $"Expected a PostgreSQL error but got: {ex}");
        return postgres.SqlState;
    }

    protected const string UniqueViolation = "23505";
    protected const string ForeignKeyViolation = "23503";
    protected const string CheckViolation = "23514";

    protected async Task<Candidate> AddCandidateAsync(string name = "Test Candidate")
    {
        await using var ctx = Db.CreateContext();
        var candidate = new Candidate(name);
        ctx.Candidates.Add(candidate);
        await ctx.SaveChangesAsync();
        return candidate;
    }
}
