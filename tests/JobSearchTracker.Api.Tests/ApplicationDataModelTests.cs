using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>Order 7: constraints, delete rules and the append-only trail, asserted against the real PostgreSQL schema.</summary>
public class ApplicationDataModelTests : DatabaseTestBase
{
    private async Task<(Candidate Candidate, Job Job)> AddCandidateAndJobAsync(string employerName = "Acme", string title = "Backend Engineer")
    {
        var candidate = await AddCandidateAsync();
        await using var ctx = Db.CreateContext();
        var employer = new Employer(employerName);
        var job = new Job(employer, new JobDetails(title, NameNormalizer.Normalize(title), null, null, null, null, null, null, null, null, null, null, null, null, null, null), DateTimeOffset.UtcNow);
        ctx.Jobs.Add(job);
        await ctx.SaveChangesAsync();
        return (candidate, job);
    }

    private async Task<Application> AddApplicationAsync(Candidate candidate, Job job)
    {
        await using var ctx = Db.CreateContext();
        var application = new Application(candidate.Id, job.Id, DateTimeOffset.UtcNow);
        ctx.Applications.Add(application);
        await ctx.SaveChangesAsync();
        return application;
    }

    [Fact]
    public async Task An_application_round_trips_with_its_created_audit_row()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        var created = await AddApplicationAsync(candidate, job);

