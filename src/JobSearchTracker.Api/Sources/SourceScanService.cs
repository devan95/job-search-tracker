using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Sources;

/// <summary>
/// Timeout and retry bounds for one source check. The values are clamped to hard caps, so no caller (or setting) can make a
/// scan wait or retry without limit.
/// </summary>
public sealed record SourceRunPolicy
{
    public const int MaxAttemptsCap = 5;
    public static readonly TimeSpan TimeoutCap = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan BackoffCap = TimeSpan.FromSeconds(30);

    public SourceRunPolicy(TimeSpan? timeoutPerAttempt = null, int maxAttempts = 3, TimeSpan? baseBackoff = null)
    {
        var timeout = timeoutPerAttempt ?? TimeSpan.FromSeconds(30);
        TimeoutPerAttempt = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout > TimeoutCap ? TimeoutCap : timeout;
        MaxAttempts = Math.Clamp(maxAttempts, 1, MaxAttemptsCap);
        var backoff = baseBackoff ?? TimeSpan.FromSeconds(1);
        BaseBackoff = backoff < TimeSpan.Zero ? TimeSpan.Zero : backoff > BackoffCap ? BackoffCap : backoff;
    }

    public TimeSpan TimeoutPerAttempt { get; }
    public int MaxAttempts { get; }
    public TimeSpan BaseBackoff { get; }

    /// <summary>Wait before attempt number <paramref name="nextAttempt"/> (2 or more): base, 2x base, 4x base, capped.</summary>
    public TimeSpan BackoffBefore(int nextAttempt)
    {
        var factor = Math.Pow(2, Math.Max(0, nextAttempt - 2));
        var delay = TimeSpan.FromTicks((long)Math.Min(BaseBackoff.Ticks * factor, BackoffCap.Ticks));
        return delay;
    }
}

/// <summary>One source to check in a run.</summary>
public sealed record SourceTask(ISourceAdapter Adapter, SourceRequest Request);

/// <summary>The outcome of one source check after the common path has applied its rules, plus the listings to import.</summary>
public sealed record SourceCheckResult(
    string SourceType,
    string Method,
    Guid? EmployerId,
    string Outcome,
    string? ErrorText,
    string? RecommendedNextStep,
    int Attempts,
    IReadOnlyList<SourceListingData> Listings,
    int ListingsRejected);

/// <summary>What one scan produced. The listings are handed back for import (S2.8); this service never changes a job.</summary>
public sealed record ScanRunResult(Guid ScanRunId, string Outcome, IReadOnlyList<SourceCheckResult> Sources);

