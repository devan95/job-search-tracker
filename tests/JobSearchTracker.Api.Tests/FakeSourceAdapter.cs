using JobSearchTracker.Api.Data;
using JobSearchTracker.Api.Sources;

namespace JobSearchTracker.Api.Tests;

/// <summary>
/// Deterministic fake source for tests (S2.2). It plays a script: attempt n gets the n-th step, and the last step repeats.
/// A step is a result, an exception to throw, or "hang" (never completes unless the token is cancelled).
/// No network, no clock, no randomness.
/// </summary>
public sealed class FakeSourceAdapter : ISourceAdapter
{
    private readonly List<Func<CancellationToken, Task<SourceFetchResult>>> _steps = [];
    private int _calls;

    public FakeSourceAdapter(string sourceType = "fake", string method = SourceMethods.Api,
        SourcePagination pagination = SourcePagination.None, bool requiresCredentials = false)
    {
        SourceType = sourceType;
        Capabilities = new SourceCapabilities(method, pagination, requiresCredentials);
    }

    public string SourceType { get; }
    public SourceCapabilities Capabilities { get; }

    /// <summary>How many times the common path called <see cref="FetchAsync"/>.</summary>
    public int Calls => _calls;

    public FakeSourceAdapter Returns(SourceFetchResult result) => Add(_ => Task.FromResult(result));

    public FakeSourceAdapter Throws(Exception exception) => Add(_ => throw exception);

    public FakeSourceAdapter Hangs() => Add(async ct =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return SourceFetchResult.Failed("unreachable");
    });

    /// <summary>A hang that ignores the cancellation token, like a badly written adapter.</summary>
    public FakeSourceAdapter HangsIgnoringToken() => Add(async _ =>
    {
        await Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None);
        return SourceFetchResult.Failed("unreachable");
    });

    /// <summary>Hangs, and its cancellation callback blocks until released: cancelling the token must not hold up the common path.</summary>
    public FakeSourceAdapter HangsWithBlockingCancellationCallback(ManualResetEventSlim release) => Add(async ct =>
    {
        using var registration = ct.Register(() => release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
        await Task.Delay(Timeout.Infinite, CancellationToken.None);
        return SourceFetchResult.Failed("unreachable");
    });

    /// <summary>Blocks the calling thread before returning a task, ignoring the token: the worst-behaved adapter.</summary>
    public FakeSourceAdapter BlocksSynchronouslyIgnoringToken(ManualResetEventSlim release) => Add(_ =>
    {
        release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None);
        return Task.FromResult(SourceFetchResult.Success([]));
    });

    public Task<SourceFetchResult> FetchAsync(SourceRequest request, CancellationToken cancellationToken)
    {
        var index = Math.Min(Interlocked.Increment(ref _calls), _steps.Count) - 1;
        if (index < 0)
        {
            return Task.FromResult(SourceFetchResult.Success([]));
        }

        return _steps[index](cancellationToken);
    }

    public static SourceListingData Listing(string externalId, string title = "Backend Engineer") =>
        new(externalId, title, $"https://example.com/jobs/{externalId}", Description: "Synthetic description.");

    private FakeSourceAdapter Add(Func<CancellationToken, Task<SourceFetchResult>> step)
    {
        _steps.Add(step);
        return this;
    }
}

/// <summary>A clock that only moves when a test says so.</summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
