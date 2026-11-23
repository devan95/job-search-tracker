using System.Net;

namespace JobSearchTracker.Api.Tests;

/// <summary>A search profile selects company lists, role lists and location sets independently of each other.</summary>
public class ProfileSelectionApiTests : ApiTestBase
{
    private static readonly string[] RoleListNames = ["A roles", "B roles"];
    private static readonly string[] LocationSetNames = ["A places", "B places"];
    private static readonly string[] RemoteOnly = ["remote"];

    private async Task<Guid> CreateRoleListAsync(string name) => (await PostAsync("/api/role-lists", new { name })).Id;
    private async Task<Guid> CreateLocationSetAsync(string name) => (await PostAsync("/api/location-sets", new { name })).Id;

    [Fact]
    public async Task A_new_profile_selects_nothing_and_returns_empty_role_list_and_location_set_arrays()
    {
        var profile = await PostAsync("/api/search-profiles", new { candidateId = await CreateCandidateAsync(), name = "Draft" });

        Assert.Equal(0, profile.Body.GetProperty("roleLists").GetArrayLength());
        Assert.Equal(0, profile.Body.GetProperty("locationSets").GetArrayLength());
    }

    [Fact]
    public async Task Create_can_select_role_lists_and_location_sets_and_returns_them_in_name_order()
    {
        var candidateId = await CreateCandidateAsync();
        var roleB = await CreateRoleListAsync("B roles");
        var roleA = await CreateRoleListAsync("A roles");
        var setB = await CreateLocationSetAsync("B places");
        var setA = await CreateLocationSetAsync("A places");

        var created = await PostAsync("/api/search-profiles", new
        {
            candidateId,
            name = "Full",
            roleListIds = new[] { roleB, roleA, roleA },
            locationSetIds = new[] { setB, setA },
        });

        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(RoleListNames, created.Body.GetProperty("roleLists").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToArray());
        Assert.Equal(LocationSetNames, created.Body.GetProperty("locationSets").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToArray());
        var listed = await GetAsync($"/api/search-profiles?candidateId={candidateId}");
        Assert.Equal(2, listed[0].GetProperty("roleLists").GetArrayLength());
        Assert.Equal(2, listed[0].GetProperty("locationSets").GetArrayLength());
    }

    [Fact]
    public async Task Create_with_an_unknown_role_list_or_location_set_is_a_validation_error()
    {
        var candidateId = await CreateCandidateAsync();

        AssertInvalid(await PostAsync("/api/search-profiles", new { candidateId, name = "X", roleListIds = new[] { Guid.NewGuid() } }), "roleListIds");
        AssertInvalid(await PostAsync("/api/search-profiles", new { candidateId, name = "Y", locationSetIds = new[] { Guid.NewGuid() } }), "locationSetIds");
    }

    [Fact]
    public async Task Role_lists_location_sets_and_company_lists_are_selected_and_unselected_independently()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Profile");
        var company = await CreateListAsync("Companies");
        var roles = await CreateRoleListAsync("Roles");
        var places = await CreateLocationSetAsync("Places");

        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{profileId}/company-lists/{company}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{profileId}/role-lists/{roles}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{profileId}/location-sets/{places}")).Status);
        // Selecting again is a no-op.
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{profileId}/role-lists/{roles}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{profileId}/location-sets/{places}")).Status);

        var all = await GetAsync($"/api/search-profiles/{profileId}");
        Assert.Equal(1, all.Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(1, all.Body.GetProperty("roleLists").GetArrayLength());
        Assert.Equal(1, all.Body.GetProperty("locationSets").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/search-profiles/{profileId}/role-lists/{roles}")).Status);
        var afterRoles = await GetAsync($"/api/search-profiles/{profileId}");
        Assert.Equal(1, afterRoles.Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(0, afterRoles.Body.GetProperty("roleLists").GetArrayLength());
        Assert.Equal(1, afterRoles.Body.GetProperty("locationSets").GetArrayLength());

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/search-profiles/{profileId}/location-sets/{places}")).Status);
        var afterPlaces = await GetAsync($"/api/search-profiles/{profileId}");
        Assert.Equal(1, afterPlaces.Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(0, afterPlaces.Body.GetProperty("locationSets").GetArrayLength());
    }

    [Fact]
    public async Task One_role_list_and_one_location_set_can_be_selected_by_many_profiles()
    {
        var candidateId = await CreateCandidateAsync();
        var first = await CreateProfileAsync(candidateId, "First");
        var second = await CreateProfileAsync(candidateId, "Second");
        var roles = await CreateRoleListAsync("Roles");
        var places = await CreateLocationSetAsync("Places");

        foreach (var profile in new[] { first, second })
        {
            await PutAsync($"/api/search-profiles/{profile}/role-lists/{roles}");
            await PutAsync($"/api/search-profiles/{profile}/location-sets/{places}");
        }

        Assert.Equal(2, (await DeleteAsync($"/api/role-lists/{roles}")).Body.GetProperty("searchProfileCount").GetInt32());
    }

    [Fact]
    public async Task Unknown_ids_and_unselected_removals_are_404()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Profile");
        var roles = await CreateRoleListAsync("Roles");
        var places = await CreateLocationSetAsync("Places");

        AssertProblem(await PutAsync($"/api/search-profiles/{Guid.NewGuid()}/role-lists/{roles}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/search-profiles/{profileId}/role-lists/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/search-profiles/{profileId}/location-sets/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/search-profiles/{profileId}/role-lists/{roles}"), HttpStatusCode.NotFound, "not_a_member");
        AssertProblem(await DeleteAsync($"/api/search-profiles/{profileId}/location-sets/{places}"), HttpStatusCode.NotFound, "not_a_member");
        AssertProblem(await DeleteAsync($"/api/search-profiles/{Guid.NewGuid()}/location-sets/{places}"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Selecting_and_unselecting_never_changes_the_profile_version_or_work_modes()
    {
        var candidateId = await CreateCandidateAsync();
        var created = await PostAsync("/api/search-profiles", new { candidateId, name = "Versioned", workModes = RemoteOnly });
        var roles = await CreateRoleListAsync("Roles");
        var places = await CreateLocationSetAsync("Places");

        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{created.Id}/role-lists/{roles}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{created.Id}/location-sets/{places}")).Status);
        var selected = await GetAsync($"/api/search-profiles/{created.Id}");
        Assert.Equal(roles, selected.Body.GetProperty("roleLists")[0].GetProperty("id").GetGuid());
        Assert.Equal(places, selected.Body.GetProperty("locationSets")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/search-profiles/{created.Id}/role-lists/{roles}")).Status);

        var after = await GetAsync($"/api/search-profiles/{created.Id}");
        Assert.Equal(1, after.Body.GetProperty("profileVersion").GetInt32());
        Assert.Equal(RemoteOnly, after.Strings("workModes"));
    }

    [Fact]
    public async Task Deleting_a_profile_removes_its_selections_and_keeps_the_lists()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Profile");
        var roles = await CreateRoleListAsync("Roles");
        var places = await CreateLocationSetAsync("Places");
        await PutAsync($"/api/search-profiles/{profileId}/role-lists/{roles}");
        await PutAsync($"/api/search-profiles/{profileId}/location-sets/{places}");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/search-profiles/{profileId}")).Status);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/role-lists/{roles}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/api/location-sets/{places}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/role-lists/{roles}")).Status);
    }
}
