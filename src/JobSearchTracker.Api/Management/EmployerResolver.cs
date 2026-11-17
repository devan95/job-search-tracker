using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// The one find-or-create for canonical employers, shared by <c>POST /employers/resolve</c> and the company-list import.
/// A unique violation (a concurrent create of the same normalized name) returns the winner. When the caller already runs a
/// transaction, PostgreSQL would abort it on that violation, so the insert is wrapped in a savepoint that is rolled back.
/// </summary>
internal static class EmployerResolver
{
    private const string Savepoint = "resolve_employer";

    /// <param name="display">Trimmed display name (already validated).</param>
    /// <param name="normalized">Its <see cref="NameNormalizer.Normalize"/> form.</param>
    /// <returns><c>Created</c> is true only when this call inserted the employer.</returns>
    public static async Task<(Employer Employer, bool Created)> FindOrCreateAsync(
        AppDbContext db, string display, string normalized, CancellationToken ct)
    {
        var existing = await db.Employers.AsNoTracking().SingleOrDefaultAsync(e => e.NormalizedName == normalized, ct);
        if (existing is not null)
        {
            return (existing, false);
        }

        var employer = new Employer(display);
        db.Employers.Add(employer);
        var transaction = db.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(Savepoint, ct);
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Lost a race with a concurrent create of the same canonical name: return the winner.
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(Savepoint, ct);
            }

            db.Entry(employer).State = EntityState.Detached;
            var winner = await db.Employers.AsNoTracking().SingleAsync(e => e.NormalizedName == normalized, ct);
            return (winner, false);
        }

        return (employer, true);
    }
}
