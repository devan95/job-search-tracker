using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Data;

/// <summary>
/// EF Core context. No entities or migrations yet (Phase 0 skeleton); the first model arrives with Phase 1.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
