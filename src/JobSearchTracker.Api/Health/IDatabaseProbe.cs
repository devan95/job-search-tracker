using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Health;

/// <summary>Checks whether the database can be reached. Replaceable in tests.</summary>
public interface IDatabaseProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed class EfDatabaseProbe(AppDbContext db) : IDatabaseProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Timeout);
        try
        {
            return await db.Database.CanConnectAsync(cts.Token);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout or connection failure: report unavailable, never throw from a health probe.
            return false;
        }
    }
}
