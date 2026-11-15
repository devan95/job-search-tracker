using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Data;

/// <summary>
/// EF Core context for the Phase 1 data model: candidates, employers, company lists, search profiles and the
/// two join tables. See docs/data-model.md for the decisions behind it.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Employer> Employers => Set<Employer>();
    public DbSet<CompanyList> CompanyLists => Set<CompanyList>();
    public DbSet<CompanyListEmployer> CompanyListEmployers => Set<CompanyListEmployer>();
    public DbSet<SearchProfile> SearchProfiles => Set<SearchProfile>();
    public DbSet<SearchProfileCompanyList> SearchProfileCompanyLists => Set<SearchProfileCompanyList>();

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
    }
}
