using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Tests;

/// <summary>AC3: uniqueness, work-mode semantics, version rule, delete behaviour and join rows.</summary>
public class DataModelBehaviourTests : DatabaseTestBase
{
    // ---- U2 uniqueness -------------------------------------------------------------------------------------

    [Fact]
    public async Task Employer_names_are_unique_after_normalization()
    {
        await using (var ctx = Db.CreateContext())
        {
            ctx.Employers.Add(new Employer("Acme Corp"));
            await ctx.SaveChangesAsync();
        }

        await using var second = Db.CreateContext();
        second.Employers.Add(new Employer("  ACME   corp "));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => second.SaveChangesAsync()));
    }

    [Fact]
    public async Task Company_list_names_are_unique_after_normalization()
    {
        await using (var ctx = Db.CreateContext())
        {
            ctx.CompanyLists.Add(new CompanyList("Pune Tech"));
            await ctx.SaveChangesAsync();
        }

        await using var second = Db.CreateContext();
        second.CompanyLists.Add(new CompanyList("pune  tech"));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => second.SaveChangesAsync()));
    }

    [Fact]
    public async Task Profile_names_are_unique_per_candidate_but_may_repeat_across_candidates()
    {
        var first = await AddCandidateAsync("First");
        var second = await AddCandidateAsync("Second");

        await using (var ctx = Db.CreateContext())
        {
            ctx.SearchProfiles.Add(new SearchProfile(first.Id, "Core search"));
            ctx.SearchProfiles.Add(new SearchProfile(second.Id, "Core search"));
            await ctx.SaveChangesAsync();
        }

        await using var duplicate = Db.CreateContext();
        duplicate.SearchProfiles.Add(new SearchProfile(first.Id, "  CORE   search "));
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => duplicate.SaveChangesAsync()));
    }

    [Fact]
    public async Task An_employer_can_be_in_several_lists_and_a_list_holds_many_employers()
    {
        var employer = new Employer("Shared Employer");
        var listA = new CompanyList("List A");
        var listB = new CompanyList("List B");
        await using (var ctx = Db.CreateContext())
        {
            ctx.AddRange(employer, listA, listB);
            listA.Employers.Add(new CompanyListEmployer { Employer = employer });
            listB.Employers.Add(new CompanyListEmployer { Employer = employer });
            await ctx.SaveChangesAsync();
        }

        await using var read = Db.CreateContext();
        Assert.Equal(2, await read.CompanyListEmployers.CountAsync(x => x.EmployerId == employer.Id));
        Assert.Equal(1, await read.Employers.CountAsync());
    }

    [Fact]
    public async Task The_same_employer_cannot_be_added_twice_to_one_list()
    {
        var employer = new Employer("Only Once");
        var list = new CompanyList("One list");
        await using (var ctx = Db.CreateContext())
        {
            ctx.AddRange(employer, list);
            list.Employers.Add(new CompanyListEmployer { Employer = employer });
            await ctx.SaveChangesAsync();
        }

        await using var again = Db.CreateContext();
        again.CompanyListEmployers.Add(new CompanyListEmployer { CompanyListId = list.Id, EmployerId = employer.Id });
        Assert.Equal(UniqueViolation, await SqlStateOfAsync(() => again.SaveChangesAsync()));
    }

    // ---- U1 / U4 / U7 / U9 value semantics -----------------------------------------------------------------

    [Fact]
    public async Task Location_label_is_nullable_free_text_and_work_mode_defaults_round_trip()
    {
        Guid withLabel, withoutLabel;
        await using (var ctx = Db.CreateContext())
        {
            var a = new CompanyList("With label", "Pune, Maharashtra", WorkModes.Remote | WorkModes.Hybrid);
            var b = new CompanyList("Without label");
            ctx.AddRange(a, b);
            await ctx.SaveChangesAsync();
            withLabel = a.Id;
            withoutLabel = b.Id;
        }

        await using var read = Db.CreateContext();
        var loadedA = await read.CompanyLists.SingleAsync(x => x.Id == withLabel);
        var loadedB = await read.CompanyLists.SingleAsync(x => x.Id == withoutLabel);
        Assert.Equal("Pune, Maharashtra", loadedA.LocationLabel);
        Assert.Equal(WorkModes.Remote | WorkModes.Hybrid, loadedA.DefaultWorkModes);
        Assert.Null(loadedB.LocationLabel);
        Assert.Equal(WorkModes.None, loadedB.DefaultWorkModes);
    }

    [Fact]
    public async Task Profile_work_modes_distinguish_null_from_the_empty_set()
    {
        var candidate = await AddCandidateAsync();
        Guid inherit, empty, some;
        await using (var ctx = Db.CreateContext())
        {
            var p1 = new SearchProfile(candidate.Id, "Inherit") { WorkModes = null };
            var p2 = new SearchProfile(candidate.Id, "Empty") { WorkModes = WorkModes.None };
            var p3 = new SearchProfile(candidate.Id, "Some") { WorkModes = WorkModes.OnSite | WorkModes.Hybrid };
            ctx.AddRange(p1, p2, p3);
            await ctx.SaveChangesAsync();
            (inherit, empty, some) = (p1.Id, p2.Id, p3.Id);
        }

        await using var read = Db.CreateContext();
        Assert.Null((await read.SearchProfiles.SingleAsync(x => x.Id == inherit)).WorkModes);
        Assert.Equal(WorkModes.None, (await read.SearchProfiles.SingleAsync(x => x.Id == empty)).WorkModes);
        Assert.Equal(WorkModes.OnSite | WorkModes.Hybrid, (await read.SearchProfiles.SingleAsync(x => x.Id == some)).WorkModes);

        var raw = await Db.QueryAsync(
            "SELECT \"Name\", \"WorkModes\" IS NULL, COALESCE(\"WorkModes\", -1) FROM search_profiles ORDER BY \"Name\"",
            r => (r.GetString(0), r.GetBoolean(1), r.GetInt32(2)));
        Assert.Equal(new[] {("Empty", false, 0), ("Inherit", true, -1), ("Some", false, 6)}, raw);
    }

    [Fact]
    public async Task Work_modes_outside_the_defined_bits_are_rejected()
    {
        var candidate = await AddCandidateAsync();
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => Db.ExecuteAsync(
            $"INSERT INTO search_profiles (\"Id\", \"CandidateId\", \"Name\", \"NormalizedName\", \"WorkModes\", " +
            $"\"CompanyMonitoringEnabled\", \"BroadSearchEnabled\", \"ProfileVersion\") " +
            $"VALUES (gen_random_uuid(), '{candidate.Id}', 'x', 'x', 8, false, false, 1)")));
    }

    [Fact]
    public async Task Draft_profile_with_both_discovery_modes_disabled_is_allowed_and_starts_at_version_one()
    {
        var candidate = await AddCandidateAsync();
        Guid id;
        await using (var ctx = Db.CreateContext())
        {
            var draft = new SearchProfile(candidate.Id, "Draft");
            ctx.SearchProfiles.Add(draft);
            await ctx.SaveChangesAsync();
            id = draft.Id;
        }

        await using var read = Db.CreateContext();
        var loaded = await read.SearchProfiles.SingleAsync(x => x.Id == id);
        Assert.False(loaded.CompanyMonitoringEnabled);
        Assert.False(loaded.BroadSearchEnabled);
        Assert.Equal(1, loaded.ProfileVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Profile_version_below_one_is_rejected_by_the_database(int version)
    {
        var candidate = await AddCandidateAsync();
        await using var ctx = Db.CreateContext();
        ctx.SearchProfiles.Add(new SearchProfile(candidate.Id, "Bad version") { ProfileVersion = version });
        Assert.Equal(CheckViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    // ---- U5 / U8 delete behaviour --------------------------------------------------------------------------

    [Fact]
    public async Task Candidate_with_profiles_cannot_be_deleted()
    {
        var candidate = await AddCandidateAsync();
        await using (var ctx = Db.CreateContext())
        {
            ctx.SearchProfiles.Add(new SearchProfile(candidate.Id, "Keeps candidate"));
            await ctx.SaveChangesAsync();
        }

        AssertDeleteBlocked(await SqlStateOfAsync(() =>
            Db.ExecuteAsync($"DELETE FROM candidates WHERE \"Id\" = '{candidate.Id}'")));
    }

    [Fact]
    public async Task Candidate_without_profiles_can_be_deleted()
    {
        var candidate = await AddCandidateAsync();
        await Db.ExecuteAsync($"DELETE FROM candidates WHERE \"Id\" = '{candidate.Id}'");
        Assert.Empty(await Db.QueryAsync("SELECT 1 FROM candidates", r => r.GetInt32(0)));
    }

    [Fact]
    public async Task A_profile_requires_an_existing_candidate()
    {
        await using var ctx = Db.CreateContext();
        ctx.SearchProfiles.Add(new SearchProfile(Guid.NewGuid(), "Orphan"));
        Assert.Equal(ForeignKeyViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }

    [Fact]
    public async Task Deleting_a_profile_removes_its_join_rows_but_keeps_lists_employers_and_the_candidate()
    {
        var candidate = await AddCandidateAsync();
        var employer = new Employer("Kept employer");
        var list = new CompanyList("Kept list");
        var profile = new SearchProfile(candidate.Id, "Doomed profile");
        await using (var ctx = Db.CreateContext())
        {
            ctx.AddRange(employer, list, profile);
            list.Employers.Add(new CompanyListEmployer { Employer = employer });
            profile.CompanyLists.Add(new SearchProfileCompanyList { CompanyList = list });
            await ctx.SaveChangesAsync();
        }

        await Db.ExecuteAsync($"DELETE FROM search_profiles WHERE \"Id\" = '{profile.Id}'");

        await using var read = Db.CreateContext();
        Assert.Equal(0, await read.SearchProfiles.CountAsync());
        Assert.Equal(0, await read.SearchProfileCompanyLists.CountAsync());
        Assert.Equal(1, await read.CompanyLists.CountAsync());
        Assert.Equal(1, await read.CompanyListEmployers.CountAsync());
        Assert.Equal(1, await read.Employers.CountAsync());
        Assert.Equal(1, await read.Candidates.CountAsync());
    }

    [Fact]
    public async Task Deleting_a_company_list_removes_its_employer_links_but_not_the_employers()
    {
        var employer = new Employer("Stays");
        var list = new CompanyList("Goes");
        await using (var ctx = Db.CreateContext())
        {
            ctx.AddRange(employer, list);
            list.Employers.Add(new CompanyListEmployer { Employer = employer });
            await ctx.SaveChangesAsync();
        }

        await Db.ExecuteAsync($"DELETE FROM company_lists WHERE \"Id\" = '{list.Id}'");

        await using var read = Db.CreateContext();
        Assert.Equal(0, await read.CompanyListEmployers.CountAsync());
        Assert.Equal(1, await read.Employers.CountAsync());
    }

    [Fact]
    public async Task A_company_list_selected_by_a_profile_cannot_be_deleted()
    {
        var candidate = await AddCandidateAsync();
        var list = new CompanyList("Selected list");
        var profile = new SearchProfile(candidate.Id, "Selecting profile");
        await using (var ctx = Db.CreateContext())
        {
            ctx.AddRange(list, profile);
            profile.CompanyLists.Add(new SearchProfileCompanyList { CompanyList = list });
            await ctx.SaveChangesAsync();
        }

        AssertDeleteBlocked(await SqlStateOfAsync(() =>
            Db.ExecuteAsync($"DELETE FROM company_lists WHERE \"Id\" = '{list.Id}'")));
    }

    [Fact]
    public async Task An_employer_referenced_by_a_list_cannot_be_deleted_until_unlinked()
    {
        var employer = new Employer("Referenced");
        var list = new CompanyList("Holder");
        await using (var ctx = Db.CreateContext())
        {
            ctx.AddRange(employer, list);
            list.Employers.Add(new CompanyListEmployer { Employer = employer });
            await ctx.SaveChangesAsync();
        }

        AssertDeleteBlocked(await SqlStateOfAsync(() =>
            Db.ExecuteAsync($"DELETE FROM employers WHERE \"Id\" = '{employer.Id}'")));

        await Db.ExecuteAsync($"DELETE FROM company_list_employers WHERE \"EmployerId\" = '{employer.Id}'");
        await Db.ExecuteAsync($"DELETE FROM employers WHERE \"Id\" = '{employer.Id}'");
        Assert.Empty(await Db.QueryAsync("SELECT 1 FROM employers", r => r.GetInt32(0)));
    }

    [Fact]
    public async Task Join_rows_must_reference_existing_rows()
    {
        await using var ctx = Db.CreateContext();
        ctx.CompanyListEmployers.Add(new CompanyListEmployer { CompanyListId = Guid.NewGuid(), EmployerId = Guid.NewGuid() });
        Assert.Equal(ForeignKeyViolation, await SqlStateOfAsync(() => ctx.SaveChangesAsync()));
    }
}