/// <summary>
/// The common path every source goes through: run the adapter with a bounded timeout and a capped retry, apply the honesty
/// rules, and persist a scan run with one source outcome per source.
/// <para>
/// This service writes only <c>scan_runs</c> and <c>source_outcomes</c>. It never creates, updates, closes or reopens a job or
/// a listing: importing listings into canonical jobs and the lifecycle (closing after N consecutive successful misses) belong
/// to S2.8, which must only act on Success results. A Failed or Partial outcome therefore cannot change any job state here.
/// </para>
/// </summary>
public sealed class SourceScanService(AppDbContext db, TimeProvider clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    /// <summary>How long a timed-out attempt waits for the adapter's cancellation callbacks before moving on.</summary>
    private static readonly TimeSpan CancelCleanupBound = TimeSpan.FromSeconds(1);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? ((span, ct) => Task.Delay(span, ct));

    public async Task<ScanRunResult> RunAsync(Guid? searchProfileId, string trigger, IReadOnlyList<SourceTask> tasks,
        SourceRunPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        policy ??= new SourceRunPolicy();
        var run = new ScanRun(searchProfileId, trigger, clock.GetUtcNow());
        db.ScanRuns.Add(run);

        var results = new List<SourceCheckResult>();
        foreach (var task in tasks)
        {
            var started = clock.GetUtcNow();
            var check = await CheckAsync(task, policy, cancellationToken);
            results.Add(check);
            db.SourceOutcomes.Add(new SourceOutcome(
                run.Id, check.SourceType, check.Method, check.EmployerId, check.Outcome, check.ErrorText, check.RecommendedNextStep,
                check.Listings.Count, check.ListingsRejected, check.Attempts, started, clock.GetUtcNow()));
        }

        var overall = Combine(results.Select(r => r.Outcome));
        run.Finish(overall, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return new ScanRunResult(run.Id, overall, results);
    }

    /// <summary>
    /// Overall outcome of a run. All equal: that outcome. Otherwise Partial when anything was read (Success or Partial mixed
    /// with failures), else Failed if any source failed, else Unsupported if any is unsupported, else Not checked. No sources: Not checked.
    /// </summary>
    public static string Combine(IEnumerable<string> outcomes)
    {
        var list = outcomes.ToList();
        if (list.Count == 0)
        {
            return ScanOutcomes.NotChecked;
        }

        if (list.Distinct().Count() == 1)
        {
            return list[0];
        }

        if (list.Any(ScanOutcomes.ReadListings))
        {
            return ScanOutcomes.Partial;
        }

        if (list.Contains(ScanOutcomes.Failed))
        {
            return ScanOutcomes.Failed;
        }

        return list.Contains(ScanOutcomes.Unsupported) ? ScanOutcomes.Unsupported : ScanOutcomes.NotChecked;
    }

    private async Task<SourceCheckResult> CheckAsync(SourceTask task, SourceRunPolicy policy, CancellationToken ct)
    {
        var adapter = task.Adapter;
        var method = adapter.Capabilities.Method;
        SourceFetchResult? last = null;
        var attempts = 0;

        while (attempts < policy.MaxAttempts)
        {
            attempts++;
            last = await AttemptAsync(adapter, task.Request, policy.TimeoutPerAttempt, ct);
            if (last.Outcome != ScanOutcomes.Failed || !last.Retryable || attempts >= policy.MaxAttempts)
            {
                break;
            }

            await _delay(policy.BackoffBefore(attempts + 1), ct);
        }

        return Normalize(adapter, task.Request, last!, attempts);
    }

    private static async Task<SourceFetchResult> AttemptAsync(ISourceAdapter adapter, SourceRequest request, TimeSpan timeout, CancellationToken ct)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            // Task.Run puts the call itself inside the timeout, so an adapter that blocks before its first await is bounded too.
            // WaitAsync bounds the wait even when the adapter ignores its token. A blocked thread is only freed by the adapter
            // honouring the token, so adapters must still do that.
            return await Task.Run(() => adapter.FetchAsync(request, attemptCts.Token), attemptCts.Token).WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            await CancelAttemptAsync(attemptCts);
            return SourceFetchResult.Failed($"The source did not answer within {timeout.TotalSeconds:0.#} seconds.", "Try again later.", retryable: true);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The adapter's own cancellation (for example an HttpClient timeout), not a stop request from the caller.
            return SourceFetchResult.Failed("The source request was cancelled before it finished.", "Try again later.", retryable: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type name only: exception messages can carry URLs, payload fragments or secrets.
            return SourceFetchResult.Failed($"The source adapter failed ({ex.GetType().Name}).", "Check the source settings and try again.");
        }
    }

    /// <summary>
    /// Cancels the attempt's token for cooperative adapters. CancelAsync completes only after registered callbacks finish, so the
    /// wait is bounded: a blocking callback must not hold up the Failed result or the retry.
    /// </summary>
    private static async Task CancelAttemptAsync(CancellationTokenSource attemptCts)
    {
        try
        {
            await attemptCts.CancelAsync().WaitAsync(CancelCleanupBound, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            // A callback is still running; the attempt is already over, so do not wait for it.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A misbehaving cancellation callback threw; the attempt has already failed.
        }
    }

    /// <summary>Applies the honesty rules so a stored outcome can never claim more than the adapter actually did.</summary>
    private static SourceCheckResult Normalize(ISourceAdapter adapter, SourceRequest request, SourceFetchResult result, int attempts)
    {
        var caps = adapter.Capabilities;
        var outcome = result.Outcome;
        var error = SafeText.Clean(result.ErrorText, SourceScanLimits.ErrorText);
        var nextStep = SafeText.Clean(result.RecommendedNextStep, SourceScanLimits.NextStep);
        var listings = result.Listings;
        var rejected = result.ListingsRejected;

        if (!ScanOutcomes.All.Contains(outcome))
        {
            outcome = ScanOutcomes.Failed;
            error = "The source adapter returned an unknown outcome.";
            listings = [];
        }

        // Link-only and email-derived sources never searched live: Success or Partial would be a lie.
        if (!caps.CanSearchLive && ScanOutcomes.ReadListings(outcome))
        {
            outcome = ScanOutcomes.NotChecked;
            error = "This source gives a link or reads email; it does not search live.";
            nextStep ??= "Open the link yourself and add jobs manually or by capture.";
            listings = [];
            rejected = 0;
        }

        // Only a Success or Partial outcome carries listings.
        if (!ScanOutcomes.ReadListings(outcome))
        {
            listings = [];
            rejected = 0;
        }

        if (outcome != ScanOutcomes.Success && error is null)
        {
            error = outcome switch
            {
                ScanOutcomes.Partial => "The source was only partly read.",
                ScanOutcomes.Unsupported => "This source is not supported.",
                ScanOutcomes.NotChecked => "This source was not checked.",
                _ => "The source check failed.",
            };
        }

        return new SourceCheckResult(
            SafeText.Clean(adapter.SourceType, SourceScanLimits.SourceType) ?? adapter.SourceType,
            caps.Method, request.EmployerId, outcome, error, nextStep, attempts, listings, Math.Max(0, rejected));
    }
}
