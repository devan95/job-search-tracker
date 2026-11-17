using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>Body of both import endpoints: one company per line.</summary>
public sealed record CompanyListImportRequest(string? Text);

/// <summary>Exact <c>status</c> values of a preview row.</summary>
public static class ImportRowStatus
{
    public const string New = "new";
    public const string ExistingEmployer = "existing_employer";
    public const string AlreadyInList = "already_in_list";
    public const string DuplicateInInput = "duplicate_in_input";
    public const string Invalid = "invalid";
}

public sealed record ImportPreviewRow(
    int RowNumber,
    string Input,
    string? Name,
    string? NormalizedName,
    string Status,
    Guid? ExistingEmployerId,
    int? DuplicateOfRow,
    string Message);

/// <summary><c>Total</c> is the number of non-blank rows; the five counts add up to it.</summary>
public sealed record ImportPreviewSummary(int Total, int New, int ExistingEmployer, int AlreadyInList, int DuplicateInInput, int Invalid);

public sealed record ImportPreviewResponse(IReadOnlyList<ImportPreviewRow> Rows, ImportPreviewSummary Summary);

/// <summary><c>Created + Reused + Skipped</c> equals the number of non-blank rows; <c>AddedToList = Created + Reused</c>.</summary>
public sealed record ImportResultResponse(int Created, int Reused, int AddedToList, int Skipped);

/// <summary>
/// Company paste workflow: a non-destructive preview and a stateless confirmed save. Both parse the text with
/// <see cref="CompanyListImportParser"/>, so the preview and the save cannot disagree about rows.
/// </summary>
public static class CompanyListImportEndpoints
{
    private const int MaxSaveAttempts = 3;

