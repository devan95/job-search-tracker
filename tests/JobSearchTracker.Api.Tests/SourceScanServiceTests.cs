using JobSearchTracker.Api.Data;
using JobSearchTracker.Api.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>Phase 2 S2.2: the common path, its honest outcomes, bounded timeout and retry, and persistence of scan runs and outcomes.</summary>
public class SourceScanServiceTests : SourceScanTestBase
{
    private static readonly string[] ExpectedSourceTypes = ["greenhouse", "jobboard", "lever"];

    private static readonly SourceRunPolicy Fast = new(TimeSpan.FromSeconds(5), maxAttempts: 3, baseBackoff: TimeSpan.FromSeconds(1));

    private readonly List<TimeSpan> _delays = [];
    private readonly ManualTimeProvider _clock = new(T0);

    private SourceScanService Service(AppDbContext ctx) => new(ctx, _clock, (span, _) =>
    {
        _delays.Add(span);
        _clock.Advance(span);
        return Task.CompletedTask;
    });

    private async Task<ScanRunResult> RunAsync(FakeSourceAdapter adapter, SourceRunPolicy? policy = null, Guid? employerId = null, Guid? profileId = null)
    {
        await using var ctx = Db.CreateContext();
        return await Service(ctx).RunAsync(profileId, ScanTriggers.Manual, [new SourceTask(adapter, new SourceRequest(employerId))], policy ?? Fast);
    }

    // ----- the five outcomes -----

    [Fact]
    public async Task Success_is_persisted_with_its_listing_count_and_the_run_finishes_as_success()
    {
        var employer = await AddEmployerAsync();
        var adapter = new FakeSourceAdapter("greenhouse").Returns(SourceFetchResult.Success(
            [FakeSourceAdapter.Listing("1"), FakeSourceAdapter.Listing("2")], rejected: 1));

        var result = await RunAsync(adapter, employerId: employer.Id);

        Assert.Equal(ScanOutcomes.Success, result.Outcome);
        await using var ctx = Db.CreateContext();
        var run = await ctx.ScanRuns.SingleAsync();
        Assert.Equal(ScanOutcomes.Success, run.Outcome);
        Assert.Equal(ScanTriggers.Manual, run.Trigger);
        Assert.NotNull(run.FinishedAtUtc);
        var outcome = await ctx.SourceOutcomes.SingleAsync();
        Assert.Equal(run.Id, outcome.ScanRunId);
        Assert.Equal("greenhouse", outcome.SourceType);
        Assert.Equal(SourceMethods.Api, outcome.Method);
        Assert.Equal(employer.Id, outcome.EmployerId);
        Assert.Equal(2, outcome.ListingsFound);
        Assert.Equal(1, outcome.ListingsRejected);
        Assert.Equal(1, outcome.Attempts);
        Assert.Null(outcome.ErrorText);
    }

    [Fact]
    public async Task An_empty_success_is_a_real_zero_and_stays_success()
    {
        var result = await RunAsync(new FakeSourceAdapter().Returns(SourceFetchResult.Success([])));

        Assert.Equal(ScanOutcomes.Success, result.Outcome);
        Assert.Empty(result.Sources.Single().Listings);
    }

    [Fact]
    public async Task Partial_keeps_the_listings_read_and_records_error_and_next_step()
    {
        var adapter = new FakeSourceAdapter().Returns(SourceFetchResult.Partial(
            [FakeSourceAdapter.Listing("1")], "Page 2 failed with HTTP 500.", "Run the scan again later."));

        var result = await RunAsync(adapter);

        Assert.Equal(ScanOutcomes.Partial, result.Outcome);
        var check = result.Sources.Single();
        Assert.Single(check.Listings);
        await using var ctx = Db.CreateContext();
        var outcome = await ctx.SourceOutcomes.SingleAsync();
        Assert.Equal(ScanOutcomes.Partial, outcome.Outcome);
        Assert.Equal(1, outcome.ListingsFound);
        Assert.Equal("Page 2 failed with HTTP 500.", outcome.ErrorText);
        Assert.Equal("Run the scan again later.", outcome.RecommendedNextStep);
    }

