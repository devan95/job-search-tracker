using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Data;

/// <summary>
/// EF Core context for the Phase 1 data model: candidates, employers, company lists, search profiles, the
/// two join tables, canonical jobs and their status history, applications and their audit trail. See docs/data-model.md for the decisions behind it.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Employer> Employers => Set<Employer>();
    public DbSet<CompanyList> CompanyLists => Set<CompanyList>();
    public DbSet<CompanyListEmployer> CompanyListEmployers => Set<CompanyListEmployer>();
    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();
    public DbSet<SearchProfileCompanyList> SearchProfileCompanyLists => Set<SearchProfileCompanyList>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobStatusHistory> JobStatusHistories => Set<JobStatusHistory>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<ApplicationAuditEntry> ApplicationAuditEntries => Set<ApplicationAuditEntry>();

    private static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Candidate>(b =>
        {
            b.ToTable("candidates", t => t.HasCheckConstraint("CK_candidates_name_not_blank", "char_length(btrim(\"Name\")) > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
        });

        modelBuilder.Entity<Employer>(b =>
        {
            b.ToTable("employers", t => t.HasCheckConstraint("CK_employers_name_not_blank", "char_length(btrim(\"Name\")) > 0"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<CompanyList>(b =>
        {
            b.ToTable("company_lists", t =>
            {
                t.HasCheckConstraint("CK_company_lists_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_company_lists_default_work_modes_range", $"\"DefaultWorkModes\" BETWEEN 0 AND {WorkModesLimits.AllModes}");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.LocationLabel).HasMaxLength(CompanyList.LocationLabelMaxLength);
            b.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<CompanyListEmployer>(b =>
        {
            b.ToTable("company_list_employers");
            b.HasKey(x => new { x.CompanyListId, x.EmployerId });
            b.HasOne(x => x.CompanyList).WithMany(x => x.Employers)
                .HasForeignKey(x => x.CompanyListId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne(x => x.Employer).WithMany(x => x.CompanyLists)
                .HasForeignKey(x => x.EmployerId).OnDelete(DeleteBehavior.Restrict).IsRequired();
        });

        modelBuilder.Entity<SearchProfile>(b =>
        {
            b.ToTable("search_profiles", t =>
            {
                t.HasCheckConstraint("CK_search_profiles_name_not_blank", "char_length(btrim(\"Name\")) > 0");
                t.HasCheckConstraint("CK_search_profiles_profile_version_min", "\"ProfileVersion\" >= 1");
                t.HasCheckConstraint("CK_search_profiles_work_modes_range", $"\"WorkModes\" IS NULL OR \"WorkModes\" BETWEEN 0 AND {WorkModesLimits.AllModes}");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.HasOne(x => x.Candidate).WithMany(x => x.SearchProfiles)
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasIndex(x => new { x.CandidateId, x.NormalizedName }).IsUnique();
        });

        modelBuilder.Entity<SearchProfileCompanyList>(b =>
        {
            b.ToTable("search_profile_company_lists");
            b.HasKey(x => new { x.SearchProfileId, x.CompanyListId });
            b.HasOne(x => x.SearchProfile).WithMany(x => x.CompanyLists)
                .HasForeignKey(x => x.SearchProfileId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasOne(x => x.CompanyList).WithMany(x => x.SearchProfiles)
                .HasForeignKey(x => x.CompanyListId).OnDelete(DeleteBehavior.Restrict).IsRequired();
        });

        modelBuilder.Entity<Job>(b =>
        {
            b.ToTable("jobs", t =>
            {
                t.HasCheckConstraint("CK_jobs_title_not_blank", "char_length(btrim(\"Title\")) > 0");
                t.HasCheckConstraint("CK_jobs_status_values", $"\"Status\" IN ({InList(JobStatuses.All)})");
                t.HasCheckConstraint("CK_jobs_work_mode_values", $"\"WorkMode\" IS NULL OR \"WorkMode\" IN ({InList(JobWorkModes.All)})");
                t.HasCheckConstraint("CK_jobs_employment_type_values", $"\"EmploymentType\" IS NULL OR \"EmploymentType\" IN ({InList(JobEmploymentTypes.All)})");
                t.HasCheckConstraint("CK_jobs_experience_years_not_negative", "(\"ExperienceMinYears\" IS NULL OR \"ExperienceMinYears\" >= 0) AND (\"ExperienceMaxYears\" IS NULL OR \"ExperienceMaxYears\" >= 0)");
                t.HasCheckConstraint("CK_jobs_experience_min_not_above_max", "\"ExperienceMinYears\" IS NULL OR \"ExperienceMaxYears\" IS NULL OR \"ExperienceMinYears\" <= \"ExperienceMaxYears\"");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Title).IsRequired().HasMaxLength(JobLimits.Title);
            b.Property(x => x.NormalizedTitle).IsRequired().HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.ApplyUrl).HasMaxLength(JobLimits.Url);
            b.Property(x => x.CanonicalUrl).HasMaxLength(JobLimits.Url);
            b.Property(x => x.Location).HasMaxLength(JobLimits.Location);
            b.Property(x => x.NormalizedLocation).HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.WorkMode).HasMaxLength(10);
            b.Property(x => x.EmploymentType).HasMaxLength(20);
            b.Property(x => x.Department).HasMaxLength(JobLimits.Department);
            b.Property(x => x.RequisitionId).HasMaxLength(JobLimits.RequisitionId);
            b.Property(x => x.NormalizedRequisitionId).HasMaxLength(NameNormalizer.MaxLength);
            b.Property(x => x.FoundOn).HasMaxLength(JobLimits.FoundOn);
            b.Property(x => x.CreatedVia).IsRequired().HasMaxLength(20);
            b.Property(x => x.Status).IsRequired().HasMaxLength(30);
            // PostgreSQL's xmin system column is the row version: two concurrent writes of the same job cannot both succeed.
            // It is a system column, so it adds no column and needs no migration.
            b.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            b.HasOne(x => x.Employer).WithMany()
                .HasForeignKey(x => x.EmployerId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            // Same employer + same requisition id is one job and cannot be overridden (OQ-P1.7).
            b.HasIndex(x => new { x.EmployerId, x.NormalizedRequisitionId }).IsUnique()
                .HasFilter("\"NormalizedRequisitionId\" IS NOT NULL");
            b.HasIndex(x => new { x.EmployerId, x.NormalizedTitle });
            b.HasIndex(x => x.CanonicalUrl);
        });

        modelBuilder.Entity<JobStatusHistory>(b =>
        {
            b.ToTable("job_status_history", t =>
            {
                t.HasCheckConstraint("CK_job_status_history_from_status_values", $"\"FromStatus\" IS NULL OR \"FromStatus\" IN ({InList(JobStatuses.All)})");
                t.HasCheckConstraint("CK_job_status_history_to_status_values", $"\"ToStatus\" IN ({InList(JobStatuses.All)})");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.FromStatus).HasMaxLength(30);
            b.Property(x => x.ToStatus).IsRequired().HasMaxLength(30);
            b.HasOne(x => x.Job).WithMany(x => x.StatusHistory)
                .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Cascade).IsRequired();
            b.HasIndex(x => new { x.JobId, x.ChangedAtUtc });
        });

        modelBuilder.Entity<Application>(b =>
        {
            b.ToTable("applications", t =>
            {
                t.HasCheckConstraint("CK_applications_status_values", $"\"Status\" IN ({InList(ApplicationStatuses.All)})");
                t.HasCheckConstraint("CK_applications_method_values", $"\"Method\" IS NULL OR \"Method\" IN ({InList(ApplicationMethods.All)})");
                // The applied fact is complete or absent: flag, method, time applied and time confirmed go together.
                t.HasCheckConstraint("CK_applications_applied_fact_complete",
                    "(\"IsApplied\" AND \"Method\" IS NOT NULL AND \"AppliedAtUtc\" IS NOT NULL AND \"AppliedConfirmedAtUtc\" IS NOT NULL) " +
                    "OR (NOT \"IsApplied\" AND \"Method\" IS NULL AND \"AppliedAtUtc\" IS NULL AND \"AppliedConfirmedAtUtc\" IS NULL)");
                // Saved and pending verification are never applied; applied, interview and offer always are.
                t.HasCheckConstraint("CK_applications_status_applied_consistent",
                    $"(\"Status\" NOT IN ({InList(ApplicationStatuses.RequireApplied)}) OR \"IsApplied\") " +
                    $"AND (\"Status\" NOT IN ({InList(ApplicationStatuses.RequireNotApplied)}) OR NOT \"IsApplied\")");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Status).IsRequired().HasMaxLength(30);
            b.Property(x => x.Method).HasMaxLength(20);
            // Same optimistic concurrency token as jobs: two concurrent changes to one application cannot both be saved.
            b.Property<uint>("xmin").HasColumnName("xmin").HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            // RESTRICT: a candidate or job that has an application cannot be hard-deleted (OQ-P1.6).
            b.HasOne(x => x.Candidate).WithMany()
                .HasForeignKey(x => x.CandidateId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne(x => x.Job).WithMany()
                .HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            // One application per candidate and canonical job, whatever source the job was discovered through.
            b.HasIndex(x => new { x.CandidateId, x.JobId }).IsUnique();
        });

        modelBuilder.Entity<ApplicationAuditEntry>(b =>
        {
            b.ToTable("application_audit", t =>
            {
                t.HasCheckConstraint("CK_application_audit_action_values", $"\"Action\" IN ({InList(ApplicationAuditActions.All)})");
                t.HasCheckConstraint("CK_application_audit_actor_values", $"\"Actor\" IN ({InList(ApplicationActors.All)})");
                t.HasCheckConstraint("CK_application_audit_new_status_values", $"\"NewStatus\" IN ({InList(ApplicationStatuses.All)})");
                t.HasCheckConstraint("CK_application_audit_old_status_values", $"\"OldStatus\" IS NULL OR \"OldStatus\" IN ({InList(ApplicationStatuses.All)})");
                t.HasCheckConstraint("CK_application_audit_reason_required",
                    $"\"Action\" NOT IN ({InList(ApplicationAuditActions.RequireReason)}) OR (\"Reason\" IS NOT NULL AND char_length(btrim(\"Reason\")) > 0)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Action).IsRequired().HasMaxLength(20);
            b.Property(x => x.Actor).IsRequired().HasMaxLength(20);
            b.Property(x => x.OldStatus).HasMaxLength(30);
            b.Property(x => x.NewStatus).IsRequired().HasMaxLength(30);
            b.Property(x => x.Reason).HasMaxLength(ApplicationLimits.Reason);
            // Audit rows are append-only; a migration trigger rejects UPDATE and DELETE. RESTRICT keeps the trail with its application.
            b.HasOne(x => x.Application).WithMany(x => x.AuditEntries)
                .HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasIndex(x => new { x.ApplicationId, x.ChangedAtUtc });
        });
    }
}
