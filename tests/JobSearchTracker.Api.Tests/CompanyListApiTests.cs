using System.Net;

namespace JobSearchTracker.Api.Tests;

public class CompanyListApiTests : ApiTestBase
{
    [Fact]
    public async Task Create_returns_201_with_label_and_work_mode_defaults()
    {
        var reply = await PostAsync("/api/company-lists", new
        {
            name = "  Gujarat startups ",
            locationLabel = " Ahmedabad ",
            defaultWorkModes = new[] { "Hybrid", "remote" },
        });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/company-lists/{reply.Id}", reply.Location);
        Assert.Equal("Gujarat startups", reply.Str("name"));
        Assert.Equal("Ahmedabad", reply.Str("locationLabel"));
        Assert.Equal(new[] { "remote", "hybrid" }, reply.Strings("defaultWorkModes"));
        Assert.Equal(0, reply.Body.GetProperty("employerCount").GetInt32());
    }

    [Fact]
    public async Task Default_work_modes_are_never_null_and_an_omitted_value_is_the_empty_set()
    {
        var omitted = await PostAsync("/api/company-lists", new { name = "No defaults" });
        var explicitNull = await PostAsync("/api/company-lists", new { name = "Null defaults", defaultWorkModes = (string[]?)null });
        var empty = await PostAsync("/api/company-lists", new { name = "Empty defaults", defaultWorkModes = Array.Empty<string>() });

        foreach (var reply in new[] { omitted, explicitNull, empty })
        {
            Assert.Equal(HttpStatusCode.Created, reply.Status);
            Assert.Equal(System.Text.Json.JsonValueKind.Array, reply.Body.GetProperty("defaultWorkModes").ValueKind);
            Assert.Empty(reply.Strings("defaultWorkModes"));
        }

        Assert.Null(omitted.Body.GetProperty("locationLabel").GetString());
    }

    [Theory]
    [InlineData("gujarat startups")]
    [InlineData("  GUJARAT   Startups ")]
    public async Task Create_reports_a_duplicate_after_normalization_as_409(string variant)
    {
        await CreateListAsync("Gujarat startups");

        var reply = await PostAsync("/api/company-lists", new { name = variant });

        AssertProblem(reply, HttpStatusCode.Conflict, "duplicate_name");
        Assert.Equal(1, (await GetAsync("/api/company-lists")).Count);
    }

    [Fact]
    public async Task Create_rejects_invalid_input_and_reports_every_field()
    {
        var reply = await PostAsync("/api/company-lists", new
        {
            name = " ",
            locationLabel = new string('x', 201),
            defaultWorkModes = new[] { "remote", "moon base" },
        });

        AssertInvalid(reply, "name");
        AssertInvalid(reply, "locationLabel");
        AssertInvalid(reply, "defaultWorkModes");
        Assert.Empty((await GetAsync("/api/company-lists")).Body.EnumerateArray());
    }

    [Fact]
    public async Task Numeric_work_mode_strings_are_not_accepted()
    {
        var reply = await PostAsync("/api/company-lists", new { name = "Numbers", defaultWorkModes = new[] { "3" } });

        AssertInvalid(reply, "defaultWorkModes");
    }