    [Fact]
    public async Task Failed_unsupported_and_not_checked_are_persisted_with_no_listings_and_a_reason()
    {
        foreach (var result in new[]
        {
            SourceFetchResult.Failed("HTTP 404 from the board.", "Check the board id."),
            SourceFetchResult.Unsupported("Unknown ATS.", "Add the jobs manually."),
            SourceFetchResult.NotChecked("Source disabled.", "Enable it in source settings."),
        })
        {
            await RunAsync(new FakeSourceAdapter().Returns(result));
        }

        await using var ctx = Db.CreateContext();
        var outcomes = await ctx.SourceOutcomes.OrderBy(o => o.Outcome).ToListAsync();
        Assert.Equal(new[] { ScanOutcomes.Failed, ScanOutcomes.NotChecked, ScanOutcomes.Unsupported }, outcomes.Select(o => o.Outcome));
        Assert.All(outcomes, o =>
        {
            Assert.Equal(0, o.ListingsFound);
            Assert.False(string.IsNullOrWhiteSpace(o.ErrorText));
            Assert.False(string.IsNullOrWhiteSpace(o.RecommendedNextStep));
        });
    }

    [Fact]
    public async Task An_adapter_that_returns_listings_with_a_failed_outcome_cannot_make_the_common_path_import_them()
    {
        // Failed has no listings by construction; the common path also drops any that arrive with a non-read outcome.
        var result = await RunAsync(new FakeSourceAdapter().Returns(SourceFetchResult.Failed("boom")));

        Assert.Empty(result.Sources.Single().Listings);
    }

    // ----- link-only and email-derived are never "searched" -----

    [Theory]
    [InlineData(SourceMethods.LinkOnly, ScanOutcomes.Success)]
    [InlineData(SourceMethods.LinkOnly, ScanOutcomes.Partial)]
    [InlineData(SourceMethods.EmailDerived, ScanOutcomes.Success)]
    [InlineData(SourceMethods.EmailDerived, ScanOutcomes.Partial)]
    public async Task A_link_only_or_email_derived_source_that_claims_success_or_partial_is_recorded_as_not_checked(string method, string claimed)
    {
        var claim = claimed == ScanOutcomes.Success
            ? SourceFetchResult.Success([FakeSourceAdapter.Listing("1")])
            : SourceFetchResult.Partial([FakeSourceAdapter.Listing("1")], "half");
        var adapter = new FakeSourceAdapter("board", method).Returns(claim);

        var result = await RunAsync(adapter);

        Assert.Equal(ScanOutcomes.NotChecked, result.Outcome);
        var check = result.Sources.Single();
        Assert.Equal(ScanOutcomes.NotChecked, check.Outcome);
        Assert.Empty(check.Listings);
        Assert.False(string.IsNullOrWhiteSpace(check.ErrorText));
        await using var ctx = Db.CreateContext();
        var stored = await ctx.SourceOutcomes.SingleAsync();
        Assert.Equal(ScanOutcomes.NotChecked, stored.Outcome);
        Assert.Equal(method, stored.Method);
        Assert.Equal(0, stored.ListingsFound);
    }

    [Fact]
    public async Task A_link_only_source_that_honestly_says_not_checked_is_kept_as_it_is()
    {
        var adapter = new FakeSourceAdapter("board", SourceMethods.LinkOnly)
            .Returns(SourceFetchResult.NotChecked("Link only.", "Open the search link."));

        var result = await RunAsync(adapter);

        Assert.Equal(ScanOutcomes.NotChecked, result.Outcome);
        Assert.Equal("Open the search link.", result.Sources.Single().RecommendedNextStep);
    }

    // ----- timeout and retry -----

