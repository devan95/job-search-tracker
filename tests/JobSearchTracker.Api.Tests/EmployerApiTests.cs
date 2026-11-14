using System.Net;

namespace JobSearchTracker.Api.Tests;

public class EmployerApiTests : ApiTestBase
{
    [Fact]
    public async Task Create_returns_201_with_the_display_name_and_the_normalized_name()
    {
        var reply = await PostAsync("/api/employers", new { name = "  Acme   Corp " });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal($"/api/employers/{reply.Id}", reply.Location);
        Assert.Equal("Acme   Corp", reply.Str("name"));
        Assert.Equal("acme corp", reply.Str("normalizedName"));
    }

    [Theory]
    [InlineData("acme corp")]
    [InlineData("  ACME     CORP")]
    [InlineData("ＡＣＭＥ Corp")] // full-width letters fold to ASCII under NFKC
    public async Task Create_reports_a_duplicate_after_normalization_as_409_with_the_existing_id(string variant)
    {
        var existingId = await CreateEmployerAsync("Acme Corp");

        var reply = await PostAsync("/api/employers", new { name = variant });

        AssertProblem(reply, HttpStatusCode.Conflict, "duplicate_name");
        Assert.Equal(existingId, reply.Body.GetProperty("existingId").GetGuid());
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Resolve_creates_once_and_then_returns_the_canonical_employer()
    {
        var first = await PostAsync("/api/employers/resolve", new { name = "Globex" });
        var second = await PostAsync("/api/employers/resolve", new { name = "  GLOBEX " });

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Globex", second.Str("name"));
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Lookup_finds_the_employer_by_any_spelling_that_normalizes_the_same_and_404s_otherwise()
    {
        var id = await CreateEmployerAsync("Initech");

        var found = await GetAsync("/api/employers/lookup?name=%20%20INITECH");
        var missing = await GetAsync("/api/employers/lookup?name=Nope");
        var blank = await GetAsync("/api/employers/lookup?name=%20");

        Assert.Equal(HttpStatusCode.OK, found.Status);
        Assert.Equal(id, found.Id);
        AssertProblem(missing, HttpStatusCode.NotFound, "not_found");
        AssertInvalid(blank, "name");
    }

    [Fact]
    public async Task List_filters_by_a_normalized_substring_and_orders_by_name()
    {
        await CreateEmployerAsync("Zeta Systems");
        await CreateEmployerAsync("Alpha Systems");
        await CreateEmployerAsync("Beta Labs");

        var all = await GetAsync("/api/employers");
        var systems = await GetAsync("/api/employers?q=%20SYSTEMS");

        Assert.Equal(3, all.Count);
        Assert.Equal(2, systems.Count);
        Assert.Equal("Alpha Systems", systems[0].GetProperty("name").GetString());
        Assert.Equal("Zeta Systems", systems[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Get_returns_the_employer_and_404_for_an_unknown_id()
    {
        var id = await CreateEmployerAsync("Hooli");

        var found = await GetAsync($"/api/employers/{id}");
        var missing = await GetAsync($"/api/employers/{Guid.NewGuid()}");

        Assert.Equal("Hooli", found.Str("name"));
        AssertProblem(missing, HttpStatusCode.NotFound, "not_found");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_and_resolve_reject_a_blank_name(string name)
    {
        AssertInvalid(await PostAsync("/api/employers", new { name }), "name");
        AssertInvalid(await PostAsync("/api/employers/resolve", new { name }), "name");
    }

    [Fact]
    public async Task Create_rejects_a_missing_name_and_a_name_over_200_characters()
    {
        AssertInvalid(await PostAsync("/api/employers", new { }), "name");
        AssertInvalid(await PostAsync("/api/employers", new { name = new string('x', 201) }), "name");
    }

    [Fact]
    public async Task Delete_removes_an_employer_that_is_in_no_list()
    {
        var id = await CreateEmployerAsync("Soylent");

        var deleted = await DeleteAsync($"/api/employers/{id}");
        var again = await DeleteAsync($"/api/employers/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.Status);
        AssertProblem(again, HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task Delete_is_restricted_while_the_employer_is_in_a_list_and_allowed_after_removal()
    {
        var employerId = await CreateEmployerAsync("Umbrella");
        var listId = await CreateListAsync("Pharma");
        await AddEmployerToListAsync(listId, employerId);

        var blocked = await DeleteAsync($"/api/employers/{employerId}");
        var stillThere = await GetAsync($"/api/employers/{employerId}");
        var removed = await DeleteAsync($"/api/company-lists/{listId}/employers/{employerId}");
        var nowAllowed = await DeleteAsync($"/api/employers/{employerId}");

        AssertProblem(blocked, HttpStatusCode.Conflict, "employer_in_company_list");
        Assert.Equal(1, blocked.Body.GetProperty("companyListCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, stillThere.Status);
        Assert.Equal(HttpStatusCode.NoContent, removed.Status);
        Assert.Equal(HttpStatusCode.NoContent, nowAllowed.Status);
    }
}
