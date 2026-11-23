using System.Net;

namespace JobSearchTracker.Api.Tests;

public class RoleListApiTests : ApiTestBase
{
    private static readonly string[] ExpectedAliases = ["Lead Business Analyst", "Sr. BA"];
    private static readonly string[] OnlyX = ["x"];
    private static readonly string[] PoAndLead = ["po", "Product Lead"];
    private static readonly string[] OnlySm = ["SM"];

    private async Task<Guid> CreateRoleListAsync(string name)
    {
        var reply = await PostAsync("/api/role-lists", new { name });
        Assert.Equal(HttpStatusCode.Created, reply.Status);
        return reply.Id;
    }

    private async Task<Reply> AddTitleAsync(Guid listId, string title, params string[] aliases) =>
        await PostAsync($"/api/role-lists/{listId}/titles", new { title, aliases });

    [Fact]
    public async Task Create_returns_201_with_an_empty_list_and_the_trimmed_name()
    {
        var reply = await PostAsync("/api/role-lists", new { name = "  Backend roles " });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/role-lists/{reply.Id}", reply.Location);
        Assert.Equal("Backend roles", reply.Str("name"));
        Assert.Equal(0, reply.Body.GetProperty("titleCount").GetInt32());
        Assert.Equal(0, reply.Body.GetProperty("titles").GetArrayLength());
    }

    [Fact]
    public async Task Names_are_unique_after_normalization_and_blank_names_are_rejected()
    {
        await CreateRoleListAsync("Business Analyst");

        AssertProblem(await PostAsync("/api/role-lists", new { name = "  business   ANALYST " }), HttpStatusCode.Conflict, "duplicate_name");
        AssertInvalid(await PostAsync("/api/role-lists", new { name = "   " }), "name");
        AssertInvalid(await PostAsync("/api/role-lists", new { name = new string('x', 201) }), "name");
    }

