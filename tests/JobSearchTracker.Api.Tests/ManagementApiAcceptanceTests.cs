using System.Net;
using System.Text.Json;

namespace JobSearchTracker.Api.Tests;

/// <summary>
/// Gap-filling tests found by reviewing CandidateApiTests, EmployerApiTests, CompanyListApiTests and SearchProfileApiTests
/// against the acceptance criteria AC1 to AC8 of the "Backend API: candidates, employers, company lists and search
/// profiles" task (consistent semantics, duplicate handling, shared lists, work-mode inheritance, discovery flags,
/// delete restrictions, PostgreSQL-backed coverage, documented contract). Each test names the criterion it adds to.
/// </summary>
public class ManagementApiAcceptanceTests : ApiTestBase
{
    private static readonly string[] Remote = ["remote"];
    private static readonly string[] OnSite = ["onSite"];
    private static readonly string[] Hybrid = ["hybrid"];
    private static readonly string[] RemoteThenOnSite = ["remote", "onSite"];
    private static readonly string[] UnknownMode = ["moon base"];
    private static readonly string[] MisspelledMode = ["office"];
    private static readonly string[] MessyModes = [" ONSITE ", "OnSite", "REMOTE"];

    // AC1: consistent response and error semantics

    [Theory]
    [InlineData("/api/candidates")]
    [InlineData("/api/employers")]
    [InlineData("/api/company-lists")]
    [InlineData("/api/search-profiles")]
    public async Task Every_list_endpoint_validates_paging_and_accepts_the_documented_limits(string path)
    {
        var negativeSkip = await GetAsync($"{path}?skip=-1");
        var zeroTake = await GetAsync($"{path}?take=0");
        var hugeTake = await GetAsync($"{path}?take=501");
        var maxTake = await GetAsync($"{path}?skip=0&take=500");

        AssertInvalid(negativeSkip, "skip");
        AssertInvalid(zeroTake, "take");
        AssertInvalid(hugeTake, "take");
        Assert.Equal(HttpStatusCode.OK, maxTake.Status);
    }

    [Fact]
    public async Task Skip_and_take_page_employers_company_lists_and_profiles_in_name_order()
    {
        var candidateId = await CreateCandidateAsync();
        foreach (var name in new[] { "Charlie", "Alpha", "Bravo" })
        {
            await CreateEmployerAsync($"{name} Co");
            await CreateListAsync($"{name} list");
            await CreateProfileAsync(candidateId, $"{name} profile");
        }

        var employers = await GetAsync("/api/employers?skip=1&take=2");
        var lists = await GetAsync("/api/company-lists?skip=1&take=2");
        var profiles = await GetAsync("/api/search-profiles?skip=1&take=2");
        var pastTheEnd = await GetAsync("/api/employers?skip=3&take=10");

        // Two items after the first: both names are asserted, so a reversed sort (Bravo, Alpha) would fail.
        Assert.Equal(2, employers.Count);
        Assert.Equal("Bravo Co", employers[0].GetProperty("name").GetString());
        Assert.Equal("Charlie Co", employers[1].GetProperty("name").GetString());
        Assert.Equal(2, lists.Count);
        Assert.Equal("Bravo list", lists[0].GetProperty("name").GetString());
        Assert.Equal("Charlie list", lists[1].GetProperty("name").GetString());
        Assert.Equal(2, profiles.Count);
        Assert.Equal("Bravo profile", profiles[0].GetProperty("name").GetString());
        Assert.Equal("Charlie profile", profiles[1].GetProperty("name").GetString());
        Assert.Equal(0, pastTheEnd.Count);
    }

