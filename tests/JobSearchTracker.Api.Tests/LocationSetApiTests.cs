using System.Net;
using System.Text.Json;

namespace JobSearchTracker.Api.Tests;

public class LocationSetApiTests : ApiTestBase
{
    private async Task<Guid> CreateSetAsync(string name)
    {
        var reply = await PostAsync("/api/location-sets", new { name });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    private Task<Reply> AddAsync(Guid setId, string label, string? scope, string? countryCode = null) =>
        PostAsync($"/api/location-sets/{setId}/locations", new { label, scope, countryCode });

    [Fact]
    public async Task Create_returns_201_with_an_empty_set_and_names_are_unique_after_normalization()
    {
        var reply = await PostAsync("/api/location-sets", new { name = "  Target cities " });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/location-sets/{reply.Id}", reply.Location);
        Assert.Equal("Target cities", reply.Str("name"));
        Assert.Equal(0, reply.Body.GetProperty("locationCount").GetInt32());
        AssertProblem(await PostAsync("/api/location-sets", new { name = "TARGET   cities" }), HttpStatusCode.Conflict, "duplicate_name");
        AssertInvalid(await PostAsync("/api/location-sets", new { name = "" }), "name");
    }

    [Fact]
    public async Task Rename_checks_uniqueness_and_unknown_sets_are_404()
    {
        var a = await CreateSetAsync("A");
        await CreateSetAsync("B");

        Assert.Equal(HttpStatusCode.OK, (await PutAsync($"/api/location-sets/{a}", new { name = "a" })).Status);
        AssertProblem(await PutAsync($"/api/location-sets/{a}", new { name = "b" }), HttpStatusCode.Conflict, "duplicate_name");
        AssertProblem(await PutAsync($"/api/location-sets/{Guid.NewGuid()}", new { name = "Z" }), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await GetAsync($"/api/location-sets/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Every_scope_is_stored_and_remote_india_stays_different_from_remote_worldwide()
    {
        var setId = await CreateSetAsync("Mixed");

        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Vadodara", "city", "in")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Gujarat", "state", "IN")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "India", "country", "IN")).Status);
        var remoteIndia = await AddAsync(setId, "Remote India", "remoteCountry", "IN");
        var remoteWorld = await AddAsync(setId, "Remote worldwide", "REMOTEWORLDWIDE");

        Assert.Equal(HttpStatusCode.Created, remoteIndia.Status);
        Assert.Equal("remoteCountry", remoteIndia.Str("scope"));
        Assert.Equal("IN", remoteIndia.Str("countryCode"));
        Assert.Equal(HttpStatusCode.Created, remoteWorld.Status);
        Assert.Equal("remoteWorldwide", remoteWorld.Str("scope"));
        Assert.Equal(JsonValueKind.Null, remoteWorld.Body.GetProperty("countryCode").ValueKind);

        var set = await GetAsync($"/api/location-sets/{setId}");
        Assert.Equal(5, set.Body.GetProperty("locationCount").GetInt32());
        var scopes = set.Body.GetProperty("locations").EnumerateArray().Select(l => l.GetProperty("scope").GetString()).ToHashSet();
        Assert.Equal(new[] { "city", "state", "country", "remoteCountry", "remoteWorldwide" }.ToHashSet(), scopes);
    }