        await using var ctx = Db.CreateContext();
        var stored = await ctx.Applications.Include(a => a.AuditEntries).SingleAsync(a => a.Id == created.Id);
        Assert.Equal(ApplicationStatuses.Saved, stored.Status);
        Assert.False(stored.IsApplied);
        var audit = Assert.Single(stored.AuditEntries);
        Assert.Equal(ApplicationAuditActions.Created, audit.Action);
        Assert.Equal(ApplicationActors.User, audit.Actor);
    }

    [Fact]
    public async Task A_second_application_for_the_same_candidate_and_job_is_rejected_by_the_database()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        await AddApplicationAsync(candidate, job);

        var state = await SqlStateOfAsync(() => AddApplicationAsync(candidate, job));

        Assert.Equal(UniqueViolation, state);
    }

    [Fact]
    public async Task Another_candidate_or_another_job_may_have_its_own_application()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        var other = await AddCandidateAsync("Other Candidate");
        await AddApplicationAsync(candidate, job);

        await AddApplicationAsync(other, job);

        await using var ctx = Db.CreateContext();
        Assert.Equal(2, await ctx.Applications.CountAsync(a => a.JobId == job.Id));
    }

    [Fact]
    public async Task A_job_or_candidate_with_an_application_cannot_be_deleted()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        await AddApplicationAsync(candidate, job);

        AssertDeleteBlocked(await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM jobs WHERE \"Id\" = '{job.Id}'")));
        AssertDeleteBlocked(await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM candidates WHERE \"Id\" = '{candidate.Id}'")));
    }

    [Theory]
    [InlineData("applied", false, "NULL", "NULL", "NULL")]
    [InlineData("interview", false, "NULL", "NULL", "NULL")]
    [InlineData("offer", false, "NULL", "NULL", "NULL")]
    [InlineData("saved", true, "'manual'", "now()", "now()")]
    [InlineData("pendingVerification", true, "'manual'", "now()", "now()")]
    [InlineData("applied", true, "NULL", "now()", "now()")]
    [InlineData("applied", true, "'manual'", "NULL", "now()")]
    [InlineData("applied", true, "'manual'", "now()", "NULL")]
    [InlineData("saved", false, "'manual'", "NULL", "NULL")]
    [InlineData("saved", false, "NULL", "now()", "NULL")]
    [InlineData("applied", true, "'assisted'", "now()", "now()")]
    [InlineData("screening", false, "NULL", "NULL", "NULL")]
    public async Task The_database_rejects_a_status_that_disagrees_with_the_applied_fact(
        string status, bool isApplied, string method, string appliedAt, string confirmedAt)
    {
        var (candidate, job) = await AddCandidateAndJobAsync();

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO applications (\"Id\", \"CandidateId\", \"JobId\", \"Status\", \"IsApplied\", \"Method\", \"AppliedAtUtc\", \"AppliedConfirmedAtUtc\", \"CreatedAtUtc\", \"UpdatedAtUtc\") " +
            $"VALUES ('{Guid.NewGuid()}', '{candidate.Id}', '{job.Id}', '{status}', {(isApplied ? "true" : "false")}, {method}, {appliedAt}, {confirmedAt}, now(), now())"));

        Assert.Equal(CheckViolation, state);
    }

    [Theory]
    [InlineData("rejected", false, "NULL", "NULL", "NULL")]
    [InlineData("rejected", true, "'manual'", "now()", "now()")]
    [InlineData("withdrawn", true, "'manual'", "now()", "now()")]
    [InlineData("interview", true, "'manual'", "now()", "now()")]
    public async Task The_database_accepts_valid_combinations_including_rejected_and_withdrawn_with_the_applied_fact(
        string status, bool isApplied, string method, string appliedAt, string confirmedAt)
    {
        var (candidate, job) = await AddCandidateAndJobAsync();

        await Db.ExecuteAsync(
            "INSERT INTO applications (\"Id\", \"CandidateId\", \"JobId\", \"Status\", \"IsApplied\", \"Method\", \"AppliedAtUtc\", \"AppliedConfirmedAtUtc\", \"CreatedAtUtc\", \"UpdatedAtUtc\") " +
            $"VALUES ('{Guid.NewGuid()}', '{candidate.Id}', '{job.Id}', '{status}', {(isApplied ? "true" : "false")}, {method}, {appliedAt}, {confirmedAt}, now(), now())");
    }

    [Fact]
    public async Task Audit_rows_cannot_be_updated_or_deleted()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        var application = await AddApplicationAsync(candidate, job);

        var update = await SqlStateOfAsync(() => Db.ExecuteAsync($"UPDATE application_audit SET \"Reason\" = 'edited' WHERE \"ApplicationId\" = '{application.Id}'"));
        var delete = await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM application_audit WHERE \"ApplicationId\" = '{application.Id}'"));

        Assert.Equal("23000", update);
        Assert.Equal("23000", delete);
        var rows = await Db.QueryAsync($"SELECT count(*) FROM application_audit WHERE \"ApplicationId\" = '{application.Id}'", r => r.GetInt64(0));
        Assert.Equal(1, rows.Single());
    }

    [Fact]
    public async Task An_application_with_audit_rows_cannot_be_deleted()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        var application = await AddApplicationAsync(candidate, job);

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM applications WHERE \"Id\" = '{application.Id}'"));

        AssertDeleteBlocked(state);
    }

    [Theory]
    [InlineData("unapplied", "NULL")]
    [InlineData("unapplied", "'   '")]
    [InlineData("corrected", "NULL")]
    [InlineData("corrected", "''")]
    public async Task Unapply_and_correction_audit_rows_need_a_reason(string action, string reason)
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        var application = await AddApplicationAsync(candidate, job);

        var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
            "INSERT INTO application_audit (\"Id\", \"ApplicationId\", \"Action\", \"Actor\", \"ChangedAtUtc\", \"NewStatus\", \"NewIsApplied\", \"Reason\") " +
            $"VALUES ('{Guid.NewGuid()}', '{application.Id}', '{action}', 'user', now(), 'saved', false, {reason})"));

        Assert.Equal(CheckViolation, state);
    }

    [Fact]
    public async Task Audit_rows_reject_unknown_actions_and_actors()
    {
        var (candidate, job) = await AddCandidateAndJobAsync();
        var application = await AddApplicationAsync(candidate, job);

        foreach (var (action, actor) in new[] { ("deleted", "user"), ("created", "robot") })
        {
            var state = await SqlStateOfAsync(() => Db.ExecuteAsync(
                "INSERT INTO application_audit (\"Id\", \"ApplicationId\", \"Action\", \"Actor\", \"ChangedAtUtc\", \"NewStatus\", \"NewIsApplied\") " +
                $"VALUES ('{Guid.NewGuid()}', '{application.Id}', '{action}', '{actor}', now(), 'saved', false)"));
            Assert.Equal(CheckViolation, state);
        }
    }
}
