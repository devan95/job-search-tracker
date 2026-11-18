using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// The one find-or-create for canonical employers, shared by <c>POST /employers/resolve</c> and the company-list import.
/// A unique violation of the employer normalized-name index (a concurrent create of the same name) returns the winner. When
/// the caller already runs a transaction, PostgreSQL would abort it on that violation, so the insert is wrapped in a savepoint
/// that is rolled back. Any other unique violation (for example a list membership key) is not handled here and propagates.
/// </summary>
internal static class EmployerResolver
{
    private const string Savepoint = "resolve_employer";

    /// <summary>The unique index on <c>employers.NormalizedName</c> (see the InitialDataModel migration).</summary>
    private const string NameIndex = "IX_employers_NormalizedName";

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
        catch (DbUpdateException ex) when (IsEmployerNameViolation(ex))
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

    private static bool IsEmployerNameViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: NameIndex };
}