    [Fact]
    public async Task Profiles_filtered_by_an_unknown_candidate_are_an_empty_list_not_an_error()
    {
        var reply = await GetAsync($"/api/search-profiles?candidateId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal(0, reply.Count);
    }

    [Theory]
    [InlineData("/api/candidates/not-a-guid")]
    [InlineData("/api/employers/not-a-guid")]
    [InlineData("/api/company-lists/not-a-guid")]
    [InlineData("/api/search-profiles/not-a-guid")]
    public async Task A_route_id_that_is_not_a_guid_matches_no_route(string url)
    {
        using var response = await Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/employers")]
    [InlineData("/api/employers/resolve")]
    [InlineData("/api/company-lists")]
    [InlineData("/api/search-profiles")]
    public async Task Malformed_json_is_a_400_problem_on_every_create_endpoint(string path)
    {
        var reply = await SendRawJsonAsync(HttpMethod.Post, path, "{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("json", reply.ContentType);
    }

    [Theory]
    [InlineData("/api/candidates")]
    [InlineData("/api/company-lists")]
    [InlineData("/api/search-profiles")]
    public async Task Malformed_json_is_a_400_problem_on_every_update_endpoint(string collectionPath)
    {
        var reply = await SendRawJsonAsync(HttpMethod.Put, $"{collectionPath}/{Guid.NewGuid()}", "{ not json");

        Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        Assert.Contains("json", reply.ContentType);
    }

    [Fact]
    public async Task Candidate_update_rejects_a_blank_missing_or_over_long_name_and_keeps_the_old_name()
    {
        var id = await CreateCandidateAsync("Keep me");

        var blank = await PutAsync($"/api/candidates/{id}", new { name = " " });
        var missing = await PutAsync($"/api/candidates/{id}", new { });
        var tooLong = await PutAsync($"/api/candidates/{id}", new { name = new string('x', 201) });
        var reread = await GetAsync($"/api/candidates/{id}");

        AssertInvalid(blank, "name");
        AssertInvalid(missing, "name");
        AssertInvalid(tooLong, "name");
        Assert.Equal("Keep me", reread.Str("name"));
    }

    [Fact]
    public async Task Company_list_update_reports_every_invalid_field_together_and_changes_nothing()
    {
        var id = await CreateListAsync("Stable list", "Surat", Remote);

        var reply = await PutAsync($"/api/company-lists/{id}", new
        {
            name = " ",
            locationLabel = new string('x', 201),
            defaultWorkModes = UnknownMode,
        });
        var reread = await GetAsync($"/api/company-lists/{id}");

        AssertInvalid(reply, "name");
        AssertInvalid(reply, "locationLabel");
        AssertInvalid(reply, "defaultWorkModes");
        Assert.Equal("Stable list", reread.Str("name"));
        Assert.Equal("Surat", reread.Str("locationLabel"));
        Assert.Equal(Remote, reread.Strings("defaultWorkModes"));
    }

    [Fact]
    public async Task Profile_create_reports_every_invalid_field_together()
    {
        var reply = await PostAsync("/api/search-profiles", new { name = " ", workModes = UnknownMode });

        AssertInvalid(reply, "name");
        AssertInvalid(reply, "workModes");
        AssertInvalid(reply, "candidateId");
    }

    [Fact]
    public async Task Profile_update_rejects_an_unknown_work_mode_and_an_over_long_name_and_changes_nothing()
    {
        var profileId = await CreateProfileAsync(await CreateCandidateAsync(), "Stable profile");

        var badMode = await PutAsync($"/api/search-profiles/{profileId}", new
        {
            name = "Stable profile",
            workModes = MisspelledMode,
            companyMonitoringEnabled = true,
            broadSearchEnabled = true,
        });
        var tooLong = await PutAsync($"/api/search-profiles/{profileId}", new
        {
            name = new string('x', 201),
            companyMonitoringEnabled = true,
            broadSearchEnabled = true,
        });
        var reread = await GetAsync($"/api/search-profiles/{profileId}");

        AssertInvalid(badMode, "workModes");
        AssertInvalid(tooLong, "name");
        Assert.Equal("Stable profile", reread.Str("name"));
        Assert.False(reread.Body.GetProperty("companyMonitoringEnabled").GetBoolean());
        Assert.False(reread.Body.GetProperty("broadSearchEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reread.Body.GetProperty("workModes").ValueKind);
    }

    [Fact]
    public async Task Deleting_an_unknown_candidate_or_company_list_is_a_404_problem()
    {
        AssertProblem(await DeleteAsync($"/api/candidates/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await DeleteAsync($"/api/company-lists/{Guid.NewGuid()}"), HttpStatusCode.NotFound, "not_found");
    }

    // AC2: duplicate normalized names are handled safely

    [Theory]
    [InlineData("/api/candidates")]
    [InlineData("/api/employers")]
    [InlineData("/api/company-lists")]
    public async Task A_name_of_exactly_200_characters_is_accepted_and_201_is_rejected(string path)
    {
        var accepted = await PostAsync(path, new { name = new string('a', 200) });
        var rejected = await PostAsync(path, new { name = new string('b', 201) });

        Assert.Equal(HttpStatusCode.Created, accepted.Status);
        Assert.Equal(200, accepted.Str("name").Length);
        AssertInvalid(rejected, "name");
    }

    [Fact]
    public async Task A_profile_name_of_exactly_200_characters_is_accepted()
    {
        var candidateId = await CreateCandidateAsync();

        var accepted = await PostAsync("/api/search-profiles", new { candidateId, name = new string('a', 200) });

        Assert.Equal(HttpStatusCode.Created, accepted.Status);
    }

    [Fact]
    public async Task A_name_that_grows_past_200_characters_when_normalized_is_a_validation_error_not_a_server_error()
    {
        // U+FB03 (the "ffi" ligature) is one character as typed and three after NFKC, so 100 of them normalize to 300.
        var ligatures = new string('ﬃ', 100);
        var candidateId = await CreateCandidateAsync();

        var employer = await PostAsync("/api/employers", new { name = ligatures });
        var resolved = await PostAsync("/api/employers/resolve", new { name = ligatures });
        var list = await PostAsync("/api/company-lists", new { name = ligatures });
        var profile = await PostAsync("/api/search-profiles", new { candidateId, name = ligatures });

        AssertInvalid(employer, "name");
        AssertInvalid(resolved, "name");
        AssertInvalid(list, "name");
        AssertInvalid(profile, "name");
        Assert.Equal(0, (await GetAsync("/api/employers")).Count);
        Assert.Equal(0, (await GetAsync("/api/company-lists")).Count);
        Assert.Equal(0, (await GetAsync("/api/search-profiles")).Count);
    }

    [Fact]
    public async Task Profile_update_may_use_a_name_that_another_candidates_profile_already_has()
    {
        var first = await CreateCandidateAsync("First");
        var second = await CreateCandidateAsync("Second");
        await CreateProfileAsync(first, "Shared name");
        var otherProfileId = await CreateProfileAsync(second, "Different");

        var reply = await PutAsync($"/api/search-profiles/{otherProfileId}", new
        {
            name = "SHARED   name",
            companyMonitoringEnabled = false,
            broadSearchEnabled = false,
        });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.Equal("SHARED   name", reply.Str("name"));
    }

    [Fact]
    public async Task Concurrent_creates_of_one_employer_name_leave_one_employer_and_only_201_or_409()
    {
        var spellings = new[] { "Parallel Co", " parallel   CO", "PARALLEL CO", "Parallel Co", "parallel co", "Parallel  Co" };

        var replies = await Task.WhenAll(spellings.Select(name => PostAsync("/api/employers", new { name })));

        Assert.Equal(1, replies.Count(r => r.Status == HttpStatusCode.Created));
        Assert.All(
            replies.Where(r => r.Status != HttpStatusCode.Created),
            r => AssertProblem(r, HttpStatusCode.Conflict, "duplicate_name"));
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Concurrent_resolves_of_one_employer_name_all_return_the_same_canonical_employer()
    {
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync("/api/employers/resolve", new { name = "Resolve Co" })));

        Assert.All(replies, r => Assert.True(r.Status is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Equal(1, replies.Count(r => r.Status == HttpStatusCode.Created));
        Assert.Single(replies.Select(r => r.Id).Distinct());
        Assert.Equal(1, (await GetAsync("/api/employers")).Count);
    }

    [Fact]
    public async Task Concurrent_creates_of_one_list_name_or_one_profile_name_leave_one_row_and_only_201_or_409()
    {
        var candidateId = await CreateCandidateAsync();
        var spellings = new[] { "Race", " RACE ", "race", "Race", " rAcE" };

        var lists = await Task.WhenAll(spellings.Select(name => PostAsync("/api/company-lists", new { name })));
        var profiles = await Task.WhenAll(spellings.Select(name => PostAsync("/api/search-profiles", new { candidateId, name })));

        foreach (var replies in new[] { lists, profiles })
        {
            Assert.Equal(1, replies.Count(r => r.Status == HttpStatusCode.Created));
            Assert.All(
                replies.Where(r => r.Status != HttpStatusCode.Created),
                r => AssertProblem(r, HttpStatusCode.Conflict, "duplicate_name"));
        }

        Assert.Equal(1, (await GetAsync("/api/company-lists")).Count);
        Assert.Equal(1, (await GetAsync($"/api/search-profiles?candidateId={candidateId}")).Count);
    }

    // AC3: a profile selects several lists and lists are shared by profiles

    [Fact]
    public async Task A_list_shared_by_two_profiles_stays_protected_until_the_last_profile_lets_go()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("Shared list");
        var profileOne = await CreateProfileAsync(candidateId, "One", listId);
        var profileTwo = await CreateProfileAsync(candidateId, "Two", listId);

        var blockedByBoth = await DeleteAsync($"/api/company-lists/{listId}");
        var unselected = await DeleteAsync($"/api/search-profiles/{profileOne}/company-lists/{listId}");
        var blockedByOne = await DeleteAsync($"/api/company-lists/{listId}");
        var profileTwoStillSelects = await GetAsync($"/api/search-profiles/{profileTwo}");
        var profileDeleted = await DeleteAsync($"/api/search-profiles/{profileTwo}");
        var allowed = await DeleteAsync($"/api/company-lists/{listId}");

        AssertProblem(blockedByBoth, HttpStatusCode.Conflict, "company_list_in_use");
        Assert.Equal(2, blockedByBoth.Body.GetProperty("searchProfileCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, unselected.Status);
        AssertProblem(blockedByOne, HttpStatusCode.Conflict, "company_list_in_use");
        Assert.Equal(1, blockedByOne.Body.GetProperty("searchProfileCount").GetInt32());
        Assert.Equal(1, profileTwoStillSelects.Body.GetProperty("companyLists").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, profileDeleted.Status);
        Assert.Equal(HttpStatusCode.NoContent, allowed.Status);
    }

    // AC4: NULL inherits the lists' defaults, an empty set is explicit, list defaults are never NULL

    [Fact]
    public async Task A_profile_that_inherits_follows_later_changes_to_its_lists_defaults_and_an_override_does_not()
    {
        var candidateId = await CreateCandidateAsync();
        var listId = await CreateListAsync("Follow me", defaultWorkModes: Remote);
        var inheriting = await PostAsync("/api/search-profiles", new { candidateId, name = "Inheriting", companyListIds = new[] { listId } });
        var overriding = await PostAsync("/api/search-profiles", new { candidateId, name = "Overriding", companyListIds = new[] { listId }, workModes = Hybrid });

        var changed = await PutAsync($"/api/company-lists/{listId}", new { name = "Follow me", defaultWorkModes = OnSite });
        var inheritingAfter = await GetAsync($"/api/search-profiles/{inheriting.Id}");
        var overridingAfter = await GetAsync($"/api/search-profiles/{overriding.Id}");

        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal(JsonValueKind.Null, inheritingAfter.Body.GetProperty("workModes").ValueKind);
        Assert.Equal(
            OnSite,
            inheritingAfter.Body.GetProperty("companyLists")[0].GetProperty("defaultWorkModes").EnumerateArray().Select(e => e.GetString()!).ToArray());
        Assert.Equal(Hybrid, overridingAfter.Strings("workModes"));
    }

    [Fact]
    public async Task Work_mode_names_are_trimmed_and_case_insensitive_and_repeated_names_count_once()
    {
        var reply = await PostAsync("/api/company-lists", new { name = "Lenient", defaultWorkModes = MessyModes });

        Assert.Equal(HttpStatusCode.Created, reply.Status);
        Assert.Equal(RemoteThenOnSite, reply.Strings("defaultWorkModes"));
    }

    // AC5: both discovery flags may be false; no source catalog and no per-source enablement

    [Fact]
    public async Task Profile_and_employer_responses_carry_no_source_enablement_or_website_fields()
    {
        var candidateId = await CreateCandidateAsync();
        var employer = await PostAsync("/api/employers", new { name = "Shape Co" });
        var listId = await CreateListAsync("Shape list");
        var profile = await PostAsync("/api/search-profiles", new { candidateId, name = "Shape profile", companyListIds = new[] { listId } });

        AssertFields(employer.Body, "id", "name", "normalizedName");
        AssertFields(
            profile.Body,
            "id",
            "candidateId",
            "name",
            "workModes",
            "companyMonitoringEnabled",
            "broadSearchEnabled",
            "profileVersion",
            "companyLists");
        AssertFields(profile.Body.GetProperty("companyLists")[0], "id", "name", "defaultWorkModes");
    }

    // AC6: deletion restrictions match the database design; removing an association is explicit

    [Fact]
    public async Task Delete_restrictions_report_every_blocking_row_and_removal_from_one_list_leaves_the_others()
    {
        var candidateId = await CreateCandidateAsync();
        await CreateProfileAsync(candidateId, "P1");
        await CreateProfileAsync(candidateId, "P2");
        var employerId = await CreateEmployerAsync("Twice listed");
        var listOne = await CreateListAsync("L1");
        var listTwo = await CreateListAsync("L2");
        await AddEmployerToListAsync(listOne, employerId);
        await AddEmployerToListAsync(listTwo, employerId);

        var candidateBlocked = await DeleteAsync($"/api/candidates/{candidateId}");
        var employerBlockedByBoth = await DeleteAsync($"/api/employers/{employerId}");
        var removedFromOne = await DeleteAsync($"/api/company-lists/{listOne}/employers/{employerId}");
        var employerBlockedByOne = await DeleteAsync($"/api/employers/{employerId}");
        var otherListKeepsIt = await GetAsync($"/api/company-lists/{listTwo}");

        AssertProblem(candidateBlocked, HttpStatusCode.Conflict, "candidate_has_profiles");
        Assert.Equal(2, candidateBlocked.Body.GetProperty("searchProfileCount").GetInt32());
        AssertProblem(employerBlockedByBoth, HttpStatusCode.Conflict, "employer_in_company_list");
        Assert.Equal(2, employerBlockedByBoth.Body.GetProperty("companyListCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, removedFromOne.Status);
        AssertProblem(employerBlockedByOne, HttpStatusCode.Conflict, "employer_in_company_list");
        Assert.Equal(1, employerBlockedByOne.Body.GetProperty("companyListCount").GetInt32());
        Assert.Equal(1, otherListKeepsIt.Body.GetProperty("employerCount").GetInt32());
    }

    private static void AssertFields(JsonElement element, params string[] expected) =>
        Assert.Equal(
            expected.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            element.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
}
