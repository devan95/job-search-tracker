using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

public static class CandidateEndpoints
{
    public static RouteGroupBuilder MapCandidateEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/candidates").WithTags("Candidates");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(CandidateRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryDisplayName(request?.Name, "name", errors, out var name))
        {
            return errors.ToResult();
        }

        var candidate = new Candidate(name);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/candidates/{candidate.Id}", CandidateResponse.From(candidate));
    }

    private static async Task<IResult> ListAsync(AppDbContext db, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue))
        {
            return errors.ToResult();
        }

        var candidates = await db.Candidates.AsNoTracking()
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip(skipValue).Take(takeValue)
            .ToListAsync(ct);
        return Results.Ok(candidates.Select(CandidateResponse.From).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var candidate = await db.Candidates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        return candidate is null ? ApiResults.NotFound("Candidate", id) : Results.Ok(CandidateResponse.From(candidate));
    }

    private static async Task<IResult> UpdateAsync(Guid id, CandidateRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!Input.TryDisplayName(request?.Name, "name", errors, out var name))
        {
            return errors.ToResult();
        }

        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null)
        {
            return ApiResults.NotFound("Candidate", id);
        }

        candidate.Rename(name);
        await db.SaveChangesAsync(ct);
        return Results.Ok(CandidateResponse.From(candidate));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var candidate = await db.Candidates.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (candidate is null)
        {
            return ApiResults.NotFound("Candidate", id);
        }

        var profileCount = await db.SearchProfiles.CountAsync(p => p.CandidateId == id, ct);
        if (profileCount > 0)
        {
            return CandidateHasProfiles(id, profileCount);
        }

        db.Candidates.Remove(candidate);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            // A profile was created between the check and the delete; the database restriction is the final word.
            return CandidateHasProfiles(id, null);
        }

        return Results.NoContent();
    }

    private static IResult CandidateHasProfiles(Guid id, int? profileCount) =>
        ApiResults.Conflict(
            ApiResults.CandidateHasProfilesCode,
            "Candidate has search profiles",
            $"Candidate {id} still has search profiles. Delete the profiles first.",
            profileCount is null ? null : new Dictionary<string, object?> { ["searchProfileCount"] = profileCount });
}
