using System.Net;
using System.Text.Json;

namespace JobSearchTracker.Api.Tests;

/// <summary>Order 7: manual application tracking, manual confirmation of Applied, corrections and the audit trail over HTTP.</summary>
public class ApplicationApiTests : ApiTestBase
{
    private async Task<Guid> NewJobAsync(string employer = "Acme", string title = "Backend Engineer", string? url = null)
    {
        var reply = await PostAsync("/api/jobs", new { employer, title, applyUrl = url });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    private async Task<Reply> CreateAsync(Guid candidateId, Guid jobId) =>
        await PostAsync("/api/applications", new { candidateId, jobId });

    private async Task<Guid> NewApplicationAsync(Guid? candidateId = null, Guid? jobId = null)
    {
        var reply = await CreateAsync(candidateId ?? await CreateCandidateAsync(), jobId ?? await NewJobAsync());
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    private async Task<Reply> ConfirmAsync(Guid id, DateTimeOffset? appliedAtUtc = null) =>
        await PostAsync($"/api/applications/{id}/confirm-applied", new { confirmed = true, appliedAtUtc });

    private async Task<Guid> AppliedApplicationAsync(DateTimeOffset? appliedAtUtc = null)
    {
        var id = await NewApplicationAsync();
        Assert.Equal(HttpStatusCode.OK, (await ConfirmAsync(id, appliedAtUtc)).Status);
        return id;
    }

    private Task<Reply> StatusAsync(Guid id, string status, string? reason = null) =>
        PostAsync($"/api/applications/{id}/status", new { status, reason });

    /// <summary>Times cross PostgreSQL (microsecond precision) and JSON, so compare with a small tolerance.</summary>
    private static void Near(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True(Math.Abs((expected - actual).TotalMilliseconds) < 5, $"Expected {expected:O} but was {actual:O}.");

    private static string[] Actions(Reply reply) =>
        reply.Body.GetProperty("auditTrail").EnumerateArray().Select(e => e.GetProperty("action").GetString()!).ToArray();

    // ---- create, read, list -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_returns_a_saved_application_that_is_not_applied_with_one_audit_row()
    {
        var candidateId = await CreateCandidateAsync();
        var jobId = await NewJobAsync("Globex", "Staff Engineer");

        var reply = await CreateAsync(candidateId, jobId);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/applications/{reply.Id}", reply.Location);
        Assert.Equal("saved", reply.Str("status"));
        Assert.False(reply.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("method").ValueKind);
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("appliedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("appliedConfirmedAtUtc").ValueKind);
        Assert.Equal(candidateId, reply.Body.GetProperty("candidateId").GetGuid());
        Assert.Equal(jobId, reply.Body.GetProperty("job").GetProperty("id").GetGuid());
        Assert.Equal("Staff Engineer", reply.Body.GetProperty("job").GetProperty("title").GetString());
        Assert.Equal("Globex", reply.Body.GetProperty("job").GetProperty("employer").GetProperty("name").GetString());
        Assert.Equal(new[] { "created" }, Actions(reply));
    }

    [Fact]
    public async Task Saving_a_job_never_marks_it_applied_even_when_the_job_is_closed()
    {
        var jobId = await NewJobAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/jobs/{jobId}/status", new { status = "closed" })).Status);

        var reply = await CreateAsync(await CreateCandidateAsync(), jobId);

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal("saved", reply.Str("status"));
        Assert.False(reply.Body.GetProperty("isApplied").GetBoolean());
    }

    [Fact]
    public async Task Create_validates_ids_and_reports_every_problem()
    {
        var missing = await PostAsync("/api/applications", new { });
        AssertInvalid(missing, "candidateId");
        AssertInvalid(missing, "jobId");

        var unknown = await CreateAsync(Guid.NewGuid(), Guid.NewGuid());
        AssertInvalid(unknown, "candidateId");
        AssertInvalid(unknown, "jobId");
    }

    [Fact]
    public async Task A_second_application_for_the_same_candidate_and_job_is_a_409_that_names_the_existing_one()
    {
        var candidateId = await CreateCandidateAsync();
        var jobId = await NewJobAsync();
        var first = await CreateAsync(candidateId, jobId);
        await ConfirmAsync(first.Id);

        var second = await CreateAsync(candidateId, jobId);

        AssertProblem(second, HttpStatusCode.Conflict, "application_exists");
        Assert.Equal(first.Id, second.Body.GetProperty("existingId").GetGuid());
        Assert.Equal("applied", second.Body.GetProperty("status").GetString());
        Assert.True(second.Body.GetProperty("isApplied").GetBoolean());
        var list = await GetAsync($"/api/applications?candidateId={candidateId}&jobId={jobId}");
        Assert.Equal(1, list.Count);
    }

    [Fact]
    public async Task Different_candidates_have_their_own_application_for_the_same_job()
    {
        var jobId = await NewJobAsync();
        await NewApplicationAsync(await CreateCandidateAsync("One"), jobId);
        await NewApplicationAsync(await CreateCandidateAsync("Two"), jobId);

        var list = await GetAsync($"/api/applications?jobId={jobId}");

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task A_repost_warning_names_the_existing_job_so_the_existing_application_can_be_found()
    {
        var candidateId = await CreateCandidateAsync();
        var first = await NewJobAsync("Acme", "Backend Engineer", "https://acme.com/jobs/1");
        var firstApplication = await NewApplicationAsync(candidateId, first);
        await ConfirmAsync(firstApplication);

        // The job API warns about the repost; the existing application is found by the existing job id it names.
        var repost = await PostAsync("/api/jobs", new { employer = "Acme", title = "Backend Engineer", applyUrl = "https://www.acme.com/jobs/1?utm_source=x" });
        Assert.Equal("possible_duplicate", repost.Body.GetProperty("code").GetString());
        var existingJobId = repost.Body.GetProperty("existingId").GetGuid();

        var found = await GetAsync($"/api/applications?candidateId={candidateId}&jobId={existingJobId}&applied=true");
        Assert.Equal(1, found.Count);
        Assert.Equal(firstApplication, found[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Get_returns_the_application_with_its_audit_trail_and_unknown_ids_are_404()
    {
        var id = await NewApplicationAsync();

        var reply = await GetAsync($"/api/applications/{id}");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(new[] { "created" }, Actions(reply));
        AssertProblem(await GetAsync($"/api/applications/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task List_filters_by_status_and_applied_and_rejects_unknown_values()
    {
        var applied = await AppliedApplicationAsync();
        var saved = await NewApplicationAsync();

        var onlyApplied = await GetAsync("/api/applications?applied=true");
        var onlySaved = await GetAsync("/api/applications?status=SAVED");

        Assert.Equal(applied, Assert.Single(onlyApplied.Body.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal(saved, Assert.Single(onlySaved.Body.EnumerateArray()).GetProperty("id").GetGuid());
        AssertInvalid(await GetAsync("/api/applications?status=screening"), "status");
        AssertInvalid(await GetAsync("/api/applications?take=0"), "take");
    }

    // ---- confirm applied ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Confirming_applied_defaults_to_now_stores_method_manual_and_the_confirmation_time()
    {
        var id = await NewApplicationAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var reply = await ConfirmAsync(id);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("applied", reply.Str("status"));
        Assert.True(reply.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal("manual", reply.Str("method"));
        var appliedAt = reply.Body.GetProperty("appliedAtUtc").GetDateTimeOffset();
        var confirmedAt = reply.Body.GetProperty("appliedConfirmedAtUtc").GetDateTimeOffset();
        Assert.InRange(appliedAt, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.Equal(appliedAt, confirmedAt);
        Assert.Equal(new[] { "created", "appliedConfirmed" }, Actions(reply));
        var entry = reply.Body.GetProperty("auditTrail")[1];
        Assert.Equal("saved", entry.GetProperty("oldStatus").GetString());
        Assert.False(entry.GetProperty("oldIsApplied").GetBoolean());
        Assert.Equal("applied", entry.GetProperty("newStatus").GetString());
        Assert.True(entry.GetProperty("newIsApplied").GetBoolean());
        Assert.Equal("user", entry.GetProperty("actor").GetString());
    }

    [Fact]
    public async Task Confirming_applied_allows_back_dating_and_keeps_the_confirmation_time_separate()
    {
        var id = await NewApplicationAsync();
        var appliedAt = DateTimeOffset.UtcNow.AddDays(-10);

        var reply = await ConfirmAsync(id, appliedAt);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Near(appliedAt, reply.Body.GetProperty("appliedAtUtc").GetDateTimeOffset());
        Assert.True(reply.Body.GetProperty("appliedConfirmedAtUtc").GetDateTimeOffset() > appliedAt.AddDays(9));
    }

    [Fact]
    public async Task Confirming_applied_in_the_future_is_rejected_and_changes_nothing()
    {
        var id = await NewApplicationAsync();

        var reply = await ConfirmAsync(id, DateTimeOffset.UtcNow.AddHours(1));

        AssertInvalid(reply, "appliedAtUtc");
        var after = await GetAsync($"/api/applications/{id}");
        Assert.Equal("saved", after.Str("status"));
        Assert.False(after.Body.GetProperty("isApplied").GetBoolean());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"confirmed\":false}")]
    [InlineData("{\"confirmed\":null}")]
    public async Task Applied_needs_an_explicit_confirmation(string body)
    {
        var id = await NewApplicationAsync();

        var reply = await SendRawJsonAsync(HttpMethod.Post, $"/api/applications/{id}/confirm-applied", body);

        AssertInvalid(reply, "confirmed");
        Assert.False((await GetAsync($"/api/applications/{id}")).Body.GetProperty("isApplied").GetBoolean());
    }

    [Fact]
    public async Task Confirming_twice_is_a_409_and_does_not_add_an_audit_row()
    {
        var id = await AppliedApplicationAsync();

        var again = await ConfirmAsync(id);

        AssertProblem(again, HttpStatusCode.Conflict, "already_applied");
        Assert.Equal(2, (await GetAsync($"/api/applications/{id}")).Body.GetProperty("auditTrail").GetArrayLength());
    }

    [Fact]
    public async Task Pending_verification_is_not_applied_until_the_user_confirms()
    {
        var id = await NewApplicationAsync();

        var pending = await StatusAsync(id, "pendingVerification");

        Assert.Equal(HttpStatusCode.OK, pending.Status);
        Assert.Equal("pendingVerification", pending.Str("status"));
        Assert.False(pending.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal(JsonValueKind.Null, pending.Body.GetProperty("method").ValueKind);

        var confirmed = await ConfirmAsync(id);
        Assert.Equal("applied", confirmed.Str("status"));
        Assert.True(confirmed.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal(new[] { "created", "statusChanged", "appliedConfirmed" }, Actions(confirmed));
    }

    [Fact]
    public async Task Confirming_unknown_application_is_404()
    {
        AssertProblem(await ConfirmAsync(Guid.NewGuid()), HttpStatusCode.NotFound, "not_found");
    }

    // ---- status ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Later_statuses_keep_the_applied_fact_method_and_time()
    {
        var appliedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var id = await AppliedApplicationAsync(appliedAt);

        foreach (var status in new[] { "interview", "offer", "rejected" })
        {
            var reply = await StatusAsync(id, status);
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.Equal(status, reply.Str("status"));
            Assert.True(reply.Body.GetProperty("isApplied").GetBoolean());
            Assert.Equal("manual", reply.Str("method"));
            Near(appliedAt, reply.Body.GetProperty("appliedAtUtc").GetDateTimeOffset());
        }
    }

    [Fact]
    public async Task Withdrawing_a_saved_or_an_applied_application_works_and_keeps_the_applied_fact()
    {
        var saved = await NewApplicationAsync();
        var applied = await AppliedApplicationAsync();

        var fromSaved = await StatusAsync(saved, "withdrawn", "Role withdrawn by me");
        var fromApplied = await StatusAsync(applied, "withdrawn");

        Assert.Equal("withdrawn", fromSaved.Str("status"));
        Assert.False(fromSaved.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal("withdrawn", fromApplied.Str("status"));
        Assert.True(fromApplied.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal("Role withdrawn by me", fromSaved.Body.GetProperty("auditTrail")[1].GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData("interview")]
    [InlineData("offer")]
    [InlineData("rejected")]
    public async Task A_saved_application_cannot_jump_to_a_status_that_needs_an_applied_submission(string status)
    {
        var id = await NewApplicationAsync();

        var reply = await StatusAsync(id, status);

        AssertProblem(reply, HttpStatusCode.Conflict, "invalid_application_transition");
        Assert.Equal("saved", reply.Body.GetProperty("currentStatus").GetString());
        Assert.Equal(status, reply.Body.GetProperty("requestedStatus").GetString());
    }

    [Theory]
    [InlineData("rejected")]
    [InlineData("withdrawn")]
    public async Task Rejected_and_withdrawn_cannot_be_left_by_an_ordinary_status_change(string closed)
    {
        var id = await AppliedApplicationAsync();
        Assert.Equal(HttpStatusCode.OK, (await StatusAsync(id, closed)).Status);

        var reply = await StatusAsync(id, "interview");

        AssertProblem(reply, HttpStatusCode.Conflict, "invalid_application_transition");
    }

    [Theory]
    [InlineData("applied")]
    [InlineData("saved")]
    [InlineData("screening")]
    public async Task Applied_and_saved_cannot_be_requested_through_the_status_endpoint(string status)
    {
        var id = await NewApplicationAsync();

        AssertInvalid(await StatusAsync(id, status), "status");
    }

    [Fact]
    public async Task A_missing_status_is_a_field_error_and_unknown_ids_are_404()
    {
        var id = await NewApplicationAsync();

        AssertInvalid(await PostAsync($"/api/applications/{id}/status", new { }), "status");
        AssertProblem(await StatusAsync(Guid.NewGuid(), "withdrawn"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_job_status_change_never_changes_the_application()
    {
        var jobId = await NewJobAsync();
        var id = await NewApplicationAsync(jobId: jobId);
        await ConfirmAsync(id);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/jobs/{jobId}/status", new { status = "expired" })).Status);

        var application = await GetAsync($"/api/applications/{id}");
        Assert.Equal("applied", application.Str("status"));
        Assert.True(application.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal("expired", application.Body.GetProperty("job").GetProperty("status").GetString());
    }

    // ---- un-apply ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Unapply_needs_a_reason_and_changes_nothing_without_one()
    {
        var id = await AppliedApplicationAsync();

        AssertInvalid(await PostAsync($"/api/applications/{id}/unapply", new { }), "reason");
        AssertInvalid(await PostAsync($"/api/applications/{id}/unapply", new { reason = "   " }), "reason");
        AssertInvalid(await PostAsync($"/api/applications/{id}/unapply", new { reason = new string('x', 501) }), "reason");

        Assert.True((await GetAsync($"/api/applications/{id}")).Body.GetProperty("isApplied").GetBoolean());
    }

    [Fact]
    public async Task Unapply_clears_the_fact_returns_to_saved_and_keeps_the_old_values_in_the_audit_trail()
    {
        var appliedAt = DateTimeOffset.UtcNow.AddDays(-4);
        var id = await AppliedApplicationAsync(appliedAt);
        await StatusAsync(id, "interview");

        var reply = await PostAsync($"/api/applications/{id}/unapply", new { reason = "  Ticked by mistake " });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("saved", reply.Str("status"));
        Assert.False(reply.Body.GetProperty("isApplied").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("method").ValueKind);
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("appliedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("appliedConfirmedAtUtc").ValueKind);
        Assert.Equal(new[] { "created", "appliedConfirmed", "statusChanged", "unapplied" }, Actions(reply));
        var entry = reply.Body.GetProperty("auditTrail")[3];
        Assert.Equal("interview", entry.GetProperty("oldStatus").GetString());
        Assert.True(entry.GetProperty("oldIsApplied").GetBoolean());
        Near(appliedAt, entry.GetProperty("oldAppliedAtUtc").GetDateTimeOffset());
        Assert.Equal("saved", entry.GetProperty("newStatus").GetString());
        Assert.False(entry.GetProperty("newIsApplied").GetBoolean());
        Assert.Equal("Ticked by mistake", entry.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Unapply_of_an_application_that_is_not_applied_is_a_409()
    {
        var id = await NewApplicationAsync();

        AssertProblem(await PostAsync($"/api/applications/{id}/unapply", new { reason = "x" }), HttpStatusCode.Conflict, "not_applied");
    }

    // ---- corrections -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Correct_needs_a_reason_and_at_least_one_change()
    {
        var id = await AppliedApplicationAsync();

        AssertInvalid(await PostAsync($"/api/applications/{id}/correct", new { status = "offer" }), "reason");
        AssertInvalid(await PostAsync($"/api/applications/{id}/correct", new { reason = "Typo" }), "status");
        AssertInvalid(await PostAsync($"/api/applications/{id}/correct", new { status = "screening", reason = "x" }), "status");
    }

    [Theory]
    [InlineData("rejected", "interview")]
    [InlineData("withdrawn", "offer")]
    [InlineData("rejected", "applied")]
    public async Task A_rejected_or_withdrawn_application_is_reopened_by_an_audited_correction(string closed, string reopenedTo)
    {
        var id = await AppliedApplicationAsync();
        await StatusAsync(id, closed);

        var reply = await PostAsync($"/api/applications/{id}/correct", new { status = reopenedTo, reason = "Recruiter reopened the process" });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(reopenedTo, reply.Str("status"));
        Assert.True(reply.Body.GetProperty("isApplied").GetBoolean());
        var entry = reply.Body.GetProperty("auditTrail").EnumerateArray().Last();
        Assert.Equal("corrected", entry.GetProperty("action").GetString());
        Assert.Equal(closed, entry.GetProperty("oldStatus").GetString());
        Assert.Equal(reopenedTo, entry.GetProperty("newStatus").GetString());
        Assert.Equal("Recruiter reopened the process", entry.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task A_withdrawn_application_that_was_never_applied_is_reopened_to_saved()
    {
        var id = await NewApplicationAsync();
        await StatusAsync(id, "withdrawn");

        var reply = await PostAsync($"/api/applications/{id}/correct", new { status = "saved", reason = "Still interested" });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("saved", reply.Str("status"));
        Assert.False(reply.Body.GetProperty("isApplied").GetBoolean());
    }

    [Fact]
    public async Task A_correction_cannot_create_an_inconsistent_state()
    {
        var saved = await NewApplicationAsync();
        var applied = await AppliedApplicationAsync();

        var needsApplied = await PostAsync($"/api/applications/{saved}/correct", new { status = "interview", reason = "x" });
        var appliedAsSaved = await PostAsync($"/api/applications/{applied}/correct", new { status = "saved", reason = "x" });
        var dateWhileNotApplied = await PostAsync($"/api/applications/{saved}/correct", new { appliedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), reason = "x" });
        var nothingChanges = await PostAsync($"/api/applications/{applied}/correct", new { status = "applied", reason = "x" });

        AssertProblem(needsApplied, HttpStatusCode.Conflict, "invalid_correction");
        AssertProblem(appliedAsSaved, HttpStatusCode.Conflict, "invalid_correction");
        AssertProblem(dateWhileNotApplied, HttpStatusCode.Conflict, "invalid_correction");
        AssertProblem(nothingChanges, HttpStatusCode.Conflict, "invalid_correction");
        Assert.Equal(1, (await GetAsync($"/api/applications/{saved}")).Body.GetProperty("auditTrail").GetArrayLength());
        Assert.Equal(2, (await GetAsync($"/api/applications/{applied}")).Body.GetProperty("auditTrail").GetArrayLength());
    }

    [Fact]
    public async Task The_date_applied_can_be_corrected_but_not_into_the_future()
    {
        var original = DateTimeOffset.UtcNow.AddDays(-1);
        var corrected = DateTimeOffset.UtcNow.AddDays(-6);
        var id = await AppliedApplicationAsync(original);

        AssertInvalid(await PostAsync($"/api/applications/{id}/correct", new { appliedAtUtc = DateTimeOffset.UtcNow.AddHours(2), reason = "x" }), "appliedAtUtc");
        var reply = await PostAsync($"/api/applications/{id}/correct", new { appliedAtUtc = corrected, reason = "Found the confirmation email" });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Near(corrected, reply.Body.GetProperty("appliedAtUtc").GetDateTimeOffset());
        var entry = reply.Body.GetProperty("auditTrail").EnumerateArray().Last();
        Near(original, entry.GetProperty("oldAppliedAtUtc").GetDateTimeOffset());
        Near(corrected, entry.GetProperty("newAppliedAtUtc").GetDateTimeOffset());
        Assert.True(reply.Body.GetProperty("appliedConfirmedAtUtc").GetDateTimeOffset() > corrected.AddDays(4));
    }

    // ---- audit trail and deletes -------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_change_is_listed_in_order_in_the_audit_trail()
    {
        var id = await NewApplicationAsync();
        await StatusAsync(id, "pendingVerification");
        await ConfirmAsync(id);
        await StatusAsync(id, "interview");
        await StatusAsync(id, "rejected");
        await PostAsync($"/api/applications/{id}/correct", new { status = "interview", reason = "Reopened" });
        await PostAsync($"/api/applications/{id}/unapply", new { reason = "Wrong job" });

        var reply = await GetAsync($"/api/applications/{id}");

        Assert.Equal(
            new[] { "created", "statusChanged", "appliedConfirmed", "statusChanged", "statusChanged", "corrected", "unapplied" },
            Actions(reply));
        var times = reply.Body.GetProperty("auditTrail").EnumerateArray().Select(e => e.GetProperty("changedAtUtc").GetDateTimeOffset()).ToArray();
        Assert.Equal(times.OrderBy(t => t), times);
    }

    [Fact]
    public async Task A_job_with_an_application_cannot_be_deleted_and_a_candidate_with_one_cannot_either()
    {
        var candidateId = await CreateCandidateAsync();
        var jobId = await NewJobAsync();
        var id = await NewApplicationAsync(candidateId, jobId);

        AssertProblem(await DeleteAsync($"/api/jobs/{jobId}"), HttpStatusCode.Conflict, "job_in_use");
        var candidate = await DeleteAsync($"/api/candidates/{candidateId}");
        AssertProblem(candidate, HttpStatusCode.Conflict, "candidate_has_applications");
        Assert.Equal(1, candidate.Body.GetProperty("applicationCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/applications/{id}")).Status);
    }

    [Fact]
    public async Task There_is_no_endpoint_to_delete_an_application_or_edit_its_audit_trail()
    {
        var id = await NewApplicationAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await DeleteAsync($"/api/applications/{id}")).Status);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await PutAsync($"/api/applications/{id}", new { status = "offer" })).Status);
    }
}