    public static RouteGroupBuilder MapCompanyListImportEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/import/preview", PreviewAsync);
        group.MapPost("/{id:guid}/import", ImportAsync);
        return group;
    }

    private static async Task<IResult> PreviewAsync(Guid id, CompanyListImportRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!TryParse(request, errors, out var parsed))
        {
            return errors.ToResult();
        }

        if (!await db.CompanyLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Company list", id);
        }

        var candidates = parsed.Rows.Where(r => r.Kind == ParsedRowKind.Candidate).ToList();
        var known = await LoadKnownAsync(db, id, candidates, ct);

        var rows = new List<ImportPreviewRow>(parsed.Rows.Count);
        foreach (var row in parsed.Rows)
        {
            rows.Add(row.Kind switch
            {
                ParsedRowKind.Invalid => new ImportPreviewRow(
                    row.RowNumber, row.Input, null, null, ImportRowStatus.Invalid, null, null, row.Error!),
                ParsedRowKind.DuplicateInInput => new ImportPreviewRow(
                    row.RowNumber, row.Input, row.Name, row.NormalizedName, ImportRowStatus.DuplicateInInput, null, row.DuplicateOfRow,
                    $"Same company as row {row.DuplicateOfRow}; it will be skipped."),
                _ => ClassifyCandidate(row, known),
            });
        }

        var summary = new ImportPreviewSummary(
            rows.Count,
            rows.Count(r => r.Status == ImportRowStatus.New),
            rows.Count(r => r.Status == ImportRowStatus.ExistingEmployer),
            rows.Count(r => r.Status == ImportRowStatus.AlreadyInList),
            rows.Count(r => r.Status == ImportRowStatus.DuplicateInInput),
            rows.Count(r => r.Status == ImportRowStatus.Invalid));
        return Results.Ok(new ImportPreviewResponse(rows, summary));
    }

    private static async Task<IResult> ImportAsync(Guid id, CompanyListImportRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!TryParse(request, errors, out var parsed))
        {
            return errors.ToResult();
        }

        if (!await db.CompanyLists.AnyAsync(l => l.Id == id, ct))
        {
            return ApiResults.NotFound("Company list", id);
        }

        foreach (var row in parsed.Rows.Where(r => r.Kind == ParsedRowKind.Invalid))
        {
            errors.Add("text", $"Row {row.RowNumber}: {row.Error}");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Results.Ok(await SaveOnceAsync(db, id, parsed, ct));
            }
            catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
            {
                return ApiResults.NotFound("Company list not found", "The list was deleted while the import ran.");
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex) && attempt < MaxSaveAttempts)
            {
                // A concurrent import added the same list membership. The transaction was rolled back; running the
                // whole import again is safe because it only adds what is still missing.
                db.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>One transaction: create missing employers (race-safe), add missing memberships, skip the rest.</summary>
    private static async Task<ImportResultResponse> SaveOnceAsync(AppDbContext db, Guid listId, ParseResult parsed, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Fixed (ordinal) order so that two concurrent imports take the same row locks in the same order.
        var candidates = parsed.Rows
            .Where(r => r.Kind == ParsedRowKind.Candidate)
            .OrderBy(r => r.NormalizedName, StringComparer.Ordinal)
            .ToList();
        var known = await LoadKnownAsync(db, listId, candidates, ct);

        int created = 0, reused = 0;
        var skipped = parsed.Rows.Count(r => r.Kind == ParsedRowKind.DuplicateInInput);
        foreach (var row in candidates)
        {
            Guid employerId;
            if (known.TryGetValue(row.NormalizedName!, out var entry))
            {
                if (entry.InList)
                {
                    skipped++;
                    continue;
                }

                employerId = entry.EmployerId;
                reused++;
            }
            else
            {
                var (employer, wasCreated) = await EmployerResolver.FindOrCreateAsync(db, row.Name!, row.NormalizedName!, ct);
                employerId = employer.Id;
                if (wasCreated)
                {
                    created++;
                }
                else
                {
                    reused++;
                }
            }

            db.CompanyListEmployers.Add(new CompanyListEmployer { CompanyListId = listId, EmployerId = employerId });
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new ImportResultResponse(created, reused, created + reused, skipped);
    }

    private static ImportPreviewRow ClassifyCandidate(ParsedRow row, Dictionary<string, KnownEmployer> known)
    {
        if (!known.TryGetValue(row.NormalizedName!, out var entry))
        {
            return new ImportPreviewRow(
                row.RowNumber, row.Input, row.Name, row.NormalizedName, ImportRowStatus.New, null, null,
                "New company; it will be created and added to the list.");
        }

        return entry.InList
            ? new ImportPreviewRow(
                row.RowNumber, row.Input, row.Name, row.NormalizedName, ImportRowStatus.AlreadyInList, entry.EmployerId, null,
                "Already in this list; it will be skipped.")
            : new ImportPreviewRow(
                row.RowNumber, row.Input, row.Name, row.NormalizedName, ImportRowStatus.ExistingEmployer, entry.EmployerId, null,
                "The company already exists; it will be reused and added to the list.");
    }

    /// <summary>Existing canonical employers for the candidate rows, and whether each is already in the list.</summary>
    private static async Task<Dictionary<string, KnownEmployer>> LoadKnownAsync(
        AppDbContext db, Guid listId, IReadOnlyList<ParsedRow> candidates, CancellationToken ct)
    {
        var names = candidates.Select(r => r.NormalizedName!).ToList();
        if (names.Count == 0)
        {
            return new Dictionary<string, KnownEmployer>(StringComparer.Ordinal);
        }

        var employers = await db.Employers.AsNoTracking()
            .Where(e => names.Contains(e.NormalizedName))
            .Select(e => new { e.Id, e.NormalizedName })
            .ToListAsync(ct);
        var employerIds = employers.Select(e => e.Id).ToList();
        var inList = (await db.CompanyListEmployers.AsNoTracking()
                .Where(x => x.CompanyListId == listId && employerIds.Contains(x.EmployerId))
                .Select(x => x.EmployerId)
                .ToListAsync(ct))
            .ToHashSet();

        return employers.ToDictionary(e => e.NormalizedName, e => new KnownEmployer(e.Id, inList.Contains(e.Id)), StringComparer.Ordinal);
    }

    private static bool TryParse(CompanyListImportRequest? request, ValidationErrors errors, out ParseResult parsed)
    {
        parsed = new ParseResult([], false);
        if (request?.Text is null)
        {
            errors.Add("text", "A text field is required: one company per line.");
            return false;
        }

        parsed = CompanyListImportParser.Parse(request.Text);
        if (parsed.TooManyRows)
        {
            errors.Add("text", $"At most {CompanyListImportParser.MaxRows} non-blank rows are accepted per request.");
            return false;
        }

        if (parsed.Rows.Count == 0)
        {
            errors.Add("text", "Paste at least one company name (one per line).");
            return false;
        }

        return true;
    }

    private sealed record KnownEmployer(Guid EmployerId, bool InList);
}
