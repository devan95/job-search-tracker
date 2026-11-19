using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>Role lists and location sets (OQ-P1.1, OQ-P1.2): the database itself keeps the data honest, with or without the API.</summary>
public class RoleLocationDataModelTests : DatabaseTestBase
{
    private async Task<RoleList> AddRoleListAsync(string name = "Roles")
    {
        await using var ctx = Db.CreateContext();
        var list = new RoleList(name);
        ctx.RoleLists.Add(list);
        await ctx.SaveChangesAsync();
        return list;
    }

    private async Task<LocationSet> AddLocationSetAsync(string name = "Places")
    {
        await using var ctx = Db.CreateContext();
        var set = new LocationSet(name);
        ctx.LocationSets.Add(set);
        await ctx.SaveChangesAsync();
        return set;
    }

    private async Task AddAsync(params object[] entities)
    {
        await using var ctx = Db.CreateContext();
        ctx.AddRange(entities);
        await ctx.SaveChangesAsync();
    }

    private async Task<int> CountAsync(string table) =>
        (await Db.QueryAsync($"SELECT count(*)::int FROM {table}", r => r.GetInt32(0))).Single();

    // ---- role lists --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Role_list_names_are_unique_after_normalization()
    {
        await AddRoleListAsync("Business Analyst");

        await using var second = Db.CreateContext();
        second.RoleLists.Add(new RoleList("  business   ANALYST "));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => second.SaveChangesAsync()));
    }

    [Fact]
    public async Task Titles_and_aliases_share_one_namespace_per_list_but_may_repeat_across_lists()
    {
        var one = await AddRoleListAsync("One");
        var two = await AddRoleListAsync("Two");
        var title = new RoleListTitle(one.Id, "Product Owner");
        await AddAsync(title, new RoleListTitle(one.Id, "PO", title.Id));

        await using (var duplicateAlias = Db.CreateContext())
        {
            duplicateAlias.RoleListTitles.Add(new RoleListTitle(one.Id, " po "));
            Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => duplicateAlias.SaveChangesAsync()));
        }

        await using (var duplicateTitle = Db.CreateContext())
        {
            duplicateTitle.RoleListTitles.Add(new RoleListTitle(one.Id, "product owner", title.Id));
            Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => duplicateTitle.SaveChangesAsync()));
        }

        var other = new RoleListTitle(two.Id, "Product Owner");
        await AddAsync(other, new RoleListTitle(two.Id, "PO", other.Id));
        Assert.Equal(4, await CountAsync("role_list_titles"));
    }

    [Fact]
    public async Task A_blank_title_and_a_title_that_is_its_own_primary_are_rejected()
    {
        var list = await AddRoleListAsync();

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO role_list_titles (\"Id\", \"RoleListId\", \"Title\", \"NormalizedTitle\") VALUES (gen_random_uuid(), '{list.Id}', '   ', 'x')")));

        var id = Guid.NewGuid();
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO role_list_titles (\"Id\", \"RoleListId\", \"Title\", \"NormalizedTitle\", \"PrimaryTitleId\") VALUES ('{id}', '{list.Id}', 'Self', 'self', '{id}')")));
    }

    [Fact]
    public async Task Deleting_a_primary_title_removes_its_aliases_and_deleting_a_list_removes_all_its_titles()
    {
        var list = await AddRoleListAsync();
        var first = new RoleListTitle(list.Id, "Product Owner");
        var second = new RoleListTitle(list.Id, "Scrum Master");
        await AddAsync(first, new RoleListTitle(list.Id, "PO", first.Id), new RoleListTitle(list.Id, "Owner", first.Id), second, new RoleListTitle(list.Id, "SM", second.Id));

        await Db.ExecuteAsync($"DELETE FROM role_list_titles WHERE \"Id\" = '{first.Id}'");
        Assert.Equal(2, await CountAsync("role_list_titles"));

        await Db.ExecuteAsync($"DELETE FROM role_lists WHERE \"Id\" = '{list.Id}'");
        Assert.Equal(0, await CountAsync("role_list_titles"));
    }

    // ---- location sets -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Location_set_names_are_unique_after_normalization()
    {
        await AddLocationSetAsync("Target cities");

        await using var second = Db.CreateContext();
        second.LocationSets.Add(new LocationSet("TARGET  cities"));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => second.SaveChangesAsync()));
    }

    [Fact]
    public async Task Remote_in_a_country_needs_a_country_and_remote_worldwide_must_have_none()
    {
        var set = await AddLocationSetAsync();

        await AddAsync(
            new LocationSetLocation(set.Id, "Remote India", LocationScopes.RemoteCountry, "IN"),
            new LocationSetLocation(set.Id, "Remote worldwide", LocationScopes.RemoteWorldwide, null));

        await using (var noCountry = Db.CreateContext())
        {
            noCountry.LocationSetLocations.Add(new LocationSetLocation(set.Id, "Remote somewhere", LocationScopes.RemoteCountry, null));
            Assert.Equal(CheckViolation, await SqlStateOfAsync(() => noCountry.SaveChangesAsync()));
        }

        await using var withCountry = Db.CreateContext();
        withCountry.LocationSetLocations.Add(new LocationSetLocation(set.Id, "Remote anywhere", LocationScopes.RemoteWorldwide, "IN"));
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => withCountry.SaveChangesAsync()));
    }

    [Fact]
    public async Task Scope_country_code_format_and_blank_label_are_enforced_by_the_database()
    {
        var set = await AddLocationSetAsync();
        const string columns = "\"Id\", \"LocationSetId\", \"Label\", \"NormalizedLabel\", \"Scope\", \"CountryCode\"";

        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO location_set_locations ({columns}) VALUES (gen_random_uuid(), '{set.Id}', 'Pune', 'pune', 'planet', NULL)")));
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO location_set_locations ({columns}) VALUES (gen_random_uuid(), '{set.Id}', 'Pune', 'pune', 'city', 'in')")));
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO location_set_locations ({columns}) VALUES (gen_random_uuid(), '{set.Id}', 'Pune', 'pune', 'city', '1N')")));
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO location_set_locations ({columns}) VALUES (gen_random_uuid(), '{set.Id}', '  ', 'x', 'city', NULL)")));
    }

    [Fact]
    public async Task One_label_and_scope_repeats_only_with_a_different_country_and_a_missing_country_counts_as_a_value()
    {
        var set = await AddLocationSetAsync();
        await AddAsync(
            new LocationSetLocation(set.Id, "Springfield", LocationScopes.City, "US"),
            new LocationSetLocation(set.Id, "Springfield", LocationScopes.City, "CA"),
            new LocationSetLocation(set.Id, "Springfield", LocationScopes.City, null),
            new LocationSetLocation(set.Id, "Springfield", LocationScopes.State, "US"));

        foreach (var country in new string?[] { "US", null })
        {
            await using var duplicate = Db.CreateContext();
            duplicate.LocationSetLocations.Add(new LocationSetLocation(set.Id, " springfield ", LocationScopes.City, country));
            Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => duplicate.SaveChangesAsync()));
        }

        var other = await AddLocationSetAsync("Other");
        await AddAsync(new LocationSetLocation(other.Id, "Springfield", LocationScopes.City, "US"));
        Assert.Equal(5, await CountAsync("location_set_locations"));
    }

    [Fact]
    public async Task Deleting_a_location_set_removes_its_locations()
    {
        var set = await AddLocationSetAsync();
        await AddAsync(new LocationSetLocation(set.Id, "Pune", LocationScopes.City, null));

        await Db.ExecuteAsync($"DELETE FROM location_sets WHERE \"Id\" = '{set.Id}'");

        Assert.Equal(0, await CountAsync("location_set_locations"));
    }

    // ---- profile selections ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_role_list_or_location_set_that_a_profile_selects_cannot_be_deleted_but_deleting_the_profile_frees_it()
    {
        var candidate = await AddCandidateAsync();
        var roles = await AddRoleListAsync();
        var places = await AddLocationSetAsync();
        var profile = new SearchProfile(candidate.Id, "Profile");
        profile.RoleLists.Add(new SearchProfileRoleList { RoleListId = roles.Id });
        profile.LocationSets.Add(new SearchProfileLocationSet { LocationSetId = places.Id });
        await AddAsync(profile);

        AssertDeleteBlocked(await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM role_lists WHERE \"Id\" = '{roles.Id}'")));
        AssertDeleteBlocked(await SqlStateOfAsync(() => Db.ExecuteAsync($"DELETE FROM location_sets WHERE \"Id\" = '{places.Id}'")));

        await Db.ExecuteAsync($"DELETE FROM search_profiles WHERE \"Id\" = '{profile.Id}'");
        Assert.Equal(0, await CountAsync("search_profile_role_lists"));
        Assert.Equal(0, await CountAsync("search_profile_location_sets"));
        await Db.ExecuteAsync($"DELETE FROM role_lists WHERE \"Id\" = '{roles.Id}'");
        await Db.ExecuteAsync($"DELETE FROM location_sets WHERE \"Id\" = '{places.Id}'");
    }

    [Fact]
    public async Task The_same_list_cannot_be_selected_twice_by_one_profile_and_profile_version_is_untouched()
    {
        var candidate = await AddCandidateAsync();
        var roles = await AddRoleListAsync();
        var profile = new SearchProfile(candidate.Id, "Profile");
        profile.RoleLists.Add(new SearchProfileRoleList { RoleListId = roles.Id });
        await AddAsync(profile);

        await using var second = Db.CreateContext();
        second.SearchProfileRoleLists.Add(new SearchProfileRoleList { SearchProfileId = profile.Id, RoleListId = roles.Id });
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => second.SaveChangesAsync()));

        await using var read = Db.CreateContext();
        Assert.Equal(1, (await read.SearchProfiles.SingleAsync(p => p.Id == profile.Id)).ProfileVersion);
    }
}
