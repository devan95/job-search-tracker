using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Named, reusable role lists (OQ-P1.1). A list holds primary titles, each with zero or more aliases. Titles and aliases
/// are unique within one list after name normalization; the same title may be in several lists. Phase 1 only stores them:
/// how a job title is matched against them is Phase 3 (S3.2). Nothing here changes a profile's <c>profileVersion</c>.
/// </summary>
public static class RoleListEndpoints
{
    public const int MaxAliasesPerTitle = 50;

    public static RouteGroupBuilder MapRoleListEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/role-lists").WithTags("Role lists");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/titles", AddTitleAsync);
        group.MapPut("/{id:guid}/titles/{titleId:guid}", UpdateTitleAsync);
        group.MapDelete("/{id:guid}/titles/{titleId:guid}", DeleteTitleAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(RoleListRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryUniqueName(request?.Name, "name", errors, out var display, out var normalized);
        if (request is null)
        {
            errors.Add("name", "A request body is required.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        if (await db.RoleLists.AnyAsync(l => l.NormalizedName == normalized, ct))
        {
            return DuplicateName(display);
        }

        var list = new RoleList(display);
        db.RoleLists.Add(list);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateName(display);
        }

        return Results.Created($"/api/role-lists/{list.Id}", new RoleListResponse(list.Id, list.Name, 0, 0, []));
    }

    private static async Task<IResult> ListAsync(AppDbContext db, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue))
        {
            return errors.ToResult();
        }

        var rows = await db.RoleLists.AsNoTracking()
            .OrderBy(l => l.NormalizedName).ThenBy(l => l.Id)
            .Skip(skipValue).Take(takeValue)
            .Select(l => new
            {
                l.Id,
                l.Name,
                TitleCount = l.Titles.Count(t => t.PrimaryTitleId == null),
                AliasCount = l.Titles.Count(t => t.PrimaryTitleId != null),
            })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new RoleListSummaryResponse(r.Id, r.Name, r.TitleCount, r.AliasCount)).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Role list", id) : Results.Ok(response);
    }

    private static async Task<IResult> UpdateAsync(Guid id, RoleListRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryUniqueName(request?.Name, "name", errors, out var display, out var normalized);
        if (request is null)
        {
            errors.Add("name", "A request body is required.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        var list = await db.RoleLists.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (list is null)
        {
            return ApiResults.NotFound("Role list", id);
        }

        if (await db.RoleLists.AnyAsync(l => l.NormalizedName == normalized && l.Id != id, ct))
        {
            return DuplicateName(display);
        }

        list.Rename(display);
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
        var list = await db.RoleLists.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (list is null)
        {
            return ApiResults.NotFound("Role list", id);
        }

        var profileCount = await db.SearchProfileRoleLists.CountAsync(x => x.RoleListId == id, ct);
        if (profileCount > 0)
        {
            return InUse(id, profileCount);
        }

        // The database removes the list's titles and aliases (cascade).
        db.RoleLists.Remove(list);
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

    private static async Task<IResult> AddTitleAsync(Guid id, RoleTitleRequest? request, AppDbContext db, CancellationToken ct)
    {
        if (!TryValidate(request, out var title, out var aliases, out var invalid))
        {
            return invalid!;
        }

        if (!await db.RoleLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Role list", id);
        }

        var taken = await TakenAsync(db, id, null, ct);
        var clash = FirstClash(title, aliases, taken);
        if (clash is not null)
        {
            return DuplicateTitle(clash);
        }

        var primary = new RoleListTitle(id, title);
        db.RoleListTitles.Add(primary);
        foreach (var alias in aliases)
        {
            db.RoleListTitles.Add(new RoleListTitle(id, alias, primary.Id));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateTitle(title);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return ApiResults.NotFound("Role list not found", "The role list was deleted while the request ran.");
        }

        db.ChangeTracker.Clear();
        return Results.Created($"/api/role-lists/{id}/titles/{primary.Id}", await LoadTitleAsync(db, id, primary.Id, ct));
    }

    private static async Task<IResult> UpdateTitleAsync(Guid id, Guid titleId, RoleTitleRequest? request, AppDbContext db, CancellationToken ct)
    {
        if (!TryValidate(request, out var title, out var aliases, out var invalid))
        {
            return invalid!;
        }

        if (!await db.RoleLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Role list", id);
        }

        var primary = await db.RoleListTitles
            .Include(t => t.Aliases)
            .SingleOrDefaultAsync(t => t.Id == titleId && t.RoleListId == id && t.PrimaryTitleId == null, ct);
        if (primary is null)
        {
            return TitleNotFound(id, titleId);
        }

        // Names owned by this title (itself and its aliases) do not clash with its own new values.
        var taken = await TakenAsync(db, id, titleId, ct);
        var clash = FirstClash(title, aliases, taken);
        if (clash is not null)
        {
            return DuplicateTitle(clash);
        }

        // Replace the whole alias set: delete first and save, so a renamed alias never meets its old row in the unique index.
        primary.Rename(title);
        db.RoleListTitles.RemoveRange(primary.Aliases);
        try
        {
            // One transaction, so a failure leaves the title and its aliases as they were.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            foreach (var alias in aliases)
            {
                db.RoleListTitles.Add(new RoleListTitle(id, alias, titleId));
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateTitle(title);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return TitleNotFound(id, titleId);
        }

        db.ChangeTracker.Clear();
        return Results.Ok(await LoadTitleAsync(db, id, titleId, ct));
    }

    /// <summary>Removes the title and, by cascade, its aliases. The list stays.</summary>
    private static async Task<IResult> DeleteTitleAsync(Guid id, Guid titleId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.RoleLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Role list", id);
        }

        var primary = await db.RoleListTitles
            .SingleOrDefaultAsync(t => t.Id == titleId && t.RoleListId == id && t.PrimaryTitleId == null, ct);
        if (primary is null)
        {
            return TitleNotFound(id, titleId);
        }

        db.RoleListTitles.Remove(primary);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool TryValidate(RoleTitleRequest? request, out string title, out List<string> aliases, out IResult? invalid)
    {
        title = string.Empty;
        aliases = [];
        invalid = null;
        var errors = new ValidationErrors();
        if (request is null)
        {
            errors.Add("title", "A request body is required.");
            invalid = errors.ToResult();
            return false;
        }

        Input.TryUniqueName(request.Title, "title", errors, out title, out var normalizedTitle);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in request.Aliases ?? Array.Empty<string>())
        {
            if (!Input.TryUniqueName(raw, "aliases", errors, out var display, out var normalized))
            {
                continue;
            }

            if (normalized == normalizedTitle)
            {
                errors.Add("aliases", $"The alias '{display}' is the same as the title.");
                continue;
            }

            // A repeated alias in one request is kept once.
            if (seen.Add(normalized))
            {
                aliases.Add(display);
            }
        }

        if (aliases.Count > MaxAliasesPerTitle)
        {
            errors.Add("aliases", $"A title can have at most {MaxAliasesPerTitle} aliases.");
        }

        if (errors.Any)
        {
            invalid = errors.ToResult();
            return false;
        }

        return true;
    }

    /// <summary>Normalized titles and aliases already in the list, leaving out the title being replaced and its aliases.</summary>
    private static async Task<HashSet<string>> TakenAsync(AppDbContext db, Guid roleListId, Guid? exceptTitleId, CancellationToken ct)
    {
        var rows = await db.RoleListTitles.AsNoTracking()
            .Where(t => t.RoleListId == roleListId && (exceptTitleId == null || (t.Id != exceptTitleId && t.PrimaryTitleId != exceptTitleId)))
            .Select(t => t.NormalizedTitle)
            .ToListAsync(ct);
        return rows.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Returns the first title or alias (as typed) that is already used in the list, or null.</summary>
    private static string? FirstClash(string title, List<string> aliases, HashSet<string> taken)
    {
        foreach (var value in aliases.Prepend(title))
        {
            if (taken.Contains(NameNormalizer.Normalize(value)))
            {
                return value;
            }
        }

        return null;
    }

    private static async Task<RoleListResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var list = await db.RoleLists.AsNoTracking()
            .Include(l => l.Titles)
            .SingleOrDefaultAsync(l => l.Id == id, ct);
        if (list is null)
        {
            return null;
        }

        var titles = ToTitleResponses(list.Titles);
        return new RoleListResponse(list.Id, list.Name, titles.Count, titles.Sum(t => t.Aliases.Count), titles);
    }

    private static async Task<RoleTitleResponse?> LoadTitleAsync(AppDbContext db, Guid roleListId, Guid titleId, CancellationToken ct)
    {
        var rows = await db.RoleListTitles.AsNoTracking()
            .Where(t => t.RoleListId == roleListId && (t.Id == titleId || t.PrimaryTitleId == titleId))
            .ToListAsync(ct);
        return ToTitleResponses(rows).SingleOrDefault();
    }

    private static List<RoleTitleResponse> ToTitleResponses(IEnumerable<RoleListTitle> rows)
    {
        var all = rows.ToList();
        return all.Where(t => t.PrimaryTitleId is null)
            .OrderBy(t => t.NormalizedTitle, StringComparer.Ordinal)
            .Select(t => new RoleTitleResponse(
                t.Id,
                t.Title,
                all.Where(a => a.PrimaryTitleId == t.Id)
                    .OrderBy(a => a.NormalizedTitle, StringComparer.Ordinal)
                    .Select(a => new RoleAliasResponse(a.Id, a.Title))
                    .ToList()))
            .ToList();
    }

    private static IResult DuplicateName(string display) =>
        ApiResults.Conflict(
            ApiResults.DuplicateNameCode,
            "Role list name already exists",
            $"A role list named '{display}' already exists (names are compared after normalization).");

    private static IResult DuplicateTitle(string value) =>
        ApiResults.Conflict(
            ApiResults.DuplicateTitleCode,
            "Title or alias already exists in this role list",
            $"'{value}' is already a title or an alias in this role list (compared after normalization).");

    private static IResult TitleNotFound(Guid id, Guid titleId) =>
        ApiResults.NotFound("Title not found", $"Role list {id} has no title {titleId}.");

    private static IResult InUse(Guid id, int? profileCount) =>
        ApiResults.Conflict(
            ApiResults.RoleListInUseCode,
            "Role list is used by a search profile",
            $"Role list {id} is selected by at least one search profile. Remove it from those profiles first.",
            profileCount is null ? null : new Dictionary<string, object?> { ["searchProfileCount"] = profileCount });
}
