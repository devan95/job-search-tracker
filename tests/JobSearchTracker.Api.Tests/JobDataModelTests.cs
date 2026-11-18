using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>Order 6: constraints and delete rules of the jobs tables, asserted against the real PostgreSQL schema.</summary>
public class JobDataModelTests : DatabaseTestBase
{
    private static JobDetails Details(string title = "Backend Engineer", string? requisition = null, string? location = null) =>
        new(title, NameNormalizer.Normalize(title), null, null, location, location is null ? null : NameNormalizer.Normalize(location),
            null, null, null, null, null, requisition, requisition is null ? null : NameNormalizer.Normalize(requisition), null, null, null);

    private async Task<Employer> AddEmployerAsync(string name = "Acme")
    {
        await using var ctx = Db.CreateContext();
        var employer = new Employer(name);
        ctx.Employers.Add(employer);
        await ctx.SaveChangesAsync();
        return employer;
    }

    private async Task<Job> AddJobAsync(Employer employer, JobDetails details)
    {
        await using var ctx = Db.CreateContext();
        ctx.Employers.Attach(employer); // already stored by AddEmployerAsync; Add(job) must not insert it again
        var job = new Job(employer, details, DateTimeOffset.UtcNow);
        ctx.Jobs.Add(job);
        await ctx.SaveChangesAsync();
        return job;
    }

    [Fact]
    public async Task A_job_round_trips_every_field_and_starts_open_with_one_history_row()
    {
        var employer = await AddEmployerAsync();
        var details = new JobDetails(
            "Senior Engineer", "senior engineer", "https://example.com/jobs/1?utm_source=x", "https://example.com/jobs/1",
            "Pune; Remote India", "pune; remote india", JobWorkModes.Hybrid, JobEmploymentTypes.FullTime, "Platform", 3, 8,
            "R-100", "r-100", new DateOnly(2026, 11, 10), "Plain text description.", "Company site");

        var created = await AddJobAsync(employer, details);

        await using var ctx = Db.CreateContext();
        var job = await ctx.Jobs.Include(j => j.StatusHistory).SingleAsync(j => j.Id == created.Id);
        Assert.Equal(employer.Id, job.EmployerId);
        Assert.Equal("Senior Engineer", job.Title);
        Assert.Equal("https://example.com/jobs/1", job.CanonicalUrl);
        Assert.Equal(JobWorkModes.Hybrid, job.WorkMode);
        Assert.Equal(JobEmploymentTypes.FullTime, job.EmploymentType);
        Assert.Equal(3, job.ExperienceMinYears);
        Assert.Equal(8, job.ExperienceMaxYears);
        Assert.Equal(new DateOnly(2026, 11, 10), job.PostedDate);
        Assert.Equal(JobStatuses.Open, job.Status);
        Assert.Equal(JobOrigins.Manual, job.CreatedVia);
        var history = Assert.Single(job.StatusHistory);
        Assert.Null(history.FromStatus);
        Assert.Equal(JobStatuses.Open, history.ToStatus);
    }

    [Fact]
    public async Task The_same_employer_cannot_have_two_jobs_with_the_same_normalized_requisition_id()
    {
        var employer = await AddEmployerAsync();
        await AddJobAsync(employer, Details("Role A", requisition: "R-1"));

        var state = await SqlStateOfAsync(() => AddJobAsync(employer, Details("Role B", requisition: " r-1 ")));

        Assert.Equal(UniqueViolation, state);
    }

    [Fact]
    public async Task Requisition_ids_may_repeat_across_employers_and_jobs_without_one_are_not_constrained()
    {
        var first = await AddEmployerAsync("First");
        var second = await AddEmployerAsync("Second");

        await AddJobAsync(first, Details("Role", requisition: "R-1"));
        await AddJobAsync(second, Details("Role", requisition: "R-1"));
        await AddJobAsync(first, Details("No requisition one"));
        await AddJobAsync(first, Details("No requisition two"));

        await using var ctx = Db.CreateContext();
        Assert.Equal(4, await ctx.Jobs.CountAsync());
    }

    [Fact]
    public async Task An_employer_with_jobs_cannot_be_deleted()
    {
        var employer = await AddEmployerAsync();
        await AddJobAsync(employer, Details());

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM employers WHERE \"Id\" = '{employer.Id}'"));

        AssertDeleteBlocked(state);
    }

    [Fact]
    public async Task Deleting_a_job_removes_its_history_and_keeps_the_employer()
    {
        var employer = await AddEmployerAsync();
        var job = await AddJobAsync(employer, Details());

        await Db.ExecuteAsync($"DELETE FROM jobs WHERE \"Id\" = '{job.Id}'");

        Assert.Empty(await Db.QueryAsync("SELECT 1 FROM job_status_history", r => r.GetInt32(0)));
        Assert.Single(await Db.QueryAsync("SELECT 1 FROM employers", r => r.GetInt32(0)));
    }

    [Theory]
    [InlineData("\"Status\" = 'bogus'")]
    [InlineData("\"Status\" = 'Open'")]
    [InlineData("\"WorkMode\" = 'office'")]
    [InlineData("\"EmploymentType\" = 'full-time'")]
    [InlineData("\"Title\" = '   '")]
    [InlineData("\"ExperienceMinYears\" = -1")]
    [InlineData("\"ExperienceMaxYears\" = -1")]
    [InlineData("\"ExperienceMinYears\" = 9, \"ExperienceMaxYears\" = 2")]
    public async Task Check_constraints_reject_invalid_values(string assignment)
    {
        var job = await AddJobAsync(await AddEmployerAsync(), Details());

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync($"UPDATE jobs SET {assignment} WHERE \"Id\" = '{job.Id}'"));

        Assert.Equal(CheckViolation, state);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("open")]
    [InlineData("closed")]
    [InlineData("expired")]
    [InlineData("reopened")]
    [InlineData("unavailable")]
    [InlineData("verificationPending")]
    public async Task Every_defined_status_is_accepted_including_the_system_only_ones(string status)
    {
        var job = await AddJobAsync(await AddEmployerAsync(), Details());

        await Db.ExecuteAsync($"UPDATE jobs SET \"Status\" = '{status}' WHERE \"Id\" = '{job.Id}'");

        Assert.Equal(new[] { status }, await Db.QueryAsync("SELECT \"Status\" FROM jobs", r => r.GetString(0)));
    }

    [Fact]
    public async Task Two_contexts_changing_the_same_job_cannot_both_save()
    {
        var created = await AddJobAsync(await AddEmployerAsync(), Details());
        await using var first = Db.CreateContext();
        await using var second = Db.CreateContext();
        var firstJob = await first.Jobs.SingleAsync(j => j.Id == created.Id);
        var secondJob = await second.Jobs.SingleAsync(j => j.Id == created.Id);

        first.JobStatusHistories.Add(firstJob.ChangeStatus(JobStatuses.Closed, DateTimeOffset.UtcNow));
        await first.SaveChangesAsync();
        second.JobStatusHistories.Add(secondJob.ChangeStatus(JobStatuses.Expired, DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task The_status_history_only_accepts_defined_statuses()
    {
        var job = await AddJobAsync(await AddEmployerAsync(), Details());

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO job_status_history (\"Id\", \"JobId\", \"FromStatus\", \"ToStatus\", \"ChangedAtUtc\") " +
            $"VALUES (gen_random_uuid(), '{job.Id}', 'open', 'bogus', now())"));

        Assert.Equal(CheckViolation, state);
    }
}
