namespace JobSearchTracker.Api.Tests;

/// <summary>AC4: the migration applies to an empty database, reverts cleanly and re-applies.</summary>
[Trait("Category", "Database")]
public class MigrationTests
{
    private static Task<List<string>> TablesAsync(TestDatabase db) => db.QueryAsync(
        "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' " +
        "AND table_name <> '__EFMigrationsHistory' ORDER BY table_name",
        r => r.GetString(0));

    [Fact]
    public async Task Migration_applies_reverts_and_reapplies_on_an_isolated_database()
    {
        await using var db = await TestDatabase.CreateEmptyAsync();
        Assert.Empty(await TablesAsync(db));

        await db.MigrateToAsync(null);
        Assert.Equal(6, (await TablesAsync(db)).Count);

        await db.MigrateToAsync("0");
        Assert.Empty(await TablesAsync(db));
        Assert.Empty(await db.QueryAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", r => r.GetString(0)));

        await db.MigrateToAsync(null);
        Assert.Equal(6, (await TablesAsync(db)).Count);
    }

    [Fact]
    public async Task Parallel_isolated_databases_have_distinct_names()
    {
        var created = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => TestDatabase.CreateEmptyAsync()));
        try
        {
            Assert.Equal(3, created.Select(d => d.Name).Distinct().Count());
        }
        finally
        {
            foreach (var d in created)
            {
                await d.DisposeAsync();
            }
        }
    }
}