    [Fact]
    public async Task A_retryable_failure_is_retried_with_exponential_backoff_until_it_succeeds()
    {
        var adapter = new FakeSourceAdapter()
            .Returns(SourceFetchResult.Failed("HTTP 503", retryable: true))
            .Returns(SourceFetchResult.Failed("HTTP 503", retryable: true))
            .Returns(SourceFetchResult.Success([FakeSourceAdapter.Listing("1")]));

        var result = await RunAsync(adapter);

        Assert.Equal(ScanOutcomes.Success, result.Outcome);
        Assert.Equal(3, adapter.Calls);
        Assert.Equal(3, result.Sources.Single().Attempts);
        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) }, _delays);
        await using var ctx = Db.CreateContext();
        Assert.Equal(3, (await ctx.SourceOutcomes.SingleAsync()).Attempts);
    }

    [Fact]
    public async Task Retries_stop_at_the_cap_and_the_last_failure_is_recorded()
    {
        var adapter = new FakeSourceAdapter().Returns(SourceFetchResult.Failed("HTTP 503", "Try later.", retryable: true));

        var result = await RunAsync(adapter, new SourceRunPolicy(TimeSpan.FromSeconds(5), maxAttempts: 3));

        Assert.Equal(3, adapter.Calls);
        Assert.Equal(ScanOutcomes.Failed, result.Outcome);
        Assert.Equal(3, result.Sources.Single().Attempts);
        Assert.Equal(2, _delays.Count);
    }

    [Fact]
    public async Task A_failure_that_is_not_retryable_is_tried_once()
    {
        var adapter = new FakeSourceAdapter().Returns(SourceFetchResult.Failed("HTTP 404", retryable: false));

        var result = await RunAsync(adapter);

        Assert.Equal(1, adapter.Calls);
        Assert.Equal(1, result.Sources.Single().Attempts);
        Assert.Empty(_delays);
    }

    [Fact]
    public async Task Unsupported_and_not_checked_are_never_retried()
    {
        var unsupported = new FakeSourceAdapter().Returns(SourceFetchResult.Unsupported("no"));
        var notChecked = new FakeSourceAdapter().Returns(SourceFetchResult.NotChecked("no"));

        await RunAsync(unsupported);
        await RunAsync(notChecked);

        Assert.Equal(1, unsupported.Calls);
        Assert.Equal(1, notChecked.Calls);
    }

    [Fact]
    public void The_policy_is_clamped_to_hard_caps_so_nothing_can_wait_or_retry_without_limit()
    {
        var wild = new SourceRunPolicy(TimeSpan.FromDays(1), maxAttempts: 1000, baseBackoff: TimeSpan.FromHours(1));
        var low = new SourceRunPolicy(TimeSpan.Zero, maxAttempts: 0, baseBackoff: TimeSpan.FromSeconds(-5));

        Assert.Equal(SourceRunPolicy.TimeoutCap, wild.TimeoutPerAttempt);
        Assert.Equal(SourceRunPolicy.MaxAttemptsCap, wild.MaxAttempts);
        Assert.Equal(SourceRunPolicy.BackoffCap, wild.BaseBackoff);
        Assert.Equal(SourceRunPolicy.BackoffCap, wild.BackoffBefore(5));
        Assert.True(low.TimeoutPerAttempt > TimeSpan.Zero);
        Assert.Equal(1, low.MaxAttempts);
        Assert.Equal(TimeSpan.Zero, low.BaseBackoff);
    }

    [Fact]
    public async Task A_source_that_never_answers_times_out_is_retried_up_to_the_cap_and_ends_failed()
    {
        var adapter = new FakeSourceAdapter().Hangs();

        var result = await RunAsync(adapter, new SourceRunPolicy(TimeSpan.FromMilliseconds(50), maxAttempts: 2));

        Assert.Equal(ScanOutcomes.Failed, result.Outcome);
        Assert.Equal(2, adapter.Calls);
        var check = result.Sources.Single();
        Assert.Equal(2, check.Attempts);
        Assert.Contains("did not answer", check.ErrorText);
    }

    [Fact]
    public async Task A_source_that_ignores_its_cancellation_token_is_still_bounded_by_the_timeout()
    {
        var adapter = new FakeSourceAdapter().HangsIgnoringToken();

        var result = await RunAsync(adapter, new SourceRunPolicy(TimeSpan.FromMilliseconds(50), maxAttempts: 1));

        Assert.Equal(ScanOutcomes.Failed, result.Outcome);
        Assert.Equal(1, adapter.Calls);
    }

    [Fact]
    public async Task A_source_that_blocks_before_returning_a_task_is_still_bounded_by_the_timeout()
    {
        using var release = new ManualResetEventSlim();
        var adapter = new FakeSourceAdapter().BlocksSynchronouslyIgnoringToken(release);

        try
        {
            var result = await RunAsync(adapter, new SourceRunPolicy(TimeSpan.FromMilliseconds(50), maxAttempts: 1));

            Assert.Equal(ScanOutcomes.Failed, result.Outcome);
            Assert.Equal(1, adapter.Calls);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task A_blocking_cancellation_callback_does_not_hold_up_the_failed_result()
    {
        using var release = new ManualResetEventSlim();
        var adapter = new FakeSourceAdapter().HangsWithBlockingCancellationCallback(release);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var result = await RunAsync(adapter, new SourceRunPolicy(TimeSpan.FromMilliseconds(50), maxAttempts: 1));

            Assert.Equal(ScanOutcomes.Failed, result.Outcome);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"The run took {watch.Elapsed}.");
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task An_exception_is_recorded_as_failed_with_the_type_name_only()
    {
        var adapter = new FakeSourceAdapter().Throws(new InvalidOperationException("GET https://x/?token=SECRET123 failed"));

        var result = await RunAsync(adapter);

        Assert.Equal(ScanOutcomes.Failed, result.Outcome);
        var check = result.Sources.Single();
        Assert.Contains("InvalidOperationException", check.ErrorText);
        Assert.DoesNotContain("SECRET123", check.ErrorText);
        await using var ctx = Db.CreateContext();
        Assert.DoesNotContain("SECRET123", (await ctx.SourceOutcomes.SingleAsync()).ErrorText);
    }

    [Fact]
    public async Task A_stop_request_from_the_caller_propagates_and_saves_nothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var adapter = new FakeSourceAdapter().Hangs();
        await using var ctx = Db.CreateContext();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(ctx).RunAsync(null, ScanTriggers.Manual, [new SourceTask(adapter, new SourceRequest(null))], Fast, cts.Token));

        Assert.Equal(0, await CountAsync("scan_runs"));
        Assert.Equal(0, await CountAsync("source_outcomes"));
    }

    // ----- failed or partial never changes job state -----

    [Theory]
    [InlineData(ScanOutcomes.Failed)]
    [InlineData(ScanOutcomes.Partial)]
    public async Task A_failed_or_partial_scan_leaves_jobs_listings_and_history_unchanged(string outcome)
    {
        var employer = await AddEmployerAsync();
        var job = await AddJobAsync(employer);
        var listing = await AddListingAsync(job);
        var before = await SnapshotAsync();

        var adapter = new FakeSourceAdapter("greenhouse").Returns(outcome == ScanOutcomes.Failed
            ? SourceFetchResult.Failed("HTTP 500", retryable: false)
            : SourceFetchResult.Partial([], "Page 2 failed."));
        _clock.Advance(TimeSpan.FromDays(10)); // many days later: a lifecycle rule that closed jobs on "not seen" would trigger now
        await RunAsync(adapter, employerId: employer.Id);

        Assert.Equal(before, await SnapshotAsync());
        await using var ctx = Db.CreateContext();
        var stored = await ctx.Jobs.SingleAsync(j => j.Id == job.Id);
        Assert.Equal(JobStatuses.Open, stored.Status);
        Assert.Equal(1, await ctx.JobStatusHistories.CountAsync(h => h.JobId == job.Id));
        Assert.Equal(T0, (await ctx.SourceListings.SingleAsync(l => l.Id == listing.Id)).LastSeenAtUtc);
    }

    private async Task<string> SnapshotAsync()
    {
        var jobs = await Db.QueryAsync("SELECT \"Id\"::text || '|' || \"Status\" || '|' || \"UpdatedAtUtc\"::text FROM jobs ORDER BY \"Id\"", r => r.GetString(0));
        var history = await Db.QueryAsync("SELECT \"Id\"::text || '|' || \"ToStatus\" FROM job_status_history ORDER BY \"Id\"", r => r.GetString(0));
        var listings = await Db.QueryAsync("SELECT \"Id\"::text || '|' || \"LastSeenAtUtc\"::text || '|' || \"ContentHash\" FROM source_listings ORDER BY \"Id\"", r => r.GetString(0));
        return string.Join("\n", jobs.Concat(history).Concat(listings));
    }

    // ----- runs with several sources -----

    [Fact]
    public async Task A_run_with_several_sources_records_one_outcome_each_and_combines_them()
    {
        var employer = await AddEmployerAsync();
        var ok = new FakeSourceAdapter("greenhouse").Returns(SourceFetchResult.Success([FakeSourceAdapter.Listing("1")]));
        var down = new FakeSourceAdapter("lever").Returns(SourceFetchResult.Failed("HTTP 500"));
        var board = new FakeSourceAdapter("jobboard", SourceMethods.LinkOnly).Returns(SourceFetchResult.NotChecked("Link only."));
        await using var ctx = Db.CreateContext();

        var result = await Service(ctx).RunAsync(null, ScanTriggers.Scheduled,
            [new SourceTask(ok, new SourceRequest(employer.Id)), new SourceTask(down, new SourceRequest(employer.Id)), new SourceTask(board, new SourceRequest(null))], Fast);

        Assert.Equal(ScanOutcomes.Partial, result.Outcome);
        await using var read = Db.CreateContext();
        var outcomes = await read.SourceOutcomes.OrderBy(o => o.SourceType).ToListAsync();
        Assert.Equal(ExpectedSourceTypes, outcomes.Select(o => o.SourceType));
        Assert.Null(outcomes.Single(o => o.SourceType == "jobboard").EmployerId);
        Assert.Equal(ScanTriggers.Scheduled, (await read.ScanRuns.SingleAsync()).Trigger);
    }

    [Fact]
    public async Task A_run_for_a_profile_records_the_profile()
    {
        var candidate = await AddCandidateAsync();
        Guid profileId;
        await using (var ctx = Db.CreateContext())
        {
            var profile = new SearchProfile(candidate.Id, "Core search");
            ctx.SearchProfiles.Add(profile);
            await ctx.SaveChangesAsync();
            profileId = profile.Id;
        }

        await RunAsync(new FakeSourceAdapter().Returns(SourceFetchResult.Success([])), profileId: profileId);

        await using var read = Db.CreateContext();
        Assert.Equal(profileId, (await read.ScanRuns.SingleAsync()).SearchProfileId);
    }

    [Fact]
    public async Task A_run_with_no_sources_is_not_checked_never_success()
    {
        await using var ctx = Db.CreateContext();

        var result = await Service(ctx).RunAsync(null, ScanTriggers.Manual, [], Fast);

        Assert.Equal(ScanOutcomes.NotChecked, result.Outcome);
        Assert.Equal(1, await CountAsync("scan_runs"));
    }

    [Theory]
    [InlineData(new[] { ScanOutcomes.Success, ScanOutcomes.Success }, ScanOutcomes.Success)]
    [InlineData(new[] { ScanOutcomes.Failed, ScanOutcomes.Failed }, ScanOutcomes.Failed)]
    [InlineData(new[] { ScanOutcomes.Success, ScanOutcomes.Failed }, ScanOutcomes.Partial)]
    [InlineData(new[] { ScanOutcomes.Success, ScanOutcomes.NotChecked }, ScanOutcomes.Partial)]
    [InlineData(new[] { ScanOutcomes.Partial, ScanOutcomes.Unsupported }, ScanOutcomes.Partial)]
    [InlineData(new[] { ScanOutcomes.Failed, ScanOutcomes.Unsupported }, ScanOutcomes.Failed)]
    [InlineData(new[] { ScanOutcomes.Unsupported, ScanOutcomes.NotChecked }, ScanOutcomes.Unsupported)]
    [InlineData(new string[0], ScanOutcomes.NotChecked)]
    public void The_overall_outcome_of_a_run_is_derived_honestly(string[] outcomes, string expected) =>
        Assert.Equal(expected, SourceScanService.Combine(outcomes));
}

