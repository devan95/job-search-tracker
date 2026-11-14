using System.Net;
using System.Text.Json;

namespace JobSearchTracker.Api.Tests;

public class SearchProfileApiTests : ApiTestBase
{
    [Fact]
    public async Task Create_returns_201_and_a_draft_with_both_discovery_modes_off_is_valid()
    {
        var candidateId = await CreateCandidateAsync();

        var reply = await PostAsync("/api/search-profiles", new { candidateId, name = "  Backend roles " });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/search-profiles/{reply.Id}", reply.Location);
        Assert.Equal("Backend roles", reply.Str("name"));
        Assert.Equal(candidateId, reply.Body.GetProperty("candidateId").GetGuid());
        Assert.False(reply.Body.GetProperty("companyMonitoringEnabled").GetBoolean());
        Assert.False(reply.Body.GetProperty("broadSearchEnabled").GetBoolean());
        Assert.Equal(1, reply.Body.GetProperty("profileVersion").GetInt32());
        Assert.Equal(0, reply.Body.GetProperty("companyLists").GetArrayLength());
    }

    [Fact]
    public async Task Work_modes_null_inherits_while_an_empty_set_is_an_explicit_value()
    {
        var candidateId = await CreateCandidateAsync();

        var inherit = await PostAsync("/api/search-profiles", new { candidateId, name = "Inherit" });
        var empty = await PostAsync("/api/search-profiles", new { candidateId, name = "Empty", workModes = Array.Empty<string>() });
        var some = await PostAsync("/api/search-profiles", new { candidateId, name = "Some", workModes = new[] { "Remote", "hybrid", "remote" } });

        Assert.Equal(JsonValueKind.Null, inherit.Body.GetProperty("workModes").ValueKind);
        Assert.Equal(JsonValueKind.Array, empty.Body.GetProperty("workModes").ValueKind);
        Assert.Empty(empty.Strings("workModes"));
        Assert.Equal(new[] { "remote", "hybrid" }, some.Strings("workModes"));
    }

    [Fact]
    public async Task Create_with_discovery_flags_and_several_lists_returns_each_list_with_its_defaults()
    {
        var candidateId = await CreateCandidateAsync();
        var listB = await CreateListAsync("B list", defaultWorkModes: new[] { "onSite" });
        var listA = await CreateListAsync("A list");

        var reply = await PostAsync("/api/search-profiles", new
        {
            candidateId,
            name = "Both modes",
            companyMonitoringEnabled = true,
            broadSearchEnabled = true,
            companyListIds = new[] { listB, listA, listB },
        });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.True(reply.Body.GetProperty("companyMonitoringEnabled").GetBoolean());
        Assert.True(reply.Body.GetProperty("broadSearchEnabled").GetBoolean());
        var lists = reply.Body.GetProperty("companyLists");
        Assert.Equal(2, lists.GetArrayLength());
        Assert.Equal("A list", lists[0].GetProperty("name").GetString());
        Assert.Empty(lists[0].GetProperty("defaultWorkModes").EnumerateArray());
        Assert.Equal("B list", lists[1].GetProperty("name").GetString());
        Assert.Equal("onSite", lists[1].GetProperty("defaultWorkModes")[0].GetString());
    }

