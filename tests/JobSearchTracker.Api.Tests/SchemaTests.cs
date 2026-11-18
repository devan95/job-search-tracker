namespace JobSearchTracker.Api.Tests;

/// <summary>AC1/AC2: asserts on the real PostgreSQL schema produced by the migration, not on EF metadata.</summary>
public class SchemaTests : DatabaseTestBase
{
    private static readonly string[] ExpectedTables =
    [
        "application_audit", "applications", "candidates", "company_list_employers", "company_lists", "employers",
        "job_status_history", "jobs", "search_profile_company_lists", "search_profiles",
    ];

    [Fact]
    public async Task Exactly_ten_application_tables_exist_excluding_the_migration_history()
    {
        var tables = await Db.QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE' " +
            "AND table_name <> '__EFMigrationsHistory' ORDER BY table_name",
            r => r.GetString(0));

        // Compared in ordinal order: PostgreSQL's ORDER BY follows the database collation, which may ignore underscores.
        Assert.Equal(ExpectedTables.OrderBy(x => x, StringComparer.Ordinal), tables.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Migration_history_table_records_exactly_the_three_migrations()
    {
        var applied = await Db.QueryAsync("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"", r => r.GetString(0));
        Assert.Equal(new[] {"20261114120000_InitialDataModel", "20261117060000_AddJobs", "20261118040000_AddApplications"}, applied.OrderBy(x => x, StringComparer.Ordinal));
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
            "application_audit.Action character varying NO 20",
            "application_audit.Actor character varying NO 20",
            "application_audit.ApplicationId uuid NO -",
            "application_audit.ChangedAtUtc timestamp with time zone NO -",
            "application_audit.Id uuid NO -",
            "application_audit.NewAppliedAtUtc timestamp with time zone YES -",
            "application_audit.NewIsApplied boolean NO -",
            "application_audit.NewStatus character varying NO 30",
            "application_audit.OldAppliedAtUtc timestamp with time zone YES -",
            "application_audit.OldIsApplied boolean YES -",
            "application_audit.OldStatus character varying YES 30",
            "application_audit.Reason character varying YES 500",
            "applications.AppliedAtUtc timestamp with time zone YES -",
            "applications.AppliedConfirmedAtUtc timestamp with time zone YES -",
            "applications.CandidateId uuid NO -",
            "applications.CreatedAtUtc timestamp with time zone NO -",
            "applications.Id uuid NO -",
            "applications.IsApplied boolean NO -",
            "applications.JobId uuid NO -",
            "applications.Method character varying YES 20",
            "applications.Status character varying NO 30",
            "applications.UpdatedAtUtc timestamp with time zone NO -",
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
            "job_status_history.ChangedAtUtc timestamp with time zone NO -",
            "job_status_history.FromStatus character varying YES 30",
            "job_status_history.Id uuid NO -",
            "job_status_history.JobId uuid NO -",
            "job_status_history.ToStatus character varying NO 30",
            "jobs.ApplyUrl character varying YES 2000",
            "jobs.CanonicalUrl character varying YES 2000",
            "jobs.CreatedAtUtc timestamp with time zone NO -",
            "jobs.CreatedVia character varying NO 20",
            "jobs.Department character varying YES 200",
            "jobs.Description text YES -",
            "jobs.EmployerId uuid NO -",
            "jobs.EmploymentType character varying YES 20",
            "jobs.ExperienceMaxYears integer YES -",
            "jobs.ExperienceMinYears integer YES -",
            "jobs.FoundOn character varying YES 200",
            "jobs.Id uuid NO -",
            "jobs.Location character varying YES 200",
            "jobs.NormalizedLocation character varying YES 200",
            "jobs.NormalizedRequisitionId character varying YES 200",
            "jobs.NormalizedTitle character varying NO 200",
            "jobs.PostedDate date YES -",
            "jobs.RequisitionId character varying YES 100",
            "jobs.Status character varying NO 30",
            "jobs.Title character varying NO 200",
            "jobs.UpdatedAtUtc timestamp with time zone NO -",
            "jobs.WorkMode character varying YES 10",
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

        Assert.Equal(
            new[] {"IX_applications_CandidateId_JobId", "IX_company_lists_NormalizedName", "IX_employers_NormalizedName", "IX_jobs_EmployerId_NormalizedRequisitionId", "IX_search_profiles_CandidateId_NormalizedName"}
                .OrderBy(x => x, StringComparer.Ordinal),
            unique.OrderBy(x => x, StringComparer.Ordinal));
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
                "application_audit -> applications RESTRICT",
                "applications -> candidates RESTRICT",
                "applications -> jobs RESTRICT",
                "company_list_employers -> company_lists CASCADE",
                "company_list_employers -> employers RESTRICT",
                "job_status_history -> jobs CASCADE",
                "jobs -> employers RESTRICT",
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
                "CK_application_audit_action_values",
                "CK_application_audit_actor_values",
                "CK_application_audit_new_status_values",
                "CK_application_audit_old_status_values",
                "CK_application_audit_reason_required",
                "CK_applications_applied_fact_complete",
                "CK_applications_method_values",
                "CK_applications_status_applied_consistent",
                "CK_applications_status_values",
                "CK_candidates_name_not_blank",
                "CK_company_lists_default_work_modes_range",
                "CK_company_lists_name_not_blank",
                "CK_employers_name_not_blank",
                "CK_job_status_history_from_status_values",
                "CK_job_status_history_to_status_values",
                "CK_jobs_employment_type_values",
                "CK_jobs_experience_min_not_above_max",
                "CK_jobs_experience_years_not_negative",
                "CK_jobs_status_values",
                "CK_jobs_title_not_blank",
                "CK_jobs_work_mode_values",
                "CK_search_profiles_name_not_blank",
                "CK_search_profiles_profile_version_min",
                "CK_search_profiles_work_modes_range",
            }.OrderBy(x => x, StringComparer.Ordinal),
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