/// <summary>Contract pieces that need no database.</summary>
public class SourceContractTests
{
    private static readonly string[] ExpectedOutcomes = ["success", "partial", "failed", "unsupported", "notChecked"];
    private static readonly string[] ExpectedMethods = ["api", "careerPage", "linkOnly", "emailDerived"];

    [Fact]
    public void The_registry_finds_adapters_by_source_type_and_rejects_duplicates_and_unknown_methods()
    {
        var registry = new SourceAdapterRegistry([new FakeSourceAdapter("greenhouse"), new FakeSourceAdapter("lever")]);

        Assert.Equal("lever", registry.Find("lever")!.SourceType);
        Assert.Null(registry.Find("ashby"));
        Assert.Equal(2, registry.All.Count);
        Assert.Throws<ArgumentException>(() => new SourceAdapterRegistry([new FakeSourceAdapter("a"), new FakeSourceAdapter("a")]));
        Assert.Throws<ArgumentException>(() => new SourceAdapterRegistry([new FakeSourceAdapter("a", method: "scraping")]));
        Assert.Throws<ArgumentException>(() => new SourceAdapterRegistry([new FakeSourceAdapter(" ")]));
        Assert.Empty(new SourceAdapterRegistry([]).All);
    }

    [Fact]
    public void Capabilities_declare_method_pagination_and_credentials_and_link_only_cannot_search_live()
    {
        var api = new FakeSourceAdapter("lever", SourceMethods.Api, SourcePagination.Offset, requiresCredentials: true).Capabilities;
        var link = new FakeSourceAdapter("board", SourceMethods.LinkOnly).Capabilities;
        var mail = new FakeSourceAdapter("mail", SourceMethods.EmailDerived).Capabilities;

        Assert.Equal(SourcePagination.Offset, api.Pagination);
        Assert.True(api.RequiresCredentials);
        Assert.True(api.CanSearchLive);
        Assert.False(link.CanSearchLive);
        Assert.False(mail.CanSearchLive);
    }