    [Fact]
    public async Task A_list_can_be_shared_by_several_profiles_and_a_profile_can_select_several_lists()
    {
        var candidateId = await CreateCandidateAsync();
        var shared = await CreateListAsync("Shared");
        var second = await CreateListAsync("Second");
        var profileOne = await CreateProfileAsync(candidateId, "One", shared, second);
        var profileTwo = await CreateProfileAsync(candidateId, "Two", shared);

        Assert.Equal(2, (await GetAsync($"/api/search-profiles/{profileOne}")).Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(1, (await GetAsync($"/api/search-profiles/{profileTwo}")).Body.GetProperty("companyLists").GetArrayLength());
    }

    [Fact]
    public async Task Create_reports_a_duplicate_name_for_the_same_candidate_as_409_but_allows_it_for_another_candidate()
    {
        var first = await CreateCandidateAsync("First");
        var second = await CreateCandidateAsync("Second");
        await CreateProfileAsync(first, "Data roles");

        var duplicate = await PostAsync("/api/search-profiles", new { candidateId = first, name = "  DATA   roles " });
        var otherCandidate = await PostAsync("/api/search-profiles", new { candidateId = second, name = "Data roles" });

        AssertProblem(duplicate, HttpStatusCode.Conflict, "duplicate_name");
        Assert.Equal(HttpStatusCode.Created, otherCandidate.Status);
        Assert.Equal(1, (await GetAsync($"/api/search-profiles?candidateId={first}")).Count);
    }

    [Fact]
    public async Task Create_rejects_invalid_input_without_saving_anything()
    {
        var candidateId = await CreateCandidateAsync();

        var blank = await PostAsync("/api/search-profiles", new { candidateId, name = " " });
        var tooLong = await PostAsync("/api/search-profiles", new { candidateId, name = new string('x', 201) });
        var noCandidate = await PostAsync("/api/search-profiles", new { name = "No candidate" });
        var emptyCandidate = await PostAsync("/api/search-profiles", new { candidateId = Guid.Empty, name = "Empty candidate" });
        var badMode = await PostAsync("/api/search-profiles", new { candidateId, name = "Bad mode", workModes = new[] { "office" } });

        AssertInvalid(blank, "name");
        AssertInvalid(tooLong, "name");
        AssertInvalid(noCandidate, "candidateId");
        AssertInvalid(emptyCandidate, "candidateId");
        AssertInvalid(badMode, "workModes");
        Assert.Equal(0, (await GetAsync("/api/search-profiles")).Count);
    }

    [Fact]
    public async Task Create_rejects_an_unknown_candidate_or_an_unknown_list()
    {
        var candidateId = await CreateCandidateAsync();

        var unknownCandidate = await PostAsync("/api/search-profiles", new { candidateId = Guid.NewGuid(), name = "X" });
        var unknownList = await PostAsync("/api/search-profiles", new { candidateId, name = "Y", companyListIds = new[] { Guid.NewGuid() } });

        AssertInvalid(unknownCandidate, "candidateId");
        AssertInvalid(unknownList, "companyListIds");
        Assert.Equal(0, (await GetAsync("/api/search-profiles")).Count);
    }

    [Fact]
    public async Task Get_returns_the_profile_and_404_for_an_unknown_id()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Findable");

        var found = await GetAsync($"/api/search-profiles/{profileId}");
        var missing = await GetAsync($"/api/search-profiles/{Guid.NewGuid()}");

        Assert.Equal("Findable", found.Str("name"));
        AssertProblem(missing, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task List_can_be_filtered_by_candidate_and_ordered_by_name()
    {
        var first = await CreateCandidateAsync("First");
        var second = await CreateCandidateAsync("Second");
        await CreateProfileAsync(first, "Zeta");
        await CreateProfileAsync(first, "Alpha");
        await CreateProfileAsync(second, "Other");

        var forFirst = await GetAsync($"/api/search-profiles?candidateId={first}");
        var all = await GetAsync("/api/search-profiles");

        Assert.Equal(2, forFirst.Count);
        Assert.Equal("Alpha", forFirst[0].GetProperty("name").GetString());
        Assert.Equal("Zeta", forFirst[1].GetProperty("name").GetString());
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Update_changes_name_work_modes_and_flags_and_both_flags_may_be_false()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Original");

        var changed = await PutAsync($"/api/search-profiles/{profileId}", new
        {
            name = "Renamed",
            workModes = new[] { "remote" },
            companyMonitoringEnabled = true,
            broadSearchEnabled = true,
        });
        var draft = await PutAsync($"/api/search-profiles/{profileId}", new
        {
            name = "Renamed",
            workModes = Array.Empty<string>(),
            companyMonitoringEnabled = false,
            broadSearchEnabled = false,
        });
        var inherit = await PutAsync($"/api/search-profiles/{profileId}", new
        {
            name = "Renamed",
            companyMonitoringEnabled = false,
            broadSearchEnabled = false,
        });

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal("Renamed", changed.Str("name"));
        Assert.Equal(new[] { "remote" }, changed.Strings("workModes"));
        Assert.True(changed.Body.GetProperty("companyMonitoringEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Array, draft.Body.GetProperty("workModes").ValueKind);
        Assert.Empty(draft.Strings("workModes"));
        Assert.False(draft.Body.GetProperty("companyMonitoringEnabled").GetBoolean());
        Assert.False(draft.Body.GetProperty("broadSearchEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, inherit.Body.GetProperty("workModes").ValueKind);
    }

    [Fact]
    public async Task Update_requires_both_flags_and_a_valid_name_and_does_not_increment_the_profile_version()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Versioned");

        var noFlags = await PutAsync($"/api/search-profiles/{profileId}", new { name = "Versioned" });
        var blank = await PutAsync($"/api/search-profiles/{profileId}", new { name = "", companyMonitoringEnabled = true, broadSearchEnabled = true });
        var ok = await PutAsync($"/api/search-profiles/{profileId}", new { name = "Versioned", companyMonitoringEnabled = true, broadSearchEnabled = false });

        AssertInvalid(noFlags, "companyMonitoringEnabled");
        AssertInvalid(noFlags, "broadSearchEnabled");
        AssertInvalid(blank, "name");
        Assert.Equal(1, ok.Body.GetProperty("profileVersion").GetInt32());
    }

    [Fact]
    public async Task Update_to_another_profiles_name_of_the_same_candidate_is_a_409()
    {
        var candidateId = await CreateCandidateAsync();
        await CreateProfileAsync(candidateId, "Alpha");
        var betaId = await CreateProfileAsync(candidateId, "Beta");

        var clash = await PutAsync($"/api/search-profiles/{betaId}", new { name = "alpha", companyMonitoringEnabled = false, broadSearchEnabled = false });
        var ownCase = await PutAsync($"/api/search-profiles/{betaId}", new { name = "BETA", companyMonitoringEnabled = false, broadSearchEnabled = false });

        AssertProblem(clash, HttpStatusCode.Conflict, "duplicate_name");
        Assert.Equal(HttpStatusCode.OK, ownCase.Status);
    }

    [Fact]
    public async Task Update_of_an_unknown_profile_is_404()
    {
        var reply = await PutAsync($"/api/search-profiles/{Guid.NewGuid()}", new { name = "X", companyMonitoringEnabled = false, broadSearchEnabled = false });

        AssertProblem(reply, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Updating_a_profile_never_changes_its_selected_lists()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("Kept");
        var profileId = await CreateProfileAsync(candidateId, "Profile", listId);

        var updated = await PutAsync($"/api/search-profiles/{profileId}", new { name = "Profile", companyMonitoringEnabled = true, broadSearchEnabled = false });

        Assert.Equal(1, updated.Body.GetProperty("companyLists").GetArrayLength());
    }

    [Fact]
    public async Task Selecting_a_list_is_idempotent_and_unselecting_is_explicit()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("Optional");
        var profileId = await CreateProfileAsync(candidateId, "Profile");

        var added = await PutAsync($"/api/search-profiles/{profileId}/company-lists/{listId}");
        var addedAgain = await PutAsync($"/api/search-profiles/{profileId}/company-lists/{listId}");
        var selected = await GetAsync($"/api/search-profiles/{profileId}");
        var removed = await DeleteAsync($"/api/search-profiles/{profileId}/company-lists/{listId}");
        var removedAgain = await DeleteAsync($"/api/search-profiles/{profileId}/company-lists/{listId}");
        var afterwards = await GetAsync($"/api/search-profiles/{profileId}");

        Assert.Equal(HttpStatusCode.NoContent, added.Status);
        Assert.Equal(HttpStatusCode.NoContent, addedAgain.Status);
        Assert.Equal(1, selected.Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        AssertProblem(removedAgain, HttpStatusCode.NotFound, "not_a_member");
        Assert.Equal(0, afterwards.Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/company-lists/{listId}")).Status);
    }

    [Fact]
    public async Task Selecting_an_unknown_list_or_for_an_unknown_profile_is_404()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("Real");
        var profileId = await CreateProfileAsync(candidateId, "Real profile");

        AssertProblem(await PutAsync($"/api/search-profiles/{profileId}/company-lists/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/search-profiles/{Guid.NewGuid()}/company-lists/{listId}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/search-profiles/{Guid.NewGuid()}/company-lists/{listId}"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Delete_removes_the_profile_and_its_selections_but_keeps_candidate_and_lists()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("Survivor list");
        var profileId = await CreateProfileAsync(candidateId, "Doomed", listId);

        var deleted = await DeleteAsync($"/api/search-profiles/{profileId}");
        var again = await DeleteAsync($"/api/search-profiles/{profileId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        AssertProblem(again, HttpStatusCode.NotFound, "not_found");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/candidates/{candidateId}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/company-lists/{listId}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/company-lists/{listId}")).Status);
    }
}
