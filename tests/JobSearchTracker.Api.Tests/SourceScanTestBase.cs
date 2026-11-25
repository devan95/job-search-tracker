using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>Helpers shared by the Phase 2 source-data tests. Every test gets its own migrated database (see DatabaseTestBase).</summary>
public abstract class SourceScanTestBase : DatabaseTestBase
{
    protected static readonly DateTimeOffset T0 = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);
    protected static readonly string Hash = ListingContent.Hash("Synthetic description.");

    protected async Task<Employer> AddEmployerAsync(string name = "Acme")
    {
        await using var ctx = Db.CreateContext();
        var employer = new Employer(name);
        ctx.Employers.Add(employer);
        await ctx.SaveChangesAsync();
        return employer;
    }

    protected async Task<Job> AddJobAsync(Employer employer, string title = "Backend Engineer")
    {
        await using var ctx = Db.CreateContext();
        ctx.Employers.Attach(employer);
        var details = new JobDetails(title, NameNormalizer.Normalize(title), null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        var job = new Job(employer, details, T0);
        ctx.Jobs.Add(job);
        await ctx.SaveChangesAsync();
        return job;
    }

    protected async Task<SourceListing> AddListingAsync(Job job, string sourceType = "greenhouse", string externalId = "1001")
    {
        await using var ctx = Db.CreateContext();
        var listing = new SourceListing(job.Id, sourceType, externalId, $"https://example.com/jobs/{externalId}", Hash, null, T0);
        ctx.SourceListings.Add(listing);
        await ctx.SaveChangesAsync();
        return listing;
    }

    protected async Task<ScanRun> AddFinishedRunAsync(Guid? profileId = null, string outcome = ScanOutcomes.Success)
    {
        await using var ctx = Db.CreateContext();
        var run = new ScanRun(profileId, ScanTriggers.Manual, T0);
        run.Finish(outcome, T0.AddSeconds(5));
        ctx.ScanRuns.Add(run);
        await ctx.SaveChangesAsync();
        return run;
    }

    protected async Task AddOutcomeAsync(Guid runId, string outcome = ScanOutcomes.Success, string method = SourceMethods.Api,
        Guid? employerId = null, int listingsFound = 0, int listingsRejected = 0, int attempts = 1)
    {
        await using var ctx = Db.CreateContext();
        ctx.SourceOutcomes.Add(new SourceOutcome(runId, "greenhouse", method, employerId, outcome, null, null,
            listingsFound, listingsRejected, attempts, T0, T0.AddSeconds(1)));
        await ctx.SaveChangesAsync();
    }

    protected async Task<int> CountAsync(string table) =>
        (await Db.QueryAsync($"SELECT count(*)::int FROM {table}", r => r.GetInt32(0))).Single();
}
