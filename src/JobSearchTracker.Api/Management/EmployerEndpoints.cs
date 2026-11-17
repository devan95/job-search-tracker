using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Canonical employers (name only in Phase 1). Names are unique after normalization. <c>POST /employers</c> reports a
/// duplicate as 409; <c>POST /employers/resolve</c> is the find-or-create used when a name should simply map to the
/// canonical employer.
/// </summary>
public static class EmployerEndpoints
{
    public static RouteGroupBuilder MapEmployerEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/employers").WithTags("Employers");

        group.MapPost("/", CreateAsync);
        group.MapPost("/resolve", ResolveAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/lookup", LookupAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(EmployerRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryUniqueName(request?.Name, "name", errors, out var display, out var normalized))
        {
            return errors.ToResult();
        }

        var existing = await db.Employers.AsNoTracking().SingleOrDefaultAsync(e => e.NormalizedName == normalized, ct);
        if (existing is not null)
        {
            return Duplicate(existing);
        }

        var employer = new Employer(display);
        db.Employers.Add(employer);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.Employers.AsNoTracking().SingleAsync(e => e.NormalizedName == normalized, ct);
            return Duplicate(winner);
        }

        return Results.Created($"/api/employers/{employer.Id}", EmployerResponse.From(employer));
    }

    private static async Task<IResult> ResolveAsync(EmployerRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryUniqueName(request?.Name, "name", errors, out var display, out var normalized))
        {
            return errors.ToResult();
        }

        var (employer, created) = await EmployerResolver.FindOrCreateAsync(db, display, normalized, ct);
        return created
            ? Results.Created($"/api/employers/{employer.Id}", EmployerResponse.From(employer))
            : Results.Ok(EmployerResponse.From(employer));
    }

    /// <summary>Employers whose normalized name contains <c>q</c> (normalized the same way), ordered by name.</summary>
    private static async Task<IResult> ListAsync(AppDbContext db, string? q, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue);

        string? needle = null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            if (Input.TryUniqueName(q, "q", errors, out _, out var normalizedQuery))
            {
                needle = normalizedQuery;
            }
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        var query = db.Employers.AsNoTracking();
        if (needle is not null)
        {
            query = query.Where(e => e.NormalizedName.Contains(needle));
        }

        var employers = await query
            .OrderBy(e => e.NormalizedName).ThenBy(e => e.Id)
            .Skip(skipValue).Take(takeValue)
            .ToListAsync(ct);
        return Results.Ok(employers.Select(EmployerResponse.From).ToList());
    }

    /// <summary>Exact canonical lookup: the employer whose normalized name equals the normalized <c>name</c>.</summary>
    private static async Task<IResult> LookupAsync(AppDbContext db, string? name, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryUniqueName(name, "name", errors, out _, out var normalized))
        {
            return errors.ToResult();
        }

        var employer = await db.Employers.AsNoTracking().SingleOrDefaultAsync(e => e.NormalizedName == normalized, ct);
        return employer is null
            ? ApiResults.NotFound("Employer not found", "No employer has this name after normalization.")
            : Results.Ok(EmployerResponse.From(employer));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var employer = await db.Employers.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
        return employer is null ? ApiResults.NotFound("Employer", id) : Results.Ok(EmployerResponse.From(employer));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var employer = await db.Employers.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (employer is null)
        {
            return ApiResults.NotFound("Employer", id);
        }

        var listCount = await db.CompanyListEmployers.CountAsync(x => x.EmployerId == id, ct);
        if (listCount > 0)
        {
            return InUse(id, listCount);
        }

        db.Employers.Remove(employer);
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

    private static IResult Duplicate(Employer existing) =>
        ApiResults.Conflict(
            ApiResults.DuplicateNameCode,
            "Employer name already exists",
            $"An employer named '{existing.Name}' already exists (names are compared after normalization).",
            new Dictionary<string, object?> { ["existingId"] = existing.Id });

    private static IResult InUse(Guid id, int? listCount) =>
        ApiResults.Conflict(
            ApiResults.EmployerInListCode,
            "Employer is in a company list",
            $"Employer {id} is still a member of at least one company list. Remove it from those lists first.",
            listCount is null ? null : new Dictionary<string, object?> { ["companyListCount"] = listCount });
}
