using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>Phase 2 S2.1: constraints, uniqueness and delete rules of the source tables, asserted against the real PostgreSQL schema.</summary>
public class SourceDataModelTests : SourceScanTestBase
{
    // ----- source_listings -----

    [Fact]
    public async Task A_listing_round_trips_and_one_job_can_have_several_listings()
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        await AddListingAsync(job, "greenhouse", "1001");
        await AddListingAsync(job, "lever", "abc-123");

        await using var ctx = Db.CreateContext();
        var listings = await ctx.SourceListings.Where(l => l.JobId == job.Id).OrderBy(l => l.SourceType).ToListAsync();
        Assert.Equal(2, listings.Count);
        Assert.Equal("greenhouse", listings[0].SourceType);
        Assert.Equal("1001", listings[0].ExternalId);
        Assert.Equal("https://example.com/jobs/1001", listings[0].SourceUrl);
        Assert.Equal(Hash, listings[0].ContentHash);
        Assert.Equal(T0, listings[0].FirstSeenAtUtc);
        Assert.Equal(T0, listings[0].LastSeenAtUtc);
    }

    [Fact]
    public async Task The_same_source_type_and_external_id_cannot_exist_twice_even_for_another_job()
    {
        var employer = await AddEmployerAsync();
        var first = await AddJobAsync(employer, "Role A");
        var second = await AddJobAsync(employer, "Role B");
        await AddListingAsync(first, "greenhouse", "1001");

        var state = await SqlStateOfAsync(() => AddListingAsync(second, "greenhouse", "1001"));

        Assert.Equal(UniqueViolation, state);
    }

    [Fact]
    public async Task The_same_external_id_is_allowed_under_a_different_source_type()
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        await AddListingAsync(job, "greenhouse", "1001");

        await AddListingAsync(job, "lever", "1001");

        Assert.Equal(2, await CountAsync("source_listings"));
    }

    [Fact]
    public async Task A_job_that_has_a_listing_cannot_be_hard_deleted_so_no_listing_is_orphaned()
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        await AddListingAsync(job);

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM jobs WHERE \"Id\" = '{job.Id}'"));

        AssertDeleteBlocked(state);
        Assert.Equal(1, await CountAsync("source_listings"));
        Assert.Equal(1, await CountAsync("jobs"));
    }

    [Fact]
    public async Task A_listing_must_point_at_an_existing_job()
    {
        var ghost = new SourceListing(Guid.NewGuid(), "greenhouse", "1", null, Hash, null, T0);
        await using var ctx = Db.CreateContext();
        ctx.SourceListings.Add(ghost);

        var state = await SqlStateOfAsync(() => ctx.SaveChangesAsync());

        Assert.Equal(ForeignKeyViolation, state);
    }

    [Theory]
    [InlineData("NOT-A-HASH")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")] // upper case
    [InlineData("abc")]
    public async Task A_content_hash_must_be_64_lower_case_hex_characters(string hash)
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        await using var ctx = Db.CreateContext();
        ctx.SourceListings.Add(new SourceListing(job.Id, "greenhouse", "1", null, hash, null, T0));

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    [Theory]
    [InlineData("", "1")]
    [InlineData("  ", "1")]
    [InlineData("greenhouse", "")]
    [InlineData("greenhouse", "   ")]
    public async Task Source_type_and_external_id_cannot_be_blank(string sourceType, string externalId)
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        await using var ctx = Db.CreateContext();
        ctx.SourceListings.Add(new SourceListing(job.Id, sourceType, externalId, null, Hash, null, T0));

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    [Fact]
    public async Task A_source_url_must_be_http_or_https()
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        await using var ctx = Db.CreateContext();
        ctx.SourceListings.Add(new SourceListing(job.Id, "greenhouse", "1", "javascript:alert(1)", Hash, null, T0));

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    [Fact]
    public async Task Last_seen_cannot_be_before_first_seen_and_seeing_a_listing_again_keeps_first_seen()
    {
        var job = await AddJobAsync(await AddEmployerAsync());
        var listing = await AddListingAsync(job);

        await using (var ctx = Db.CreateContext())
        {
            var stored = await ctx.SourceListings.SingleAsync(l => l.Id == listing.Id);
            stored.Seen(ListingContent.Hash("changed"), null, T0.AddDays(1));
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = Db.CreateContext())
        {
            var stored = await ctx.SourceListings.SingleAsync(l => l.Id == listing.Id);
            Assert.Equal(T0, stored.FirstSeenAtUtc);
            Assert.Equal(T0.AddDays(1), stored.LastSeenAtUtc);
            Assert.Equal(ListingContent.Hash("changed"), stored.ContentHash);
        }

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"UPDATE source_listings SET \"LastSeenAtUtc\" = \"FirstSeenAtUtc\" - interval '1 second' WHERE \"Id\" = '{listing.Id}'"));
        Assert.Equal(CheckViolation, state);
    }

    // ----- scan_runs -----

    [Fact]
    public async Task A_run_without_a_profile_and_still_running_is_valid()
    {
        await using (var ctx = Db.CreateContext())
        {
            ctx.ScanRuns.Add(new ScanRun(null, ScanTriggers.BoardTest, T0));
            await ctx.SaveChangesAsync();
        }

        await using var read = Db.CreateContext();
        var run = await read.ScanRuns.SingleAsync();
        Assert.Null(run.SearchProfileId);
        Assert.Null(run.Outcome);
        Assert.Null(run.FinishedAtUtc);
    }

    [Fact]
    public async Task A_run_is_finished_exactly_when_it_has_an_overall_outcome()
    {
        var withOutcomeOnly = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO scan_runs (\"Id\", \"Trigger\", \"StartedAtUtc\", \"Outcome\") VALUES (gen_random_uuid(), 'manual', now(), 'success')"));
        var withFinishOnly = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO scan_runs (\"Id\", \"Trigger\", \"StartedAtUtc\", \"FinishedAtUtc\") VALUES (gen_random_uuid(), 'manual', now(), now())"));

        Assert.Equal(CheckViolation, withOutcomeOnly);
        Assert.Equal(CheckViolation, withFinishOnly);
    }

    [Fact]
    public async Task A_run_cannot_finish_before_it_started()
    {
        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO scan_runs (\"Id\", \"Trigger\", \"StartedAtUtc\", \"FinishedAtUtc\", \"Outcome\") " +
            "VALUES (gen_random_uuid(), 'manual', now(), now() - interval '1 minute', 'success')"));

        Assert.Equal(CheckViolation, state);
    }

    [Theory]
    [InlineData("Success")]
    [InlineData("Not checked")]
    [InlineData("maybe")]
    [InlineData("")]
    public async Task The_overall_outcome_is_limited_to_the_five_values(string outcome)
    {
        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO scan_runs (\"Id\", \"Trigger\", \"StartedAtUtc\", \"FinishedAtUtc\", \"Outcome\") VALUES (gen_random_uuid(), 'manual', now(), now(), '{outcome}')"));

        Assert.Equal(CheckViolation, state);
    }

    [Fact]
    public async Task The_trigger_is_limited_to_the_known_values()
    {
        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO scan_runs (\"Id\", \"Trigger\", \"StartedAtUtc\") VALUES (gen_random_uuid(), 'whenever', now())"));

        Assert.Equal(CheckViolation, state);
    }

    [Fact]
    public async Task Deleting_a_profile_keeps_its_scan_history_with_a_null_profile()
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

        var run = await AddFinishedRunAsync(profileId);

        await Db.ExecuteAsync($"DELETE FROM search_profiles WHERE \"Id\" = '{profileId}'");

        await using var read = Db.CreateContext();
        var kept = await read.ScanRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Null(kept.SearchProfileId);
    }

    [Fact]
    public async Task A_run_must_point_at_an_existing_profile_when_it_names_one()
    {
        await using var ctx = Db.CreateContext();
        ctx.ScanRuns.Add(new ScanRun(Guid.NewGuid(), ScanTriggers.Manual, T0));

        Assert.Equal(ForeignKeyViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    // ----- source_outcomes -----

    [Theory]
    [InlineData(ScanOutcomes.Success, 3)]
    [InlineData(ScanOutcomes.Partial, 2)]
    [InlineData(ScanOutcomes.Failed, 0)]
    [InlineData(ScanOutcomes.Unsupported, 0)]
    [InlineData(ScanOutcomes.NotChecked, 0)]
    public async Task All_five_outcomes_are_accepted(string outcome, int listings)
    {
        var run = await AddFinishedRunAsync();

        await AddOutcomeAsync(run.Id, outcome, listingsFound: listings);

        await using var ctx = Db.CreateContext();
        var stored = await ctx.SourceOutcomes.SingleAsync();
        Assert.Equal(outcome, stored.Outcome);
        Assert.Equal(listings, stored.ListingsFound);
    }

    [Theory]
    [InlineData("Success")]
    [InlineData("Not checked")]
    [InlineData("NotChecked")]
    [InlineData("ok")]
    [InlineData("")]
    public async Task An_outcome_outside_the_five_values_is_rejected(string outcome)
    {
        var run = await AddFinishedRunAsync();

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => AddOutcomeAsync(run.Id, outcome)));
    }

    [Theory]
    [InlineData(SourceMethods.LinkOnly, ScanOutcomes.Success)]
    [InlineData(SourceMethods.LinkOnly, ScanOutcomes.Partial)]
    [InlineData(SourceMethods.EmailDerived, ScanOutcomes.Success)]
    [InlineData(SourceMethods.EmailDerived, ScanOutcomes.Partial)]
    public async Task Link_only_and_email_derived_sources_can_never_be_stored_as_searched(string method, string outcome)
    {
        var run = await AddFinishedRunAsync();

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => AddOutcomeAsync(run.Id, outcome, method)));
    }

    [Theory]
    [InlineData(SourceMethods.LinkOnly, ScanOutcomes.NotChecked)]
    [InlineData(SourceMethods.LinkOnly, ScanOutcomes.Unsupported)]
    [InlineData(SourceMethods.EmailDerived, ScanOutcomes.NotChecked)]
    [InlineData(SourceMethods.EmailDerived, ScanOutcomes.Failed)]
    public async Task Link_only_and_email_derived_sources_can_be_stored_as_not_searched(string method, string outcome)
    {
        var run = await AddFinishedRunAsync();

        await AddOutcomeAsync(run.Id, outcome, method);

        Assert.Equal(1, await CountAsync("source_outcomes"));
    }

    [Theory]
    [InlineData(ScanOutcomes.Failed)]
    [InlineData(ScanOutcomes.Unsupported)]
    [InlineData(ScanOutcomes.NotChecked)]
    public async Task Only_success_and_partial_outcomes_may_report_listings(string outcome)
    {
        var run = await AddFinishedRunAsync();

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => AddOutcomeAsync(run.Id, outcome, listingsFound: 1)));
    }

    [Fact]
    public async Task The_method_is_limited_to_the_known_values()
    {
        var run = await AddFinishedRunAsync();

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => AddOutcomeAsync(run.Id, method: "scraping")));
    }

    [Theory]
    [InlineData(-1, 0, 1)]
    [InlineData(0, -1, 1)]
    [InlineData(0, 0, 0)]
    public async Task Counts_cannot_be_negative_and_at_least_one_attempt_is_recorded(int found, int rejected, int attempts)
    {
        var run = await AddFinishedRunAsync();

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => AddOutcomeAsync(run.Id, listingsFound: found, listingsRejected: rejected, attempts: attempts)));
    }

    [Fact]
    public async Task An_outcome_must_point_at_an_existing_run()
    {
        await using var ctx = Db.CreateContext();
        ctx.SourceOutcomes.Add(new SourceOutcome(Guid.NewGuid(), "greenhouse", SourceMethods.Api, null, ScanOutcomes.Failed, "x", null, 0, 0, 1, T0, T0));

        Assert.Equal(ForeignKeyViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    [Fact]
    public async Task Deleting_a_run_deletes_its_outcomes_and_nothing_else()
    {
        var employer = await AddEmployerAsync();
        var job = await AddJobAsync(employer);
        await AddListingAsync(job);
        var run = await AddFinishedRunAsync();
        await AddOutcomeAsync(run.Id, employerId: employer.Id);
        await AddOutcomeAsync(run.Id, ScanOutcomes.Failed, employerId: employer.Id);

        await Db.ExecuteAsync($"DELETE FROM scan_runs WHERE \"Id\" = '{run.Id}'");

        Assert.Equal(0, await CountAsync("source_outcomes"));
        Assert.Equal(1, await CountAsync("source_listings"));
        Assert.Equal(1, await CountAsync("jobs"));
        Assert.Equal(1, await CountAsync("employers"));
    }

    [Fact]
    public async Task Deleting_an_employer_keeps_the_outcome_history_with_a_null_employer()
    {
        var employer = await AddEmployerAsync();
        var run = await AddFinishedRunAsync();
        await AddOutcomeAsync(run.Id, employerId: employer.Id);

        await Db.ExecuteAsync($"DELETE FROM employers WHERE \"Id\" = '{employer.Id}'");

        await using var ctx = Db.CreateContext();
        var outcome = await ctx.SourceOutcomes.SingleAsync();
        Assert.Null(outcome.EmployerId);
    }

    [Fact]
    public async Task Error_text_and_next_step_are_limited_to_500_characters()
    {
        var run = await AddFinishedRunAsync();
        await using var ctx = Db.CreateContext();
        ctx.SourceOutcomes.Add(new SourceOutcome(run.Id, "greenhouse", SourceMethods.Api, null, ScanOutcomes.Failed,
            new string('x', 501), null, 0, 0, 1, T0, T0));

        Assert.Equal("22001", await SqlStateOfAsync(() => ctx.SaveChangesAsync())); // string_data_right_truncation
    }

    [Fact]
    public void The_content_hash_ignores_whitespace_differences_and_never_returns_the_text()
    {
        var a = ListingContent.Hash("Build  APIs\n\nin   .NET ");
        var b = ListingContent.Hash("Build APIs in .NET");

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}$", a);
        Assert.NotEqual(a, ListingContent.Hash("Build APIs in Java"));
        Assert.Equal(ListingContent.Hash(null), ListingContent.Hash("   "));
    }
}
