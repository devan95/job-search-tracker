namespace JobSearchTracker.Api.Tests;

/// <summary>AC1/AC2: asserts on the real PostgreSQL schema produced by the migration, not on EF metadata.</summary>
public class SchemaTests : DatabaseTestBase
{
    private static readonly string[] ExpectedTables =
    [
        "candidates", "company_list_employers", "company_lists", "employers",
        "search_profile_company_lists", "search_profiles",
    ];

    [Fact]
    public async Task Exactly_six_application_tables_exist_excluding_the_migration_history()
    {
        var tables = await Db.QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' " +
            "AND table_name <> '__EFMigrationsHistory' ORDER BY table_name",
            r => r.GetString(0));

        Assert.Equal(ExpectedTables, tables);
    }

    [Fact]
    public async Task Migration_history_table_records_exactly_the_initial_migration()
    {
        var applied = await Db.QueryAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", r => r.GetString(0));
        Assert.Equal(new[] {"20261114120000_InitialDataModel"}, applied);
    }

    [Fact]
    public async Task Columns_have_the_expected_types_and_nullability()
    {
        var columns = await Db.QueryAsync(
            "SELECT table_name || '.' || column_name || ' ' || data_type || ' ' || is_nullable || ' ' || COALESCE(character_maximum_length::text, '-') " +
            "FROM information_schema.columns WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory' " +
            "ORDER BY table_name, column_name",
            r => r.GetString(0));

        string[] expected =
        [
            "candidates.Id uuid NO -",
            "candidates.Name character varying NO 200",
            "company_list_employers.CompanyListId uuid NO -",
            "company_list_employers.EmployerId uuid NO -",
            "company_lists.DefaultWorkModes integer NO -",
            "company_lists.Id uuid NO -",
            "company_lists.LocationLabel character varying YES 200",
            "company_lists.Name character varying NO 200",
            "company_lists.NormalizedName character varying NO 200",
            "employers.Id uuid NO -",
            "employers.Name character varying NO 200",
            "employers.NormalizedName character varying NO 200",
            "search_profile_company_lists.CompanyListId uuid NO -",
            "search_profile_company_lists.SearchProfileId uuid NO -",
            "search_profiles.BroadSearchEnabled boolean NO -",
            "search_profiles.CandidateId uuid NO -",
            "search_profiles.CompanyMonitoringEnabled boolean NO -",
            "search_profiles.Id uuid NO -",
            "search_profiles.Name character varying NO 200",
            "search_profiles.NormalizedName character varying NO 200",
            "search_profiles.ProfileVersion integer NO -",
            "search_profiles.WorkModes integer YES -",
        ];

        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), columns.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Unique_indexes_enforce_normalized_names()
    {
        var unique = await Db.QueryAsync(
            "SELECT indexname FROM pg_indexes WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%' " +
            "AND indexname NOT LIKE 'PK\\_%' ORDER BY indexname",
            r => r.GetString(0));

        Assert.Equal(new[] {"IX_company_lists_NormalizedName", "IX_employers_NormalizedName", "IX_search_profiles_CandidateId_NormalizedName"},
            unique);
    }

    [Fact]
    public async Task Foreign_keys_have_the_approved_delete_rules()
    {
        var rules = await Db.QueryAsync(
            "SELECT conrelid::regclass::text || ' -> ' || confrelid::regclass::text || ' ' || " +
            "CASE confdeltype WHEN 'c' THEN 'CASCADE' WHEN 'r' THEN 'RESTRICT' WHEN 'a' THEN 'NO ACTION' ELSE confdeltype::text END " +
            "FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace ORDER BY 1",
            r => r.GetString(0));

        Assert.Equal(new[] {
                "company_list_employers -> company_lists CASCADE",
                "company_list_employers -> employers RESTRICT",
                "search_profile_company_lists -> company_lists RESTRICT",
                "search_profile_company_lists -> search_profiles CASCADE",
                "search_profiles -> candidates RESTRICT",
            },
            rules.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Check_constraints_exist()
    {
        var checks = await Db.QueryAsync(
            "SELECT conname FROM pg_constraint WHERE contype = 'c' AND connamespace = 'public'::regnamespace ORDER BY conname",
            r => r.GetString(0));

        Assert.Equal(new[] {
                "CK_candidates_name_not_blank",
                "CK_company_lists_default_work_modes_range",
                "CK_company_lists_name_not_blank",
                "CK_employers_name_not_blank",
                "CK_search_profiles_name_not_blank",
                "CK_search_profiles_profile_version_min",
                "CK_search_profiles_work_modes_range",
            },
            checks.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task No_table_stores_source_or_employer_resolution_data_that_is_deferred()
    {
        // U3/U6 deferrals: no website, career URL, ATS or per-source columns in Phase 1.
        var deferred = await Db.QueryAsync(
            "SELECT table_name || '.' || column_name FROM information_schema.columns WHERE table_schema = 'public' " +
            "AND (column_name ILIKE '%website%' OR column_name ILIKE '%career%' OR column_name ILIKE '%ats%' OR column_name ILIKE '%source%')",
            r => r.GetString(0));

        Assert.Empty(deferred);
    }
}
