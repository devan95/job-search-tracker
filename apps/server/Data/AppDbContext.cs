using Microsoft.EntityFrameworkCore;

namespace JobTracker.Server.Data;

// PLACEHOLDER entity so the initial migration is non-empty.
// Replace with the canonical Job schema once confirmed in Product Requirements.
public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();
}
