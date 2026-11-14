using System.Net;

namespace JobSearchTracker.Api.Tests;

public class CandidateApiTests : ApiTestBase
{
    [Fact]
    public async Task Create_returns_201_with_location_and_a_trimmed_name()
    {
        var reply = await PostAsync("/api/candidates", new { name = "  Asha Rao  " });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/candidates/{reply.Id}", reply.Location);
        Assert.Equal("Asha Rao", reply.Str("name"));
    }

    [Fact]
    public async Task Get_returns_the_candidate_and_404_problem_for_an_unknown_id()
    {
        var id = await CreateCandidateAsync("Asha");

        var found = await GetAsync($"/api/candidates/{id}");
        var missing = await GetAsync($"/api/candidates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, found.Status);
        Assert.Equal("Asha", found.Str("name"));
        AssertProblem(missing, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task List_is_ordered_by_name_and_supports_paging()
    {
        await CreateCandidateAsync("Charlie");
        await CreateCandidateAsync("Alice");
        await CreateCandidateAsync("Bob");

        var all = await GetAsync("/api/candidates");
        var page = await GetAsync("/api/candidates?skip=1&take=1");

        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, Enumerable.Range(0, all.Count).Select(i => all[i].GetProperty("name").GetString()).ToArray());
        Assert.Equal(1, page.Count);
        Assert.Equal("Bob", page[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_renames_the_candidate()
    {
        var id = await CreateCandidateAsync("Old");

        var updated = await PutAsync($"/api/candidates/{id}", new { name = "New" });
        var reread = await GetAsync($"/api/candidates/{id}");

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("New", reread.Str("name"));
    }

    [Fact]
    public async Task Update_of_an_unknown_candidate_returns_404()
    {
        var reply = await PutAsync($"/api/candidates/{Guid.NewGuid()}", new { name = "X" });

        AssertProblem(reply, HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_name(string name)
    {
        var reply = await PostAsync("/api/candidates", new { name });

        AssertInvalid(reply, "name");
    }

    [Fact]
    public async Task Create_rejects_a_missing_name_and_a_name_over_200_characters()
    {
        var missing = await PostAsync("/api/candidates", new { });
        var tooLong = await PostAsync("/api/candidates", new { name = new string('x', 201) });

        AssertInvalid(missing, "name");
        AssertInvalid(tooLong, "name");
    }

    [Fact]
    public async Task Malformed_json_returns_a_400_problem_not_a_server_error()
    {
        var reply = await SendRawJsonAsync(HttpMethod.Post, "/api/candidates", "{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("json", reply.ContentType);
    }

    [Fact]
    public async Task Paging_values_out_of_range_are_rejected()
    {
        var negativeSkip = await GetAsync("/api/candidates?skip=-1");
        var hugeTake = await GetAsync("/api/candidates?take=501");

        AssertInvalid(negativeSkip, "skip");
        AssertInvalid(hugeTake, "take");
    }

    [Fact]
    public async Task Delete_removes_a_candidate_without_profiles()
    {
        var id = await CreateCandidateAsync();

        var deleted = await DeleteAsync($"/api/candidates/{id}");
        var again = await GetAsync($"/api/candidates/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        AssertProblem(again, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Delete_is_restricted_while_the_candidate_has_profiles()
    {
        var candidateId = await CreateCandidateAsync();
        var profileId = await CreateProfileAsync(candidateId, "Backend roles");

        var blocked = await DeleteAsync($"/api/candidates/{candidateId}");
        var candidateStillThere = await GetAsync($"/api/candidates/{candidateId}");
        var profileDeleted = await DeleteAsync($"/api/search-profiles/{profileId}");
        var nowAllowed = await DeleteAsync($"/api/candidates/{candidateId}");

        AssertProblem(blocked, HttpStatusCode.Conflict, "candidate_has_profiles");
        Assert.Equal(1, blocked.Body.GetProperty("searchProfileCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, candidateStillThere.Status);
        Assert.Equal(HttpStatusCode.NoContent, profileDeleted.Status);
        Assert.Equal(HttpStatusCode.NoContent, nowAllowed.Status);
    }
}