    [Fact]
    public async Task Nothing_is_hardcoded_to_one_country()
    {
        var setId = await CreateSetAsync("Abroad");

        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Zürich", "city", "CH")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Remote Germany", "remoteCountry", "DE")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Somewhere unlisted", "city")).Status);
    }

    [Fact]
    public async Task Scope_and_country_code_are_validated()
    {
        var setId = await CreateSetAsync("Rules");

        AssertInvalid(await AddAsync(setId, "Pune", null), "scope");
        AssertInvalid(await AddAsync(setId, "Pune", "planet"), "scope");
        AssertInvalid(await AddAsync(setId, " ", "city"), "label");
        AssertInvalid(await AddAsync(setId, "Remote India", "remoteCountry"), "countryCode");
        AssertInvalid(await AddAsync(setId, "Remote worldwide", "remoteWorldwide", "IN"), "countryCode");
        AssertInvalid(await AddAsync(setId, "Pune", "city", "IND"), "countryCode");
        AssertInvalid(await AddAsync(setId, "Pune", "city", "1N"), "countryCode");
        AssertInvalid(await PostAsync($"/api/location-sets/{setId}/locations", new { scope = "city" }), "label");
        Assert.Equal(0, (await GetAsync($"/api/location-sets/{setId}")).Body.GetProperty("locationCount").GetInt32());
    }

    [Fact]
    public async Task A_blank_country_code_counts_as_none_for_scopes_that_allow_it()
    {
        var setId = await CreateSetAsync("Blank code");

        var city = await AddAsync(setId, "Pune", "city", "  ");

        Assert.Equal(HttpStatusCode.Created, city.Status);
        Assert.Equal(JsonValueKind.Null, city.Body.GetProperty("countryCode").ValueKind);
    }

    [Fact]
    public async Task The_same_label_and_scope_conflict_unless_the_country_differs()
    {
        var setId = await CreateSetAsync("Dupes");
        await AddAsync(setId, "Springfield", "city", "US");

        AssertProblem(await AddAsync(setId, "  springfield ", "city", "us"), HttpStatusCode.Conflict, "duplicate_location");
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Springfield", "city", "CA")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Springfield", "state", "US")).Status);

        // Without a country the pair is also unique, and different from the same label with a country.
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Springfield", "city")).Status);
        AssertProblem(await AddAsync(setId, "SPRINGFIELD", "city"), HttpStatusCode.Conflict, "duplicate_location");
    }

    [Fact]
    public async Task The_same_location_may_be_in_different_sets()
    {
        var one = await CreateSetAsync("One");
        var two = await CreateSetAsync("Two");

        Assert.Equal(HttpStatusCode.Created, (await AddAsync(one, "Remote India", "remoteCountry", "IN")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(two, "Remote India", "remoteCountry", "IN")).Status);
    }

    [Fact]
    public async Task Update_replaces_label_scope_and_country_and_rejects_a_clash_with_another_location()
    {
        var setId = await CreateSetAsync("Edit");
        var pune = await AddAsync(setId, "Pune", "city", "IN");
        await AddAsync(setId, "Mumbai", "city", "IN");

        var updated = await PutAsync($"/api/location-sets/{setId}/locations/{pune.Id}", new { label = "Remote India", scope = "remoteCountry", countryCode = "in" });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("Remote India", updated.Str("label"));
        Assert.Equal("remoteCountry", updated.Str("scope"));
        Assert.Equal("IN", updated.Str("countryCode"));
        Assert.Equal(HttpStatusCode.OK, (await PutAsync($"/api/location-sets/{setId}/locations/{pune.Id}", new { label = "remote india", scope = "remoteCountry", countryCode = "IN" })).Status);
        AssertProblem(await PutAsync($"/api/location-sets/{setId}/locations/{pune.Id}", new { label = "mumbai", scope = "city", countryCode = "IN" }), HttpStatusCode.Conflict, "duplicate_location");
        AssertInvalid(await PutAsync($"/api/location-sets/{setId}/locations/{pune.Id}", new { label = "Remote", scope = "remoteWorldwide", countryCode = "IN" }), "countryCode");
    }

    [Fact]
    public async Task Locations_of_another_set_or_unknown_ids_are_404()
    {
        var setId = await CreateSetAsync("One");
        var other = await CreateSetAsync("Two");
        var location = await AddAsync(setId, "Pune", "city");

        AssertProblem(await PutAsync($"/api/location-sets/{other}/locations/{location.Id}", new { label = "X", scope = "city" }), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/location-sets/{other}/locations/{location.Id}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/location-sets/{Guid.NewGuid()}/locations/{location.Id}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await AddAsync(Guid.NewGuid(), "X", "city"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Deleting_a_location_frees_its_key_and_deleting_a_set_removes_its_locations()
    {
        var setId = await CreateSetAsync("Gone");
        var pune = await AddAsync(setId, "Pune", "city");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/location-sets/{setId}/locations/{pune.Id}")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(setId, "Pune", "city")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/location-sets/{setId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/location-sets/{setId}")).Status);
    }

    [Fact]
    public async Task A_set_a_profile_selects_cannot_be_deleted_until_it_is_unselected()
    {
        var setId = await CreateSetAsync("Used");
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Profile");
        await PutAsync($"/api/search-profiles/{profileId}/location-sets/{setId}");

        var blocked = await DeleteAsync($"/api/location-sets/{setId}");

        AssertProblem(blocked, HttpStatusCode.Conflict, "location_set_in_use");
        Assert.Equal(1, blocked.Body.GetProperty("searchProfileCount").GetInt32());
        await DeleteAsync($"/api/search-profiles/{profileId}/location-sets/{setId}");
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/location-sets/{setId}")).Status);
    }

    [Fact]
    public async Task Listing_is_paged_in_name_order_and_locations_are_in_label_order()
    {
        var b = await CreateSetAsync("B set");
        await CreateSetAsync("A set");
        await CreateSetAsync("C set");
        await AddAsync(b, "Surat", "city");
        await AddAsync(b, "Ahmedabad", "city");

        var page = await GetAsync("/api/location-sets?skip=1&take=1");

        Assert.Equal(1, page.Count);
        Assert.Equal("B set", page[0].GetProperty("name").GetString());
        Assert.Equal(2, page[0].GetProperty("locationCount").GetInt32());
        var set = await GetAsync($"/api/location-sets/{b}");
        Assert.Equal("Ahmedabad", set.Body.GetProperty("locations")[0].GetProperty("label").GetString());
        AssertInvalid(await GetAsync("/api/location-sets?take=501"), "take");
    }
}
