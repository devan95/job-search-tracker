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
        Assert.Equal(19, (await TablesAsync(db)).Count);

        await db.MigrateToAsync("0");
        Assert.Empty(await TablesAsync(db));
        Assert.Empty(await db.QueryAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", r => r.GetString(0)));

        await db.MigrateToAsync(null);
        Assert.Equal(19, (await TablesAsync(db)).Count);
    }

    [Fact]
    public async Task The_source_migration_reverts_alone_and_leaves_the_sixteen_earlier_tables_and_their_data()
    {
        await using var db = await TestDatabase.CreateEmptyAsync();
        await db.MigrateToAsync(null);
        await db.ExecuteAsync("INSERT INTO employers (\"Id\", \"Name\", \"NormalizedName\") VALUES (gen_random_uuid(), 'Acme', 'acme')");

        await db.MigrateToAsync("20261118120000_AddRoleListsAndLocationSets");

        var tables = await TablesAsync(db);
        Assert.Equal(16, tables.Count);
        Assert.DoesNotContain("source_listings", tables);
        Assert.DoesNotContain("scan_runs", tables);
        Assert.DoesNotContain("source_outcomes", tables);
        Assert.Equal(1, (await db.QueryAsync("SELECT count(*)::int FROM employers", r => r.GetInt32(0))).Single());

        await db.MigrateToAsync(null);
        Assert.Equal(19, (await TablesAsync(db)).Count);
        Assert.Equal(1, (await db.QueryAsync("SELECT count(*)::int FROM employers", r => r.GetInt32(0))).Single());
    }

    [Fact]
    public async Task The_role_and_location_migration_reverts_alone_and_leaves_the_ten_earlier_tables()
    {
        await using var db = await TestDatabase.CreateEmptyAsync();
        await db.MigrateToAsync(null);

        await db.MigrateToAsync("20261118040000_AddApplications");

        var tables = await TablesAsync(db);
        Assert.Equal(10, tables.Count);
        Assert.DoesNotContain("role_lists", tables);
        Assert.DoesNotContain("role_list_titles", tables);
        Assert.DoesNotContain("location_sets", tables);
        Assert.DoesNotContain("location_set_locations", tables);
        Assert.DoesNotContain("search_profile_role_lists", tables);
        Assert.DoesNotContain("search_profile_location_sets", tables);

        await db.MigrateToAsync(null);
        Assert.Equal(19, (await TablesAsync(db)).Count);
    }

    [Fact]
    public async Task The_applications_migration_reverts_alone_and_leaves_the_eight_earlier_tables_and_no_trigger_function()
    {
        await using var db = await TestDatabase.CreateEmptyAsync();
        await db.MigrateToAsync(null);

        await db.MigrateToAsync("20261117060000_AddJobs");

        var tables = await TablesAsync(db);
        Assert.Equal(8, tables.Count);
        Assert.DoesNotContain("applications", tables);
        Assert.DoesNotContain("application_audit", tables);
        Assert.Empty(await db.QueryAsync("SELECT proname FROM pg_proc WHERE proname = 'application_audit_append_only'", r => r.GetString(0)));

        await db.MigrateToAsync(null);
        Assert.Equal(19, (await TablesAsync(db)).Count);
    }

    [Fact]
    public async Task The_jobs_migration_reverts_alone_and_leaves_the_initial_six_tables()
    {
        await using var db = await TestDatabase.CreateEmptyAsync();
        await db.MigrateToAsync(null);

        await db.MigrateToAsync("20261114120000_InitialDataModel");

        var tables = await TablesAsync(db);
        Assert.Equal(6, tables.Count);
        Assert.DoesNotContain("jobs", tables);
        Assert.DoesNotContain("job_status_history", tables);
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
