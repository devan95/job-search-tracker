using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// A candidate's reusable search configuration. Profile names are unique per candidate (after normalization). A profile
/// selects any number of company lists, role lists and location sets, independently of each other; selection changes only
/// through the explicit association endpoints (or the id lists given when the profile is created).
/// <c>profileVersion</c> is returned as stored; nothing increments it in Phase 1.
/// </summary>
public static class SearchProfileEndpoints
{
    public static RouteGroupBuilder MapSearchProfileEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/search-profiles").WithTags("Search profiles");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPut("/{id:guid}/company-lists/{companyListId:guid}", AddCompanyListAsync);
        group.MapDelete("/{id:guid}/company-lists/{companyListId:guid}", RemoveCompanyListAsync);
        group.MapPut("/{id:guid}/role-lists/{roleListId:guid}", AddRoleListAsync);
        group.MapDelete("/{id:guid}/role-lists/{roleListId:guid}", RemoveRoleListAsync);
        group.MapPut("/{id:guid}/location-sets/{locationSetId:guid}", AddLocationSetAsync);
        group.MapDelete("/{id:guid}/location-sets/{locationSetId:guid}", RemoveLocationSetAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(CreateSearchProfileRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (request is null)
        {
            errors.Add("name", "A request body is required.");
            return errors.ToResult();
        }

        Input.TryUniqueName(request.Name, "name", errors, out var display, out var normalized);
        var modesGiven = request.WorkModes is not null;
        WorkModesMapping.TryParse(request.WorkModes, "workModes", errors, out var modes);

        if (request.CandidateId is null || request.CandidateId == Guid.Empty)
        {
            errors.Add("candidateId", "A candidate is required.");
        }

        var listIds = (request.CompanyListIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var roleListIds = (request.RoleListIds ?? Array.Empty<Guid>()).Distinct().ToList();
        var locationSetIds = (request.LocationSetIds ?? Array.Empty<Guid>()).Distinct().ToList();
        if (errors.Any)
        {
            return errors.ToResult();
        }

        var candidateId = request.CandidateId!.Value;
        if (!await db.Candidates.AnyAsync(c => c.Id == candidateId, ct))
        {
            errors.Add("candidateId", $"Candidate {candidateId} does not exist.");
        }

        var knownListIds = await db.CompanyLists.Where(l => listIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
        foreach (var missing in listIds.Except(knownListIds))
        {
            errors.Add("companyListIds", $"Company list {missing} does not exist.");
        }

        var knownRoleListIds = await db.RoleLists.Where(l => roleListIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
        foreach (var missing in roleListIds.Except(knownRoleListIds))
        {
            errors.Add("roleListIds", $"Role list {missing} does not exist.");
        }

        var knownLocationSetIds = await db.LocationSets.Where(l => locationSetIds.Contains(l.Id)).Select(l => l.Id).ToListAsync(ct);
        foreach (var missing in locationSetIds.Except(knownLocationSetIds))
        {
            errors.Add("locationSetIds", $"Location set {missing} does not exist.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        if (await db.SearchProfiles.AnyAsync(p => p.CandidateId == candidateId && p.NormalizedName == normalized, ct))
        {
            return DuplicateName(display);
        }

        var profile = new SearchProfile(candidateId, display)
        {
            WorkModes = modesGiven ? modes : null,
            CompanyMonitoringEnabled = request.CompanyMonitoringEnabled ?? false,
            BroadSearchEnabled = request.BroadSearchEnabled ?? false,
        };
        foreach (var listId in listIds)
        {
            profile.CompanyLists.Add(new SearchProfileCompanyList { CompanyListId = listId });
        }

        foreach (var roleListId in roleListIds)
        {
            profile.RoleLists.Add(new SearchProfileRoleList { RoleListId = roleListId });
        }

        foreach (var locationSetId in locationSetIds)
        {
            profile.LocationSets.Add(new SearchProfileLocationSet { LocationSetId = locationSetId });
        }

        db.SearchProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateName(display);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            errors.Add("candidateId", "The candidate or a selected list or set was deleted while the request ran.");
            return errors.ToResult();
        }

        db.ChangeTracker.Clear();
        return Results.Created($"/api/search-profiles/{profile.Id}", await LoadAsync(db, profile.Id, ct));
    }

    private static async Task<IResult> ListAsync(AppDbContext db, Guid? candidateId, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue))
        {
            return errors.ToResult();
        }

        var query = db.SearchProfiles.AsNoTracking();
        if (candidateId is not null)
        {
            query = query.Where(p => p.CandidateId == candidateId);
        }

        var profiles = await query
            .Include(p => p.CompanyLists).ThenInclude(x => x.CompanyList)
            .Include(p => p.RoleLists).ThenInclude(x => x.RoleList)
            .Include(p => p.LocationSets).ThenInclude(x => x.LocationSet)
            .AsSplitQuery()
            .OrderBy(p => p.CandidateId).ThenBy(p => p.NormalizedName).ThenBy(p => p.Id)
            .Skip(skipValue).Take(takeValue)
            .ToListAsync(ct);
        return Results.Ok(profiles.Select(ToResponse).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Search profile", id) : Results.Ok(response);
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateSearchProfileRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (request is null)
        {
            errors.Add("name", "A request body is required.");
            return errors.ToResult();
        }

        Input.TryUniqueName(request.Name, "name", errors, out var display, out var normalized);
        var modesGiven = request.WorkModes is not null;
        WorkModesMapping.TryParse(request.WorkModes, "workModes", errors, out var modes);
        if (request.CompanyMonitoringEnabled is null)
        {
            errors.Add("companyMonitoringEnabled", "A value (true or false) is required.");
        }

        if (request.BroadSearchEnabled is null)
        {
            errors.Add("broadSearchEnabled", "A value (true or false) is required.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        var profile = await db.SearchProfiles.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return ApiResults.NotFound("Search profile", id);
        }

        if (await db.SearchProfiles.AnyAsync(
                p => p.CandidateId == profile.CandidateId && p.NormalizedName == normalized && p.Id != id, ct))
        {
            return DuplicateName(display);
        }

        profile.Rename(display);
        profile.WorkModes = modesGiven ? modes : null;
        profile.CompanyMonitoringEnabled = request.CompanyMonitoringEnabled!.Value;
        profile.BroadSearchEnabled = request.BroadSearchEnabled!.Value;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateName(display);
        }

        db.ChangeTracker.Clear();
        return Results.Ok(await LoadAsync(db, id, ct));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.SearchProfiles.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return ApiResults.NotFound("Search profile", id);
        }

        // The database removes the profile's list selections (cascade); candidates and lists are kept.
        db.SearchProfiles.Remove(profile);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Selects a company list for the profile. Idempotent: selecting it again is a no-op (204).</summary>
    private static async Task<IResult> AddCompanyListAsync(Guid id, Guid companyListId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Search profile", id);
        }

        if (!await db.CompanyLists.AnyAsync(l => l.Id == companyListId, ct))
        {
            return ApiResults.NotFound("Company list", companyListId);
        }

        if (await db.SearchProfileCompanyLists.AnyAsync(x => x.SearchProfileId == id && x.CompanyListId == companyListId, ct))
        {
            return Results.NoContent();
        }

        db.SearchProfileCompanyLists.Add(new SearchProfileCompanyList { SearchProfileId = id, CompanyListId = companyListId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Selected concurrently: the desired state already holds.
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return ApiResults.NotFound("Search profile or company list not found", "The profile or the list was deleted while the request ran.");
        }

        return Results.NoContent();
    }

    /// <summary>Unselects the list; the profile and the list stay. 404 when the profile does not select the list.</summary>
    private static async Task<IResult> RemoveCompanyListAsync(Guid id, Guid companyListId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Search profile", id);
        }

        var link = await db.SearchProfileCompanyLists
            .SingleOrDefaultAsync(x => x.SearchProfileId == id && x.CompanyListId == companyListId, ct);
        if (link is null)
        {
            return ApiResults.NotFound(
                "Company list is not selected by this profile",
                $"Company list {companyListId} is not selected by search profile {id}.",
                ApiResults.NotAMemberCode);
        }

        db.SearchProfileCompanyLists.Remove(link);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Selects a role list for the profile. Idempotent: selecting it again is a no-op (204).</summary>
    private static async Task<IResult> AddRoleListAsync(Guid id, Guid roleListId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Search profile", id);
        }

        if (!await db.RoleLists.AnyAsync(l => l.Id == roleListId, ct))
        {
            return ApiResults.NotFound("Role list", roleListId);
        }

        if (await db.SearchProfileRoleLists.AnyAsync(x => x.SearchProfileId == id && x.RoleListId == roleListId, ct))
        {
            return Results.NoContent();
        }

        db.SearchProfileRoleLists.Add(new SearchProfileRoleList { SearchProfileId = id, RoleListId = roleListId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Selected concurrently: the desired state already holds.
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return ApiResults.NotFound("Search profile or role list not found", "The profile or the list was deleted while the request ran.");
        }

        return Results.NoContent();
    }

    /// <summary>Unselects the role list; the profile and the list stay. 404 when the profile does not select the list.</summary>
    private static async Task<IResult> RemoveRoleListAsync(Guid id, Guid roleListId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Search profile", id);
        }

        var link = await db.SearchProfileRoleLists
            .SingleOrDefaultAsync(x => x.SearchProfileId == id && x.RoleListId == roleListId, ct);
        if (link is null)
        {
            return ApiResults.NotFound(
                "Role list is not selected by this profile",
                $"Role list {roleListId} is not selected by search profile {id}.",
                ApiResults.NotAMemberCode);
        }

        db.SearchProfileRoleLists.Remove(link);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row was removed by a concurrent request: say what is gone now.
            db.ChangeTracker.Clear();
            if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
            {
                return ApiResults.NotFound("Search profile", id);
            }

            return ApiResults.NotFound(
                "Role list is not selected by this profile",
                $"Role list {roleListId} is not selected by search profile {id}.",
                ApiResults.NotAMemberCode);
        }

        return Results.NoContent();
    }

    /// <summary>Selects a location set for the profile. Idempotent: selecting it again is a no-op (204).</summary>
    private static async Task<IResult> AddLocationSetAsync(Guid id, Guid locationSetId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Search profile", id);
        }

        if (!await db.LocationSets.AnyAsync(l => l.Id == locationSetId, ct))
        {
            return ApiResults.NotFound("Location set", locationSetId);
        }

        if (await db.SearchProfileLocationSets.AnyAsync(x => x.SearchProfileId == id && x.LocationSetId == locationSetId, ct))
        {
            return Results.NoContent();
        }

        db.SearchProfileLocationSets.Add(new SearchProfileLocationSet { SearchProfileId = id, LocationSetId = locationSetId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Selected concurrently: the desired state already holds.
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return ApiResults.NotFound("Search profile or location set not found", "The profile or the set was deleted while the request ran.");
        }

        return Results.NoContent();
    }

    /// <summary>Unselects the location set; the profile and the set stay. 404 when the profile does not select the set.</summary>
    private static async Task<IResult> RemoveLocationSetAsync(Guid id, Guid locationSetId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
        {
            return ApiResults.NotFound("Search profile", id);
        }

        var link = await db.SearchProfileLocationSets
            .SingleOrDefaultAsync(x => x.SearchProfileId == id && x.LocationSetId == locationSetId, ct);
        if (link is null)
        {
            return ApiResults.NotFound(
                "Location set is not selected by this profile",
                $"Location set {locationSetId} is not selected by search profile {id}.",
                ApiResults.NotAMemberCode);
        }

        db.SearchProfileLocationSets.Remove(link);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The row was removed by a concurrent request: say what is gone now.
            db.ChangeTracker.Clear();
            if (!await db.SearchProfiles.AnyAsync(p => p.Id == id, ct))
            {
                return ApiResults.NotFound("Search profile", id);
            }

            return ApiResults.NotFound(
                "Location set is not selected by this profile",
                $"Location set {locationSetId} is not selected by search profile {id}.",
                ApiResults.NotAMemberCode);
        }

        return Results.NoContent();
    }

    private static async Task<SearchProfileResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var profile = await db.SearchProfiles.AsNoTracking()
            .Include(p => p.CompanyLists).ThenInclude(x => x.CompanyList)
            .Include(p => p.RoleLists).ThenInclude(x => x.RoleList)
            .Include(p => p.LocationSets).ThenInclude(x => x.LocationSet)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == id, ct);
        return profile is null ? null : ToResponse(profile);
    }

    private static SearchProfileResponse ToResponse(SearchProfile p) =>
        new(
            p.Id,
            p.CandidateId,
            p.Name,
            p.WorkModes is null ? null : WorkModesMapping.ToNames(p.WorkModes.Value),
            p.CompanyMonitoringEnabled,
            p.BroadSearchEnabled,
            p.ProfileVersion,
            p.CompanyLists
                .Select(x => x.CompanyList)
                .OrderBy(l => l.NormalizedName, StringComparer.Ordinal)
                .Select(l => new CompanyListReference(l.Id, l.Name, WorkModesMapping.ToNames(l.DefaultWorkModes)))
                .ToList(),
            p.RoleLists
                .Select(x => x.RoleList)
                .OrderBy(l => l.NormalizedName, StringComparer.Ordinal)
                .Select(l => new RoleListReference(l.Id, l.Name))
                .ToList(),
            p.LocationSets
                .Select(x => x.LocationSet)
                .OrderBy(l => l.NormalizedName, StringComparer.Ordinal)
                .Select(l => new LocationSetReference(l.Id, l.Name))
                .ToList());

    private static IResult DuplicateName(string display) =>
        ApiResults.Conflict(
            ApiResults.DuplicateNameCode,
            "Search profile name already exists",
            $"This candidate already has a search profile named '{display}' (names are compared after normalization).");
}