    [Fact]
    public void The_fake_adapter_is_deterministic_and_repeats_its_last_step()
    {
        var adapter = new FakeSourceAdapter().Returns(SourceFetchResult.Failed("first", retryable: true)).Returns(SourceFetchResult.Success([]));

        var outcomes = Enumerable.Range(0, 4)
            .Select(_ => adapter.FetchAsync(new SourceRequest(null), CancellationToken.None).Result.Outcome).ToList();

        Assert.Equal(new[] { ScanOutcomes.Failed, ScanOutcomes.Success, ScanOutcomes.Success, ScanOutcomes.Success }, outcomes);
        Assert.Equal(4, adapter.Calls);
    }

    [Fact]
    public void Safe_text_is_one_line_bounded_and_redacts_secrets()
    {
        Assert.Null(SafeText.Clean("   ", 10));
        Assert.Null(SafeText.Clean(null, 10));
        Assert.Equal("a b c", SafeText.Clean("a\n b\t\tc", 50));
        Assert.Equal("failed api_key=[redacted] and more", SafeText.Clean("failed api_key=abc123 and more", 100));
        Assert.Contains("token=[redacted]", SafeText.Clean("GET /x?token=SECRET failed", 100));
        Assert.Equal("failed Authorization=[redacted] and more", SafeText.Clean("failed Authorization: Bearer SYNTHETIC_TOKEN and more", 100));
        Assert.Equal("failed Authorization=[redacted]", SafeText.Clean("failed Authorization: Basic c3ludGhldGlj", 100));
        Assert.Equal("failed bearer=[redacted] and more", SafeText.Clean("failed Bearer SYNTHETIC_TOKEN and more", 100));
        Assert.DoesNotContain("SYNTHETIC_TOKEN", SafeText.Clean("Authorization: Bearer SYNTHETIC_TOKEN", 100));
        Assert.DoesNotContain("SYNTHETIC_SECRET", SafeText.Clean("{\"token\":\"SYNTHETIC_SECRET\"}", 100));
        Assert.DoesNotContain("SYNTHETIC_SECRET", SafeText.Clean("{'password': 'SYNTHETIC_SECRET'}", 100));
        var spaced = SafeText.Clean("{\"password\":\"SYNTHETIC SECRET words\"} failed", 100)!;
        Assert.DoesNotContain("SECRET", spaced);
        Assert.DoesNotContain("words", spaced);
        Assert.Contains("failed", spaced);
        var clipped = SafeText.Clean(new string('x', 600), 500)!;
        Assert.Equal(500, clipped.Length);
        Assert.EndsWith("…", clipped);
    }

    [Fact]
    public void Outcome_and_method_vocabularies_are_the_approved_ones()
    {
        Assert.Equal(ExpectedOutcomes, ScanOutcomes.All);
        Assert.Equal(ExpectedMethods, SourceMethods.All);
        Assert.True(ScanOutcomes.ReadListings(ScanOutcomes.Success));
        Assert.True(ScanOutcomes.ReadListings(ScanOutcomes.Partial));
        Assert.False(ScanOutcomes.ReadListings(ScanOutcomes.NotChecked));
    }
}
