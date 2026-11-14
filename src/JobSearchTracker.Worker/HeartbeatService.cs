namespace JobSearchTracker.Worker;

/// <summary>
/// Phase 0 placeholder. It only proves the worker process starts and stays alive.
/// The persisted PostgreSQL-backed work queue (scans, retries, Gmail polling)
/// is added in a later phase; until then this service does no job work.
/// </summary>
public sealed class HeartbeatService(ILogger<HeartbeatService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Worker started (placeholder; no scheduled work yet).");
        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                logger.LogInformation("Worker heartbeat.");
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
