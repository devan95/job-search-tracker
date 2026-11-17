using System.Net;
using System.Text.Json;

namespace JobSearchTracker.Api.Tests;

/// <summary>Order 6: manual create, read, list, update, status and delete of canonical jobs.</summary>
public class JobApiTests : ApiTestBase
{
    private async Task<Reply> CreateJobAsync(string employer, string title, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["employer"] = employer, ["title"] = title };
        foreach (var property in extra?.GetType().GetProperties() ?? [])
        {
            body[property.Name] = property.GetValue(extra);
        }

        return await PostAsync("/api/jobs", body);
    }

    private async Task<Guid> NewJobAsync(string employer, string title, object? extra = null)
    {
        var reply = await CreateJobAsync(employer, title, extra);
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    // ---- create and read ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_with_only_the_required_fields_returns_201_and_an_open_manual_job()
    {
        var reply = await CreateJobAsync("  Acme   Corp ", "  Backend Engineer ");

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/jobs/{reply.Id}", reply.Location);
        Assert.Equal("Backend Engineer", reply.Str("title"));
        Assert.Equal("Acme   Corp", reply.Body.GetProperty("employer").GetProperty("name").GetString());
        Assert.Equal("open", reply.Str("status"));
        Assert.Equal("manual", reply.Str("createdVia"));
        Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty("applyUrl").ValueKind);
        var history = reply.Body.GetProperty("statusHistory");
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("fromStatus").ValueKind);
        Assert.Equal("open", history[0].GetProperty("toStatus").GetString());
    }

    [Fact]
    public async Task Create_stores_every_optional_field_and_get_returns_it_later()
    {
        var created = await PostAsync("/api/jobs", new
        {
            employer = "Globex",
            title = "Staff Engineer",
            applyUrl = "https://www.Globex.com/careers/42/?utm_source=mail&gh_jid=42",
            location = "Pune; Remote India",
            workMode = "ONSITE",
            employmentType = "FullTime",
            department = "Platform",
            experienceMinYears = 5,
            experienceMaxYears = 10,
            requisitionId = "R-42",
            postedDate = "2026-11-10",
            description = "Plain text.\nSecond line.",
            foundOn = "Company site",
        });
        Assert.Equal(HttpStatusCode.Created, created.Status);

        var job = await GetAsync($"/api/jobs/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, job.Status);
        Assert.Equal("https://www.Globex.com/careers/42/?utm_source=mail&gh_jid=42", job.Str("applyUrl"));
        Assert.Equal("https://globex.com/careers/42?gh_jid=42", job.Str("canonicalUrl"));
        Assert.Equal("Pune; Remote India", job.Str("location"));
        Assert.Equal("onSite", job.Str("workMode"));
        Assert.Equal("fullTime", job.Str("employmentType"));
        Assert.Equal("Platform", job.Str("department"));
        Assert.Equal(5, job.Body.GetProperty("experienceMinYears").GetInt32());
        Assert.Equal(10, job.Body.GetProperty("experienceMaxYears").GetInt32());
        Assert.Equal("R-42", job.Str("requisitionId"));
        Assert.Equal("2026-11-10", job.Str("postedDate"));
        Assert.Equal("Plain text.\nSecond line.", job.Str("description"));
        Assert.Equal("Company site", job.Str("foundOn"));
    }

    [Fact]
    public async Task Jobs_of_one_employer_share_the_canonical_employer_whatever_the_spelling()
    {
        var first = await CreateJobAsync("Initech", "Role One");
        var second = await CreateJobAsync("  INITECH ", "Role Two");

        Assert.Equal(first.Body.GetProperty("employer").GetProperty("id").GetGuid(), second.Body.GetProperty("employer").GetProperty("id").GetGuid());
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Create_uses_an_employer_that_already_exists()
    {
        var employerId = await CreateEmployerAsync("Hooli");

        var reply = await CreateJobAsync("hooli", "Engineer");

        Assert.Equal(employerId, reply.Body.GetProperty("employer").GetProperty("id").GetGuid());
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_job()
    {
        AssertProblem(await GetAsync($"/api/jobs/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
    }

    // ---- validation -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_reports_every_problem_of_a_request_together()
    {
        var reply = await PostAsync("/api/jobs", new
        {
            employer = " ",
            title = "",
            applyUrl = "ftp://example.com/job",
            workMode = "office",
            employmentType = "forever",
            experienceMinYears = 9,
            experienceMaxYears = 2,
        });

        AssertInvalid(reply, "employer");
        foreach (var field in new[] { "title", "applyUrl", "workMode", "employmentType", "experienceMaxYears" })
        {
            Assert.True(reply.Body.GetProperty("errors").TryGetProperty(field, out _), $"No validation error for '{field}'.");
        }

        Assert.Equal(0, (await GetAsync("/api/jobs")).Count);
        Assert.Equal(0, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Create_rejects_a_missing_body_content_a_missing_title_and_values_over_the_limits()
    {
        AssertInvalid(await PostAsync("/api/jobs", new { }), "employer");
        AssertInvalid(await PostAsync("/api/jobs", new { employer = "Acme" }), "title");
        AssertInvalid(await CreateJobAsync("Acme", new string('x', 201)), "title");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { location = new string('x', 201) }), "location");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { department = new string('x', 201) }), "department");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { requisitionId = new string('x', 101) }), "requisitionId");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { foundOn = new string('x', 201) }), "foundOn");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { description = new string('x', 50_001) }), "description");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { experienceMinYears = -1 }), "experienceMinYears");
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { experienceMaxYears = 61 }), "experienceMaxYears");
    }

    [Theory]
    [InlineData("example.com/job")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:secret@example.com/job")]
    public async Task Create_rejects_a_job_url_that_is_not_http_or_https(string url)
    {
        AssertInvalid(await CreateJobAsync("Acme", "Role", new { applyUrl = url }), "applyUrl");
    }

    [Fact]
    public async Task Blank_optional_fields_are_stored_as_unset()
    {
        var reply = await CreateJobAsync("Acme", "Role", new { applyUrl = " ", location = "", workMode = " ", employmentType = "", department = " " });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        foreach (var field in new[] { "applyUrl", "location", "workMode", "employmentType", "department" })
        {
            Assert.Equal(JsonValueKind.Null, reply.Body.GetProperty(field).ValueKind);
        }
    }

    // ---- duplicates -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_same_employer_and_requisition_id_is_blocked_and_cannot_be_overridden(bool confirm)
    {
        var existing = await NewJobAsync("Acme", "Role A", new { requisitionId = "R-1" });

        var reply = await CreateJobAsync("ACME", "Totally different title", new { requisitionId = " r-1 ", confirmDifferentJob = confirm });

        AssertProblem(reply, HttpStatusCode.Conflict, "duplicate_requisition");
        Assert.Equal(existing, reply.Body.GetProperty("existingId").GetGuid());
        Assert.Equal("requisitionId", reply.Body.GetProperty("matchedOn").GetString());
        Assert.Equal(1, (await GetAsync("/api/jobs")).Count);
    }

    [Fact]
    public async Task The_same_requisition_id_at_another_employer_is_a_different_job()
    {
        await NewJobAsync("Acme", "Role", new { requisitionId = "R-1" });

        var reply = await CreateJobAsync("Globex", "Role", new { requisitionId = "R-1" });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    [Fact]
    public async Task Different_requisition_ids_never_match_even_with_the_same_title_location_and_url()
    {
        await NewJobAsync("Acme", "Engineer", new { location = "Pune", applyUrl = "https://example.com/jobs/1", requisitionId = "R-1" });

        var reply = await CreateJobAsync("Acme", "Engineer", new { location = "Pune", applyUrl = "https://example.com/jobs/1", requisitionId = "R-2" });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    [Theory]
    [InlineData("https://example.com/jobs/1")]
    [InlineData("https://EXAMPLE.com/jobs/1/")]
    [InlineData("https://www.example.com/jobs/1#apply")]
    [InlineData("https://example.com/jobs/1?utm_source=newsletter&fbclid=abc")]
    public async Task The_same_canonical_url_is_a_warning_with_the_existing_job_id(string variant)
    {
        var existing = await NewJobAsync("Acme", "Engineer", new { applyUrl = "https://example.com/jobs/1" });

        var reply = await CreateJobAsync("Globex", "Other title", new { applyUrl = variant });

        AssertProblem(reply, HttpStatusCode.Conflict, "possible_duplicate");
        Assert.Equal(existing, reply.Body.GetProperty("existingId").GetGuid());
        Assert.Equal("canonicalUrl", reply.Body.GetProperty("matchedOn").GetString());
        Assert.Equal(1, (await GetAsync("/api/jobs")).Count);
        Assert.Equal(1, (await GetAsync("/api/employers")).Count); // the rejected job did not create the employer
    }

    [Fact]
    public async Task A_url_that_differs_in_a_job_identifying_parameter_is_not_a_duplicate()
    {
        await NewJobAsync("Acme", "Engineer", new { applyUrl = "https://boards.example.com/acme?gh_jid=1" });

        var reply = await CreateJobAsync("Acme", "Designer", new { applyUrl = "https://boards.example.com/acme?gh_jid=2" });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
    }

    [Fact]
    public async Task The_same_employer_title_and_location_is_a_warning_that_the_client_can_confirm()
    {
        var existing = await NewJobAsync("Acme", "Backend Engineer", new { location = "Pune" });

        var warning = await CreateJobAsync("acme", "  BACKEND   engineer ", new { location = " pune " });
        var confirmed = await CreateJobAsync("acme", "Backend Engineer", new { location = "Pune", confirmDifferentJob = true });

        AssertProblem(warning, HttpStatusCode.Conflict, "possible_duplicate");
        Assert.Equal(existing, warning.Body.GetProperty("existingId").GetGuid());
        Assert.Equal("employerTitleLocation", warning.Body.GetProperty("matchedOn").GetString());
        Assert.Equal(HttpStatusCode.Created, confirmed.Status);
        Assert.Equal(2, (await GetAsync("/api/jobs")).Count);
    }

    [Fact]
    public async Task A_confirmed_url_duplicate_is_created()
    {
        await NewJobAsync("Acme", "Engineer", new { applyUrl = "https://example.com/jobs/1" });

        var confirmed = await CreateJobAsync("Acme", "Engineer II", new { applyUrl = "https://example.com/jobs/1", confirmDifferentJob = true });

        Assert.Equal(HttpStatusCode.Created, confirmed.Status);
    }

    [Fact]
    public async Task A_different_title_or_location_or_employer_is_not_a_duplicate()
    {
        await NewJobAsync("Acme", "Backend Engineer", new { location = "Pune" });

        Assert.Equal(HttpStatusCode.Created, (await CreateJobAsync("Acme", "Frontend Engineer", new { location = "Pune" })).Status);
        Assert.Equal(HttpStatusCode.Created, (await CreateJobAsync("Acme", "Backend Engineer", new { location = "Mumbai" })).Status);
        Assert.Equal(HttpStatusCode.Created, (await CreateJobAsync("Globex", "Backend Engineer", new { location = "Pune" })).Status);
    }

    // ---- list and search ------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_returns_newest_first_with_paging()
    {
        var first = await NewJobAsync("Acme", "Role 1");
        var second = await NewJobAsync("Acme", "Role 2");
        var third = await NewJobAsync("Acme", "Role 3");

        var all = await GetAsync("/api/jobs");
        var page = await GetAsync("/api/jobs?skip=1&take=1");

        Assert.Equal(3, all.Count);
        Assert.Equal(third, all[0].GetProperty("id").GetGuid());
        Assert.Equal(first, all[2].GetProperty("id").GetGuid());
        Assert.Equal(1, page.Count);
        Assert.Equal(second, page[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task List_rejects_invalid_paging_and_invalid_filters()
    {
        AssertInvalid(await GetAsync("/api/jobs?skip=-1"), "skip");
        AssertInvalid(await GetAsync("/api/jobs?take=0"), "take");
        AssertInvalid(await GetAsync("/api/jobs?take=501"), "take");
        AssertInvalid(await GetAsync("/api/jobs?status=bogus"), "status");
        AssertInvalid(await GetAsync("/api/jobs?workMode=office"), "workMode");
        AssertInvalid(await GetAsync("/api/jobs?employmentType=forever"), "employmentType");
    }

    [Fact]
    public async Task List_searches_title_and_employer_and_filters_by_status_work_mode_type_and_employer()
    {
        var acme = await NewJobAsync("Acme Corp", "Backend Engineer", new { workMode = "remote", employmentType = "contract" });
        await NewJobAsync("Globex", "Data Analyst", new { workMode = "onSite", employmentType = "fullTime" });
        await NewJobAsync("Globex", "Backend Developer");
        var closed = await NewJobAsync("Initech", "Support Engineer");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync($"/api/jobs/{closed}/status", new { status = "closed" })).Status);

        Assert.Equal(2, (await GetAsync("/api/jobs?q=%20BACKEND")).Count);
        Assert.Equal(2, (await GetAsync("/api/jobs?q=globex")).Count);
        Assert.Equal(1, (await GetAsync("/api/jobs?q=acme")).Count);
        Assert.Equal(1, (await GetAsync("/api/jobs?workMode=REMOTE")).Count);
        Assert.Equal(1, (await GetAsync("/api/jobs?employmentType=contract")).Count);
        Assert.Equal(1, (await GetAsync("/api/jobs?status=closed")).Count);
        Assert.Equal(3, (await GetAsync("/api/jobs?status=open")).Count);

        var employerId = (await GetAsync($"/api/jobs/{acme}")).Body.GetProperty("employer").GetProperty("id").GetGuid();
        var byEmployer = await GetAsync($"/api/jobs?employerId={employerId}");
        Assert.Equal(1, byEmployer.Count);
        Assert.Equal(acme, byEmployer[0].GetProperty("id").GetGuid());
    }

    // ---- update ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Put_replaces_the_fields_clears_omitted_optional_ones_and_keeps_status_and_creation()
    {
        var id = await NewJobAsync("Acme", "Old title", new { location = "Pune", workMode = "remote", requisitionId = "R-1", department = "X" });
        var before = await GetAsync($"/api/jobs/{id}");

        var updated = await PutAsync($"/api/jobs/{id}", new { employer = "Acme", title = "New title", employmentType = "contract" });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("New title", updated.Str("title"));
        Assert.Equal("contract", updated.Str("employmentType"));
        foreach (var field in new[] { "location", "workMode", "requisitionId", "department" })
        {
            Assert.Equal(JsonValueKind.Null, updated.Body.GetProperty(field).ValueKind);
        }

        Assert.Equal("open", updated.Str("status"));
        Assert.Equal("manual", updated.Str("createdVia"));
        Assert.Equal(before.Str("createdAtUtc"), updated.Str("createdAtUtc"));
        Assert.Equal(1, updated.Body.GetProperty("statusHistory").GetArrayLength());
        Assert.Equal("New title", (await GetAsync($"/api/jobs/{id}")).Str("title"));
    }

    [Fact]
    public async Task Put_can_move_a_job_to_a_new_employer_that_is_created_on_the_way()
    {
        var id = await NewJobAsync("Acme", "Role");

        var updated = await PutAsync($"/api/jobs/{id}", new { employer = "Brand New Corp", title = "Role" });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("Brand New Corp", updated.Body.GetProperty("employer").GetProperty("name").GetString());
        Assert.Equal(2, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Put_with_unchanged_requisition_and_url_is_not_blocked_by_the_job_it_was_confirmed_against()
    {
        await NewJobAsync("Acme", "Engineer", new { applyUrl = "https://example.com/jobs/1" });
        var second = await NewJobAsync("Acme", "Engineer II", new { applyUrl = "https://example.com/jobs/1", confirmDifferentJob = true });

        var updated = await PutAsync($"/api/jobs/{second}", new { employer = "Acme", title = "Engineer II", applyUrl = "https://example.com/jobs/1", description = "edited" });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("edited", updated.Str("description"));
    }

    [Fact]
    public async Task Put_to_another_jobs_requisition_id_is_blocked_and_changes_nothing()
    {
        await NewJobAsync("Acme", "Role A", new { requisitionId = "R-1" });
        var second = await NewJobAsync("Acme", "Role B", new { requisitionId = "R-2" });

        var reply = await PutAsync($"/api/jobs/{second}", new { employer = "Acme", title = "Role B", requisitionId = "r-1", confirmDifferentJob = true });

        AssertProblem(reply, HttpStatusCode.Conflict, "duplicate_requisition");
        Assert.Equal("R-2", (await GetAsync($"/api/jobs/{second}")).Str("requisitionId"));
    }

    [Fact]
    public async Task Put_that_clears_the_requisition_id_re_checks_title_and_location_duplicates()
    {
        await NewJobAsync("Acme", "Backend Engineer", new { location = "Pune" });
        var second = await NewJobAsync("Acme", "Backend Engineer", new { location = "Pune", requisitionId = "R-2" });
        var body = new { employer = "Acme", title = "Backend Engineer", location = "Pune" };

        var warning = await PutAsync($"/api/jobs/{second}", body);
        var confirmed = await PutAsync($"/api/jobs/{second}", new { body.employer, body.title, body.location, confirmDifferentJob = true });

        AssertProblem(warning, HttpStatusCode.Conflict, "possible_duplicate");
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
        Assert.Equal(JsonValueKind.Null, (await GetAsync($"/api/jobs/{second}")).Body.GetProperty("requisitionId").ValueKind);
    }

    [Fact]
    public async Task Put_to_another_jobs_url_warns_and_can_be_confirmed()
    {
        await NewJobAsync("Acme", "Role A", new { applyUrl = "https://example.com/jobs/1" });
        var second = await NewJobAsync("Acme", "Role B");
        var body = new { employer = "Acme", title = "Role B", applyUrl = "https://example.com/jobs/1/" };

        var warning = await PutAsync($"/api/jobs/{second}", body);
        var confirmed = await PutAsync($"/api/jobs/{second}", new { body.employer, body.title, body.applyUrl, confirmDifferentJob = true });

        AssertProblem(warning, HttpStatusCode.Conflict, "possible_duplicate");
        Assert.Equal(HttpStatusCode.OK, confirmed.Status);
    }

    [Fact]
    public async Task Put_validates_like_create_and_returns_404_for_an_unknown_job()
    {
        var id = await NewJobAsync("Acme", "Role");

        AssertInvalid(await PutAsync($"/api/jobs/{id}", new { employer = "Acme" }), "title");
        AssertProblem(await PutAsync($"/api/jobs/{Guid.NewGuid()}", new { employer = "Acme", title = "Role" }), HttpStatusCode.NotFound, "not_found");
    }

    // ---- status ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_changes_are_timestamped_history_and_open_again_sets_reopened()
    {
        var id = await NewJobAsync("Acme", "Role");

        var closed = await PostAsync($"/api/jobs/{id}/status", new { status = "closed" });
        var reopened = await PostAsync($"/api/jobs/{id}/status", new { status = "reopened" });
        var expired = await PostAsync($"/api/jobs/{id}/status", new { status = "expired" });

        Assert.Equal(HttpStatusCode.OK, closed.Status);
        Assert.Equal("closed", closed.Str("status"));
        Assert.Equal("reopened", reopened.Str("status"));
        Assert.Equal("expired", expired.Str("status"));

        var history = (await GetAsync($"/api/jobs/{id}")).Body.GetProperty("statusHistory");
        var steps = history.EnumerateArray()
            .Select(h => $"{h.GetProperty("fromStatus").GetString() ?? "-"}>{h.GetProperty("toStatus").GetString()}")
            .ToArray();
        Assert.Equal(new[] { "->open", "open>closed", "closed>reopened", "reopened>expired" }, steps);
        var times = history.EnumerateArray().Select(h => h.GetProperty("changedAtUtc").GetDateTimeOffset()).ToArray();
        Assert.Equal(times.OrderBy(t => t), times);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("expired")]
    public async Task A_job_can_be_set_to_expired_or_unavailable_and_then_between_the_closed_states(string status)
    {
        var id = await NewJobAsync("Acme", "Role");

        Assert.Equal(status, (await PostAsync($"/api/jobs/{id}/status", new { status })).Str("status"));
        Assert.Equal("closed", (await PostAsync($"/api/jobs/{id}/status", new { status = "closed" })).Str("status"));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("new")]
    [InlineData("verificationPending")]
    [InlineData("bogus")]
    [InlineData("")]
    public async Task Only_closed_expired_unavailable_and_reopened_can_be_requested(string status)
    {
        var id = await NewJobAsync("Acme", "Role");

        AssertInvalid(await PostAsync($"/api/jobs/{id}/status", new { status }), "status");
        Assert.Equal("open", (await GetAsync($"/api/jobs/{id}")).Str("status"));
    }

    [Fact]
    public async Task Invalid_transitions_are_409_and_leave_the_job_unchanged()
    {
        var id = await NewJobAsync("Acme", "Role");

        var reopenOpen = await PostAsync($"/api/jobs/{id}/status", new { status = "reopened" });
        await PostAsync($"/api/jobs/{id}/status", new { status = "closed" });
        var closeAgain = await PostAsync($"/api/jobs/{id}/status", new { status = "closed" });

        AssertProblem(reopenOpen, HttpStatusCode.Conflict, "invalid_status_transition");
        AssertProblem(closeAgain, HttpStatusCode.Conflict, "invalid_status_transition");
        var job = await GetAsync($"/api/jobs/{id}");
        Assert.Equal("closed", job.Str("status"));
        Assert.Equal(2, job.Body.GetProperty("statusHistory").GetArrayLength());
    }

    [Fact]
    public async Task Status_returns_404_for_an_unknown_job()
    {
        AssertProblem(await PostAsync($"/api/jobs/{Guid.NewGuid()}/status", new { status = "closed" }), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_status_in_a_put_or_create_body_is_ignored()
    {
        var id = await NewJobAsync("Acme", "Role", new { status = "closed" });

        var updated = await PutAsync($"/api/jobs/{id}", new { employer = "Acme", title = "Role", status = "expired" });

        Assert.Equal("open", (await GetAsync($"/api/jobs/{id}")).Str("status"));
        Assert.Equal("open", updated.Str("status"));
    }

    // ---- delete ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_a_job_that_has_no_application_or_source_listing_and_keeps_the_employer()
    {
        var id = await NewJobAsync("Acme", "Role");

        var deleted = await DeleteAsync($"/api/jobs/{id}");
        var again = await DeleteAsync($"/api/jobs/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        AssertProblem(again, HttpStatusCode.NotFound, "not_found");
        AssertProblem(await GetAsync($"/api/jobs/{id}"), HttpStatusCode.NotFound, "not_found");
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task An_employer_that_has_jobs_cannot_be_deleted_until_the_jobs_are_gone()
    {
        var jobId = await NewJobAsync("Acme", "Role");
        var employerId = (await GetAsync($"/api/jobs/{jobId}")).Body.GetProperty("employer").GetProperty("id").GetGuid();

        var blocked = await DeleteAsync($"/api/employers/{employerId}");
        await DeleteAsync($"/api/jobs/{jobId}");
        var allowed = await DeleteAsync($"/api/employers/{employerId}");

        AssertProblem(blocked, HttpStatusCode.Conflict, "employer_has_jobs");
        Assert.Equal(1, blocked.Body.GetProperty("jobCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, allowed.Status);
    }
}