    [Fact]
    public async Task Get_returns_employers_ordered_by_name_and_404_for_an_unknown_id()
    {
        var listId = await CreateListAsync("Fintech");
        await AddEmployerToListAsync(listId, await CreateEmployerAsync("Zeta Pay"));
        await AddEmployerToListAsync(listId, await CreateEmployerAsync("Alpha Bank"));

        var detail = await GetAsync($"/api/company-lists/{listId}");
        var missing = await GetAsync($"/api/company-lists/{Guid.NewGuid()}");

        Assert.Equal(2, detail.Body.GetProperty("employerCount").GetInt32());
        var employers = detail.Body.GetProperty("employers");
        Assert.Equal("Alpha Bank", employers[0].GetProperty("name").GetString());
        Assert.Equal("Zeta Pay", employers[1].GetProperty("name").GetString());
        AssertProblem(missing, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task List_returns_summaries_with_employer_counts_ordered_by_name()
    {
        var bigId = await CreateListAsync("B list");
        await CreateListAsync("A list", "Pune", new[] { "onSite" });
        await AddEmployerToListAsync(bigId, await CreateEmployerAsync("One"));
        await AddEmployerToListAsync(bigId, await CreateEmployerAsync("Two"));

        var lists = await GetAsync("/api/company-lists");

        Assert.Equal(2, lists.Count);
        Assert.Equal("A list", lists[0].GetProperty("name").GetString());
        Assert.Equal(0, lists[0].GetProperty("employerCount").GetInt32());
        Assert.Equal("Pune", lists[0].GetProperty("locationLabel").GetString());
        Assert.Equal(2, lists[1].GetProperty("employerCount").GetInt32());
    }

    [Fact]
    public async Task Update_changes_name_label_and_defaults_and_a_missing_label_or_defaults_are_cleared()
    {
        var id = await CreateListAsync("Original", "Surat", new[] { "remote" });

        var changed = await PutAsync($"/api/company-lists/{id}", new { name = "Renamed", locationLabel = "Rajkot", defaultWorkModes = new[] { "hybrid", "onSite" } });
        var cleared = await PutAsync($"/api/company-lists/{id}", new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal("Renamed", changed.Str("name"));
        Assert.Equal("Rajkot", changed.Str("locationLabel"));
        Assert.Equal(new[] { "onSite", "hybrid" }, changed.Strings("defaultWorkModes"));
        Assert.Null(cleared.Body.GetProperty("locationLabel").GetString());
        Assert.Empty(cleared.Strings("defaultWorkModes"));
    }

    [Fact]
    public async Task Update_to_another_lists_name_is_a_409_but_changing_only_the_case_of_its_own_name_is_fine()
    {
        await CreateListAsync("Alpha");
        var betaId = await CreateListAsync("Beta");

        var clash = await PutAsync($"/api/company-lists/{betaId}", new { name = " ALPHA " });
        var ownCase = await PutAsync($"/api/company-lists/{betaId}", new { name = "BETA" });

        AssertProblem(clash, HttpStatusCode.Conflict, "duplicate_name");
        Assert.Equal(HttpStatusCode.OK, ownCase.Status);
        Assert.Equal("BETA", ownCase.Str("name"));
    }

    [Fact]
    public async Task Update_of_an_unknown_list_is_404_and_invalid_input_is_400()
    {
        var id = await CreateListAsync("Real");

        AssertProblem(await PutAsync($"/api/company-lists/{Guid.NewGuid()}", new { name = "X" }), HttpStatusCode.NotFound, "not_found");
        AssertInvalid(await PutAsync($"/api/company-lists/{id}", new { name = "" }), "name");
    }

    [Fact]
    public async Task One_employer_can_be_in_many_lists_and_adding_twice_is_a_no_op()
    {
        var employerId = await CreateEmployerAsync("Shared Co");
        var listA = await CreateListAsync("List A");
        var listB = await CreateListAsync("List B");

        await AddEmployerToListAsync(listA, employerId);
        await AddEmployerToListAsync(listA, employerId);
        await AddEmployerToListAsync(listB, employerId);

        Assert.Equal(1, (await GetAsync($"/api/company-lists/{listA}")).Body.GetProperty("employerCount").GetInt32());
        Assert.Equal(1, (await GetAsync($"/api/company-lists/{listB}")).Body.GetProperty("employerCount").GetInt32());
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Adding_an_unknown_employer_or_to_an_unknown_list_is_404_and_changes_nothing()
    {
        var listId = await CreateListAsync("Real");
        var employerId = await CreateEmployerAsync("Real Co");

        AssertProblem(await PutAsync($"/api/company-lists/{listId}/employers/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/company-lists/{Guid.NewGuid()}/employers/{employerId}"), HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, (await GetAsync($"/api/company-lists/{listId}")).Body.GetProperty("employerCount").GetInt32());
    }

    [Fact]
    public async Task Removing_an_association_is_explicit_keeps_the_employer_and_404s_for_a_non_member()
    {
        var listId = await CreateListAsync("L");
        var employerId = await CreateEmployerAsync("E");
        var otherEmployerId = await CreateEmployerAsync("Other");
        await AddEmployerToListAsync(listId, employerId);

        var removed = await DeleteAsync($"/api/company-lists/{listId}/employers/{employerId}");
        var notMember = await DeleteAsync($"/api/company-lists/{listId}/employers/{otherEmployerId}");
        var unknownList = await DeleteAsync($"/api/company-lists/{Guid.NewGuid()}/employers/{employerId}");

        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        AssertProblem(notMember, HttpStatusCode.NotFound, "not_a_member");
        AssertProblem(unknownList, HttpStatusCode.NotFound, "not_found");
        Assert.Equal(0, (await GetAsync($"/api/company-lists/{listId}")).Body.GetProperty("employerCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/employers/{employerId}")).Status);
    }

    [Fact]
    public async Task Updating_a_list_never_changes_its_membership()
    {
        var listId = await CreateListAsync("Stable");
        await AddEmployerToListAsync(listId, await CreateEmployerAsync("Member"));

        var updated = await PutAsync($"/api/company-lists/{listId}", new { name = "Stable", locationLabel = "Anywhere" });

        Assert.Equal(1, updated.Body.GetProperty("employerCount").GetInt32());
    }

    [Fact]
    public async Task Delete_removes_the_list_and_its_links_but_keeps_the_employers()
    {
        var listId = await CreateListAsync("Temporary");
        var employerId = await CreateEmployerAsync("Survivor");
        await AddEmployerToListAsync(listId, employerId);

        var deleted = await DeleteAsync($"/api/company-lists/{listId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        AssertProblem(await GetAsync($"/api/company-lists/{listId}"), HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/employers/{employerId}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/employers/{employerId}")).Status);
    }

    [Fact]
    public async Task Delete_is_restricted_while_a_profile_selects_the_list_and_allowed_after_unselecting()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("In use");
        var profileId = await CreateProfileAsync(candidateId, "Profile", listId);

        var blocked = await DeleteAsync($"/api/company-lists/{listId}");
        var stillThere = await GetAsync($"/api/company-lists/{listId}");
        var unselected = await DeleteAsync($"/api/search-profiles/{profileId}/company-lists/{listId}");
        var nowAllowed = await DeleteAsync($"/api/company-lists/{listId}");

        AssertProblem(blocked, HttpStatusCode.Conflict, "company_list_in_use");
        Assert.Equal(1, blocked.Body.GetProperty("searchProfileCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, stillThere.Status);
        Assert.Equal(HttpStatusCode.NoContent, unselected.Status);
        Assert.Equal(HttpStatusCode.NoContent, nowAllowed.Status);
    }
}