    [Fact]
    public async Task Rename_to_an_existing_name_conflicts_but_keeping_the_own_name_is_fine()
    {
        var a = await CreateRoleListAsync("A list");
        await CreateRoleListAsync("B list");

        Assert.Equal(HttpStatusCode.OK, (await PutAsync($"/api/role-lists/{a}", new { name = "a LIST" })).Status);
        AssertProblem(await PutAsync($"/api/role-lists/{a}", new { name = "b list" }), HttpStatusCode.Conflict, "duplicate_name");
        AssertProblem(await PutAsync($"/api/role-lists/{Guid.NewGuid()}", new { name = "Z" }), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task A_title_with_aliases_is_stored_and_returned_in_title_order()
    {
        var listId = await CreateRoleListAsync("Roles");

        var added = await AddTitleAsync(listId, "Senior Business Analyst", "Sr. BA", "Lead Business Analyst", "sr. ba");

        Assert.Equal(HttpStatusCode.Created, added.Status);
        Assert.Equal($"/api/role-lists/{listId}/titles/{added.Id}", added.Location);
        Assert.Equal("Senior Business Analyst", added.Str("title"));
        Assert.Equal(2, added.Body.GetProperty("aliases").GetArrayLength());
        await AddTitleAsync(listId, "Product Owner");

        var list = await GetAsync($"/api/role-lists/{listId}");
        Assert.Equal(2, list.Body.GetProperty("titleCount").GetInt32());
        Assert.Equal(2, list.Body.GetProperty("aliasCount").GetInt32());
        Assert.Equal("Product Owner", list.Body.GetProperty("titles")[0].GetProperty("title").GetString());
        var aliases = list.Body.GetProperty("titles")[1].GetProperty("aliases").EnumerateArray().Select(a => a.GetProperty("alias").GetString()).ToArray();
        Assert.Equal(ExpectedAliases, aliases);
    }

    [Fact]
    public async Task Nothing_is_hardcoded_so_any_title_in_any_language_is_accepted()
    {
        var listId = await CreateRoleListAsync("Mixed");

        Assert.Equal(HttpStatusCode.Created, (await AddTitleAsync(listId, "प्रोडक्ट मैनेजर")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddTitleAsync(listId, "Chef de partie")).Status);
    }

    [Fact]
    public async Task A_title_or_alias_that_is_already_in_the_list_conflicts_after_normalization()
    {
        var listId = await CreateRoleListAsync("Roles");
        await AddTitleAsync(listId, "Product Owner", "PO");

        AssertProblem(await AddTitleAsync(listId, "  product   OWNER "), HttpStatusCode.Conflict, "duplicate_title");
        AssertProblem(await AddTitleAsync(listId, "Scrum Master", "po"), HttpStatusCode.Conflict, "duplicate_title");
        AssertProblem(await AddTitleAsync(listId, "PO"), HttpStatusCode.Conflict, "duplicate_title");

        // Nothing from the failed requests was kept.
        var list = await GetAsync($"/api/role-lists/{listId}");
        Assert.Equal(1, list.Body.GetProperty("titleCount").GetInt32());
        Assert.Equal(1, list.Body.GetProperty("aliasCount").GetInt32());
    }

    [Fact]
    public async Task The_same_title_may_be_in_different_lists()
    {
        var one = await CreateRoleListAsync("One");
        var two = await CreateRoleListAsync("Two");

        Assert.Equal(HttpStatusCode.Created, (await AddTitleAsync(one, "Product Owner", "PO")).Status);
        Assert.Equal(HttpStatusCode.Created, (await AddTitleAsync(two, "Product Owner", "PO")).Status);
    }

    [Fact]
    public async Task An_alias_equal_to_its_own_title_or_a_bad_alias_is_a_validation_error()
    {
        var listId = await CreateRoleListAsync("Roles");

        AssertInvalid(await AddTitleAsync(listId, "Product Owner", "product owner"), "aliases");
        AssertInvalid(await AddTitleAsync(listId, "Product Owner", " "), "aliases");
        AssertInvalid(await AddTitleAsync(listId, "Product Owner", Enumerable.Range(0, 51).Select(i => $"Alias {i}").ToArray()), "aliases");
        AssertInvalid(await AddTitleAsync(listId, " "), "title");
        AssertInvalid(await PostAsync($"/api/role-lists/{listId}/titles", new { aliases = OnlyX }), "title");
    }

    [Fact]
    public async Task Update_replaces_the_title_and_the_whole_alias_set_and_may_reuse_its_own_names()
    {
        var listId = await CreateRoleListAsync("Roles");
        var title = await AddTitleAsync(listId, "Product Owner", "PO", "Owner");
        var other = await AddTitleAsync(listId, "Scrum Master", "SM");

        var updated = await PutAsync($"/api/role-lists/{listId}/titles/{title.Id}", new { title = "Product Owner (PO)", aliases = PoAndLead });

        Assert.Equal(HttpStatusCode.OK, updated.Status);
        Assert.Equal("Product Owner (PO)", updated.Str("title"));
        Assert.Equal(PoAndLead.OrderBy(x => x, StringComparer.Ordinal), updated.Body.GetProperty("aliases").EnumerateArray().Select(a => a.GetProperty("alias").GetString()!).OrderBy(x => x, StringComparer.Ordinal));
        AssertProblem(await PutAsync($"/api/role-lists/{listId}/titles/{title.Id}", new { title = "Scrum Master" }), HttpStatusCode.Conflict, "duplicate_title");
        AssertProblem(await PutAsync($"/api/role-lists/{listId}/titles/{title.Id}", new { title = "Product Owner (PO)", aliases = OnlySm }), HttpStatusCode.Conflict, "duplicate_title");

        // The rejected updates changed nothing; omitted aliases means no aliases.
        var cleared = await PutAsync($"/api/role-lists/{listId}/titles/{title.Id}", new { title = "Product Owner (PO)" });
        Assert.Equal(0, cleared.Body.GetProperty("aliases").GetArrayLength());
        Assert.Equal(1, (await GetAsync($"/api/role-lists/{listId}")).Body.GetProperty("titles")[1].GetProperty("aliases").GetArrayLength());
        Assert.NotEqual(Guid.Empty, other.Id);
    }

    [Fact]
    public async Task Update_and_delete_of_a_title_return_404_for_an_unknown_list_title_or_an_alias_id()
    {
        var listId = await CreateRoleListAsync("Roles");
        var title = await AddTitleAsync(listId, "Product Owner", "PO");
        var aliasId = title.Body.GetProperty("aliases")[0].GetProperty("id").GetGuid();
        var otherList = await CreateRoleListAsync("Other");

        AssertProblem(await PutAsync($"/api/role-lists/{Guid.NewGuid()}/titles/{title.Id}", new { title = "X" }), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/role-lists/{listId}/titles/{Guid.NewGuid()}", new { title = "X" }), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/role-lists/{listId}/titles/{aliasId}", new { title = "X" }), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await PutAsync($"/api/role-lists/{otherList}/titles/{title.Id}", new { title = "X" }), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/role-lists/{listId}/titles/{aliasId}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/role-lists/{Guid.NewGuid()}/titles/{title.Id}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await AddTitleAsync(Guid.NewGuid(), "X"), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Deleting_a_title_removes_its_aliases_and_frees_the_names()
    {
        var listId = await CreateRoleListAsync("Roles");
        var title = await AddTitleAsync(listId, "Product Owner", "PO");

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/role-lists/{listId}/titles/{title.Id}")).Status);

        var list = await GetAsync($"/api/role-lists/{listId}");
        Assert.Equal(0, list.Body.GetProperty("titleCount").GetInt32());
        Assert.Equal(0, list.Body.GetProperty("aliasCount").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await AddTitleAsync(listId, "PO")).Status);
    }

    [Fact]
    public async Task Deleting_a_list_removes_its_titles_but_a_list_a_profile_selects_cannot_be_deleted()
    {
        var free = await CreateRoleListAsync("Free");
        await AddTitleAsync(free, "Product Owner", "PO");
        var used = await CreateRoleListAsync("Used");
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Profile");
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync($"/api/search-profiles/{profileId}/role-lists/{used}")).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/role-lists/{free}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync($"/api/role-lists/{free}")).Status);
        var blocked = await DeleteAsync($"/api/role-lists/{used}");
        AssertProblem(blocked, HttpStatusCode.Conflict, "role_list_in_use");
        Assert.Equal(1, blocked.Body.GetProperty("searchProfileCount").GetInt32());

        await DeleteAsync($"/api/search-profiles/{profileId}/role-lists/{used}");
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync($"/api/role-lists/{used}")).Status);
    }

    [Fact]
    public async Task Listing_is_paged_in_name_order_and_counts_titles_and_aliases_separately()
    {
        var b = await CreateRoleListAsync("B list");
        await CreateRoleListAsync("A list");
        await CreateRoleListAsync("C list");
        await AddTitleAsync(b, "Product Owner", "PO", "Owner");
        await AddTitleAsync(b, "Scrum Master");

        var page = await GetAsync("/api/role-lists?skip=1&take=1");

        Assert.Equal(1, page.Count);
        Assert.Equal("B list", page[0].GetProperty("name").GetString());
        Assert.Equal(2, page[0].GetProperty("titleCount").GetInt32());
        Assert.Equal(2, page[0].GetProperty("aliasCount").GetInt32());
        AssertInvalid(await GetAsync("/api/role-lists?skip=-1"), "skip");
        AssertInvalid(await GetAsync("/api/role-lists?take=0"), "take");
    }
}
