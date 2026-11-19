using System.Text.RegularExpressions;
using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Named, reusable location sets (OQ-P1.2). Each location is a free-text label with a required scope (city, state,
/// country, remote within a country, remote worldwide) and an optional ISO 3166-1 alpha-2 country code. There is no built-in
/// place list, so nothing is hardcoded to one country. "Remote India" (<c>remoteCountry</c> + <c>IN</c>) and "Remote
/// worldwide" are different scopes. Work arrangement (remote, on-site, hybrid) stays a separate field and is not part
/// of a location. Nothing here changes a profile's <c>profileVersion</c>.
/// </summary>
public static partial class LocationSetEndpoints
{
    public static RouteGroupBuilder MapLocationSetEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/location-sets").WithTags("Location sets");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/locations", AddLocationAsync);
        group.MapPut("/{id:guid}/locations/{locationId:guid}", UpdateLocationAsync);
        group.MapDelete("/{id:guid}/locations/{locationId:guid}", DeleteLocationAsync);

        return api;
    }

    [GeneratedRegex("^[A-Za-z]{2}$")]
    private static partial Regex CountryCodePattern();

    private static async Task<IResult> CreateAsync(LocationSetRequest? request, AppDbContext db, CancellationToken ct)
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

        if (await db.LocationSets.AnyAsync(l => l.NormalizedName == normalized, ct))
        {
            return DuplicateName(display);
        }

        var set = new LocationSet(display);
        db.LocationSets.Add(set);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return DuplicateName(display);
        }

        return Results.Created($"/api/location-sets/{set.Id}", new LocationSetResponse(set.Id, set.Name, 0, []));
    }

    private static async Task<IResult> ListAsync(AppDbContext db, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue))
        {
            return errors.ToResult();
        }

        var rows = await db.LocationSets.AsNoTracking()
            .OrderBy(l => l.NormalizedName).ThenBy(l => l.Id)
            .Skip(skipValue).Take(takeValue)
            .Select(l => new { l.Id, l.Name, LocationCount = l.Locations.Count })
            .ToListAsync(ct);

        return Results.Ok(rows.Select(r => new LocationSetSummaryResponse(r.Id, r.Name, r.LocationCount)).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Location set", id) : Results.Ok(response);
    }

    private static async Task<IResult> UpdateAsync(Guid id, LocationSetRequest? request, AppDbContext db, CancellationToken ct)
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

        var set = await db.LocationSets.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (set is null)
        {
            return ApiResults.NotFound("Location set", id);
        }

        if (await db.LocationSets.AnyAsync(l => l.NormalizedName == normalized && l.Id != id, ct))
        {
            return DuplicateName(display);
        }

        set.Rename(display);
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
        var set = await db.LocationSets.SingleOrDefaultAsync(l => l.Id == id, ct);
        if (set is null)
        {
            return ApiResults.NotFound("Location set", id);
        }

        var profileCount = await db.SearchProfileLocationSets.CountAsync(x => x.LocationSetId == id, ct);
        if (profileCount > 0)
        {
            return InUse(id, profileCount);
        }

        // The database removes the set's locations (cascade).
        db.LocationSets.Remove(set);
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

    private static async Task<IResult> AddLocationAsync(Guid id, LocationRequest? request, AppDbContext db, CancellationToken ct)
    {
        if (!TryValidate(request, out var label, out var scope, out var countryCode, out var invalid))
        {
            return invalid!;
        }

        if (!await db.LocationSets.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Location set", id);
        }

        var normalized = NameNormalizer.Normalize(label);
        if (await db.LocationSetLocations.AnyAsync(
                l => l.LocationSetId == id && l.Scope == scope && l.NormalizedLabel == normalized && l.CountryCode == countryCode, ct))
        {
            return Duplicate(label);
        }

        var location = new LocationSetLocation(id, label, scope, countryCode);
        db.LocationSetLocations.Add(location);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Duplicate(label);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            return ApiResults.NotFound("Location set not found", "The location set was deleted while the request ran.");
        }

        return Results.Created($"/api/location-sets/{id}/locations/{location.Id}", LocationResponse.From(location));
    }

    private static async Task<IResult> UpdateLocationAsync(Guid id, Guid locationId, LocationRequest? request, AppDbContext db, CancellationToken ct)
    {
        if (!TryValidate(request, out var label, out var scope, out var countryCode, out var invalid))
        {
            return invalid!;
        }

        if (!await db.LocationSets.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Location set", id);
        }

        var location = await db.LocationSetLocations.SingleOrDefaultAsync(l => l.Id == locationId && l.LocationSetId == id, ct);
        if (location is null)
        {
            return LocationNotFound(id, locationId);
        }

        var normalized = NameNormalizer.Normalize(label);
        if (await db.LocationSetLocations.AnyAsync(
                l => l.LocationSetId == id && l.Id != locationId && l.Scope == scope && l.NormalizedLabel == normalized && l.CountryCode == countryCode, ct))
        {
            return Duplicate(label);
        }

        location.Change(label, scope, countryCode);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return Duplicate(label);
        }

        return Results.Ok(LocationResponse.From(location));
    }

    private static async Task<IResult> DeleteLocationAsync(Guid id, Guid locationId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.LocationSets.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Location set", id);
        }

        var location = await db.LocationSetLocations.SingleOrDefaultAsync(l => l.Id == locationId && l.LocationSetId == id, ct);
        if (location is null)
        {
            return LocationNotFound(id, locationId);
        }

        db.LocationSetLocations.Remove(location);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool TryValidate(LocationRequest? request, out string label, out string scope, out string? countryCode, out IResult? invalid)
    {
        label = string.Empty;
        scope = string.Empty;
        countryCode = null;
        invalid = null;
        var errors = new ValidationErrors();
        if (request is null)
        {
            errors.Add("label", "A request body is required.");
            invalid = errors.ToResult();
            return false;
        }

        Input.TryUniqueName(request.Label, "label", errors, out label, out _);

        var knownScope = LocationScopes.All.FirstOrDefault(s => string.Equals(s, request.Scope?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (knownScope is null)
        {
            errors.Add("scope", $"A scope is required. Allowed values: {string.Join(", ", LocationScopes.All)}.");
        }
        else
        {
            scope = knownScope;
        }

        var rawCode = request.CountryCode?.Trim();
        if (!string.IsNullOrEmpty(rawCode))
        {
            if (CountryCodePattern().IsMatch(rawCode))
            {
                countryCode = rawCode.ToUpperInvariant();
            }
            else
            {
                errors.Add("countryCode", "A country code must be two letters (ISO 3166-1 alpha-2), for example IN.");
            }
        }

        if (knownScope == LocationScopes.RemoteCountry && countryCode is null && string.IsNullOrEmpty(rawCode))
        {
            errors.Add("countryCode", "A country code is required for the remoteCountry scope.");
        }

        if (knownScope == LocationScopes.RemoteWorldwide && !string.IsNullOrEmpty(rawCode))
        {
            errors.Add("countryCode", "A country code must be empty for the remoteWorldwide scope.");
        }

        if (errors.Any)
        {
            invalid = errors.ToResult();
            return false;
        }

        return true;
    }

    private static async Task<LocationSetResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var set = await db.LocationSets.AsNoTracking()
            .Include(l => l.Locations)
            .SingleOrDefaultAsync(l => l.Id == id, ct);
        if (set is null)
        {
            return null;
        }

        var locations = set.Locations
            .OrderBy(l => l.NormalizedLabel, StringComparer.Ordinal)
            .ThenBy(l => l.Scope, StringComparer.Ordinal)
            .ThenBy(l => l.CountryCode, StringComparer.Ordinal)
            .ThenBy(l => l.Id)
            .Select(LocationResponse.From)
            .ToList();
        return new LocationSetResponse(set.Id, set.Name, locations.Count, locations);
    }

    private static IResult DuplicateName(string display) =>
        ApiResults.Conflict(
            ApiResults.DuplicateNameCode,
            "Location set name already exists",
            $"A location set named '{display}' already exists (names are compared after normalization).");

    private static IResult Duplicate(string label) =>
        ApiResults.Conflict(
            ApiResults.DuplicateLocationCode,
            "Location already exists in this set",
            $"This set already has '{label}' with the same scope and country (labels are compared after normalization).");

    private static IResult LocationNotFound(Guid id, Guid locationId) =>
        ApiResults.NotFound("Location not found", $"Location set {id} has no location {locationId}.");

    private static IResult InUse(Guid id, int? profileCount) =>
        ApiResults.Conflict(
            ApiResults.LocationSetInUseCode,
            "Location set is used by a search profile",
            $"Location set {id} is selected by at least one search profile. Remove it from those profiles first.",
            profileCount is null ? null : new Dictionary<string, object?> { ["searchProfileCount"] = profileCount });
}
