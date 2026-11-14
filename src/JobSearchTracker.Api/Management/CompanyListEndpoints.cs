using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Named, reusable company lists. Employers are canonical and may be in many lists; membership changes only through the
/// explicit association endpoints, never as a side effect of updating the list.
/// </summary>
public static class CompanyListEndpoints
{
    public static RouteGroupBuilder MapCompanyListEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/company-lists").WithTags("Company lists");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPut("/{id:guid}/employers/{employerId:guid}", AddEmployerAsync);
        group.MapDelete("/{id:guid}/employers/{employerId:guid}", RemoveEmployerAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(CompanyListRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryUniqueName(request?.Name, "name", errors, out var display, out var normalized);
        Input.TryLocationLabel(request?.LocationLabel, "locationLabel", errors, out var label);
        WorkModesMapping.TryParse(request?.DefaultWorkModes, "defaultWorkModes", errors, out var modes);
        if (request is null)
        {
            errors.Add("name", "A request body is required.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        if (await db.CompanyLists.AnyAsync(l => l.NormalizedName == normalized, ct))
        {
            return DuplicateName(display);
        }

        var list = new CompanyList(display, label, modes);
        db.CompanyLists.Add(list);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateName(display);
        }

        return Results.Created($"/api/company-lists/{list.Id}", ToResponse(list, []));
    }

    private static async Task<IResult> ListAsync(AppDbContext db, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue))
        {
            return errors.ToResult();
        }

        var rows = await db.CompanyLists.AsNoTracking()
            .OrderBy(l => l.NormalizedName).ThenBy(l => l.Id)
            .Skip(skipValue).Take(takeValue)
            .Select(l => new
            {
                l.Id,
                l.Name,
                l.LocationLabel,
                l.DefaultWorkModes,
                EmployerCount = l.Employers.Count,
            })
            .ToListAsync(ct);

        return Results.Ok(rows
            .Select(r => new CompanyListSummaryResponse(
                r.Id, r.Name, r.LocationLabel, WorkModesMapping.ToNames(r.DefaultWorkModes), r.EmployerCount))
            .ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Company list", id) : Results.Ok(response);
    }

    private static async Task<IResult> UpdateAsync(Guid id, CompanyListRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryUniqueName(request?.Name, "name", errors, out var display, out var normalized);
        Input.TryLocationLabel(request?.LocationLabel, "locationLabel", errors, out var label);
        WorkModesMapping.TryParse(request?.DefaultWorkModes, "defaultWorkModes", errors, out var modes);
        if (request is null)
        {
            errors.Add("name", "A request body is required.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        var list = await db.CompanyLists.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (list is null)
        {
            return ApiResults.NotFound("Company list", id);
        }

        if (await db.CompanyLists.AnyAsync(l => l.NormalizedName == normalized && l.Id != id, ct))
        {
            return DuplicateName(display);
        }

        list.Rename(display);
        list.SetLocationLabel(label);
        list.DefaultWorkModes = modes;
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
        var list = await db.CompanyLists.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (list is null)
        {
            return ApiResults.NotFound("Company list", id);
        }

        var profileCount = await db.SearchProfileCompanyLists.CountAsync(x => x.CompanyListId == id, ct);
        if (profileCount > 0)
        {
            return InUse(id, profileCount);
        }

        // The database removes the list's employer links (cascade); the employers themselves are kept.
        db.CompanyLists.Remove(list);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return InUse(id, null);
        }

        return Results.NoContent();
    }

    /// <summary>Adds the employer to the list. Idempotent: a second add of the same employer is a no-op (204).</summary>
    private static async Task<IResult> AddEmployerAsync(Guid id, Guid employerId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.CompanyLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Company list", id);
        }

        if (!await db.Employers.AnyAsync(e => e.Id == employerId, ct))
        {
            return ApiResults.NotFound("Employer", employerId);
        }

        if (await db.CompanyListEmployers.AnyAsync(x => x.CompanyListId == id && x.EmployerId == employerId, ct))
        {
            return Results.NoContent();
        }

        db.CompanyListEmployers.Add(new CompanyListEmployer { CompanyListId = id, EmployerId = employerId });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Added concurrently: the desired state already holds.
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return ApiResults.NotFound("Company list or employer not found", "The list or the employer was deleted while the request ran.");
        }

        return Results.NoContent();
    }

    /// <summary>Removes the association only; the employer and the list stay. 404 when the employer is not in the list.</summary>
    private static async Task<IResult> RemoveEmployerAsync(Guid id, Guid employerId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.CompanyLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Company list", id);
        }

        var link = await db.CompanyListEmployers.SingleOrDefaultAsync(x => x.CompanyListId == id && x.EmployerId == employerId, ct);
        if (link is null)
        {
            return ApiResults.NotFound(
                "Employer is not in this list",
                $"Employer {employerId} is not a member of company list {id}.",
                ApiResults.NotAMemberCode);
        }

        db.CompanyListEmployers.Remove(link);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<CompanyListResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var list = await db.CompanyLists.AsNoTracking()
            .Include(l => l.Employers).ThenInclude(x => x.Employer)
            .SingleOrDefaultAsync(l => l.Id == id, ct);
        return list is null ? null : ToResponse(list, list.Employers.Select(x => x.Employer).ToList());
    }

    private static CompanyListResponse ToResponse(CompanyList list, IReadOnlyCollection<Employer> employers) =>
        new(
            list.Id,
            list.Name,
            list.LocationLabel,
            WorkModesMapping.ToNames(list.DefaultWorkModes),
            employers.Count,
            employers.OrderBy(e => e.NormalizedName, StringComparer.Ordinal).Select(EmployerResponse.From).ToList());

    private static IResult DuplicateName(string display) =>
        ApiResults.Conflict(
            ApiResults.DuplicateNameCode,
            "Company list name already exists",
            $"A company list named '{display}' already exists (names are compared after normalization).");

    private static IResult InUse(Guid id, int? profileCount) =>
        ApiResults.Conflict(
            ApiResults.CompanyListInUseCode,
            "Company list is used by a search profile",
            $"Company list {id} is selected by at least one search profile. Remove it from those profiles first.",
            profileCount is null ? null : new Dictionary<string, object?> { ["searchProfileCount"] = profileCount });
}
