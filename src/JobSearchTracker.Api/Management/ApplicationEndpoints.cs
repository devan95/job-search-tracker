using System.Data;
using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Manual application tracking (OQ-P1.8, OQ-P1.9). One application per candidate and canonical job. Applied exists only
/// after an explicit confirmation (<c>confirmed: true</c>); saving or preparing a job never sets it. Later statuses keep the
/// applied fact. Every change appends an audit row in the same transaction; un-apply and corrections need a reason.
/// </summary>
public static class ApplicationEndpoints
{
    internal const string ApplicationExistsCode = "application_exists";
    internal const string InvalidTransitionCode = "invalid_application_transition";
    internal const string AlreadyAppliedCode = "already_applied";
    internal const string NotAppliedCode = "not_applied";
    internal const string InvalidCorrectionCode = "invalid_correction";
    internal const string CandidateHasApplicationsCode = "candidate_has_applications";
    internal const string ConcurrentCode = "concurrent_modification";

    private const string PairIndex = "IX_applications_CandidateId_JobId";

    public static RouteGroupBuilder MapApplicationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/applications").WithTags("Applications");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/status", ChangeStatusAsync);
        group.MapPost("/{id:guid}/confirm-applied", ConfirmAppliedAsync);
        group.MapPost("/{id:guid}/unapply", UnapplyAsync);
        group.MapPost("/{id:guid}/correct", CorrectAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(CreateApplicationRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (request?.CandidateId is null || request.CandidateId == Guid.Empty)
        {
            errors.Add("candidateId", "A candidate id is required.");
        }

        if (request?.JobId is null || request.JobId == Guid.Empty)
        {
            errors.Add("jobId", "A job id is required.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        var candidateId = request!.CandidateId!.Value;
        var jobId = request.JobId!.Value;
        if (!await db.Candidates.AnyAsync(c => c.Id == candidateId, ct))
        {
            errors.Add("candidateId", "No candidate with this id exists.");
        }

        if (!await db.Jobs.AnyAsync(j => j.Id == jobId, ct))
        {
            errors.Add("jobId", "No job with this id exists.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        var existing = await FindExistingAsync(db, candidateId, jobId, ct);
        if (existing is not null)
        {
            return Exists(existing);
        }

        var application = new Application(candidateId, jobId, DateTimeOffset.UtcNow);
        db.Applications.Add(application);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex) && DbErrors.ConstraintName(ex) == PairIndex)
        {
            db.ChangeTracker.Clear();
            var raced = await FindExistingAsync(db, candidateId, jobId, ct);
            return raced is null ? throw ex : Exists(raced);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            db.ChangeTracker.Clear();
            errors.Add("jobId", "The candidate or job was deleted while this request ran.");
            return errors.ToResult();
        }

        var response = await LoadAsync(db, application.Id, ct);
        return Results.Created($"/api/applications/{application.Id}", response);
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db, Guid? candidateId, Guid? jobId, string? status, bool? applied, int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue);
        var statusFilter = JobInput.Choice(status, ApplicationStatuses.All, "status", "status", errors);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        IQueryable<Application> query = db.Applications.AsNoTracking().Include(a => a.Job).ThenInclude(j => j.Employer);
        if (candidateId is not null)
        {
            query = query.Where(a => a.CandidateId == candidateId);
        }

        if (jobId is not null)
        {
            query = query.Where(a => a.JobId == jobId);
        }

        if (statusFilter is not null)
        {
            query = query.Where(a => a.Status == statusFilter);
        }

        if (applied is not null)
        {
            query = query.Where(a => a.IsApplied == applied);
        }

        var items = await query
            .OrderByDescending(a => a.UpdatedAtUtc).ThenBy(a => a.Id)
            .Skip(skipValue).Take(takeValue)
            .ToListAsync(ct);
        return Results.Ok(items.Select(ApplicationSummaryResponse.From).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Application", id) : Results.Ok(response);
    }

    private static async Task<IResult> ChangeStatusAsync(Guid id, ApplicationStatusRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var status = JobInput.Choice(request?.Status, ApplicationStatuses.UserSettable, "status", "status", errors);
        if (status is null && !errors.Any)
        {
            errors.Add("status", "A status is required.");
        }

        var reason = Reason(request?.Reason, required: false, errors);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        return await MutateAsync(db, id, ct, application =>
        {
            if (!ApplicationRules.IsAllowedTransition(application.Status, status!))
            {
                return ApiResults.Conflict(
                    InvalidTransitionCode,
                    "Status change not allowed",
                    $"An application that is {application.Status} cannot be set to {status}. Rejected and withdrawn applications are reopened with a correction.",
                    new Dictionary<string, object?> { ["currentStatus"] = application.Status, ["requestedStatus"] = status });
            }

            return application.ChangeStatus(status!, reason, DateTimeOffset.UtcNow);
        });
    }

    private static async Task<IResult> ConfirmAppliedAsync(Guid id, ConfirmAppliedRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (request?.Confirmed != true)
        {
            errors.Add("confirmed", "Applied needs an explicit confirmation that the application was submitted: send confirmed: true.");
        }

        // Validation uses the clock at request time. The timestamps that are stored are read inside the change callback,
        // after the row is loaded, so the audit order follows the order in which changes are really applied.
        var requestedAt = request?.AppliedAtUtc?.ToUniversalTime();
        if (requestedAt > DateTimeOffset.UtcNow)
        {
            errors.Add("appliedAtUtc", "The date and time applied cannot be in the future.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        return await MutateAsync(db, id, ct, application =>
        {
            if (application.IsApplied)
            {
                return ApiResults.Conflict(
                    AlreadyAppliedCode,
                    "Already applied",
                    $"Application {id} is already confirmed as applied. Use a correction to change the date.",
                    new Dictionary<string, object?> { ["appliedAtUtc"] = application.AppliedAtUtc });
            }

            var now = DateTimeOffset.UtcNow;
            return application.ConfirmApplied(requestedAt ?? now, now);
        });
    }

    private static async Task<IResult> UnapplyAsync(Guid id, UnapplyRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var reason = Reason(request?.Reason, required: true, errors);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        return await MutateAsync(db, id, ct, application =>
            application.IsApplied
                ? (object)application.Unapply(reason!, DateTimeOffset.UtcNow)
                : NotApplied(id));
    }

    private static async Task<IResult> CorrectAsync(Guid id, CorrectApplicationRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var status = JobInput.Choice(request?.Status, ApplicationStatuses.All, "status", "status", errors);
        var reason = Reason(request?.Reason, required: true, errors);
        var appliedAt = request?.AppliedAtUtc?.ToUniversalTime();
        if (appliedAt > DateTimeOffset.UtcNow)
        {
            errors.Add("appliedAtUtc", "The date and time applied cannot be in the future.");
        }

        if (status is null && appliedAt is null && !errors.Any)
        {
            errors.Add("status", "Send a status, an appliedAtUtc or both: a correction must change something.");
        }

        if (errors.Any)
        {
            return errors.ToResult();
        }

        return await MutateAsync(db, id, ct, application =>
        {
            var newStatus = status ?? application.Status;
            var newAppliedAt = appliedAt ?? application.AppliedAtUtc;
            if (appliedAt is not null && !application.IsApplied)
            {
                return InvalidCorrection(id, "The date applied can only be corrected while the application is confirmed as applied.");
            }

            if (!ApplicationRules.IsConsistent(newStatus, application.IsApplied))
            {
                return InvalidCorrection(
                    id,
                    application.IsApplied
                        ? $"An application that is confirmed as applied cannot be {newStatus}. Un-apply it first."
                        : $"{newStatus} requires a confirmed application. Confirm applied first.");
            }

            if (newStatus == application.Status && newAppliedAt == application.AppliedAtUtc)
            {
                return InvalidCorrection(id, "The correction does not change anything.");
            }

            return application.Correct(newStatus, newAppliedAt, reason!, DateTimeOffset.UtcNow);
        });
    }

    /// <summary>
    /// Loads the application, lets <paramref name="change"/> either return a conflict result or apply the change and return
    /// its audit entry, then saves both in one transaction. The row version (xmin) makes concurrent changes lose with 409.
    /// </summary>
    private static async Task<IResult> MutateAsync(
        AppDbContext db, Guid id, CancellationToken ct, Func<Application, object> change)
    {
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
        {
            return ApiResults.NotFound("Application", id);
        }

        var outcome = change(application);
        if (outcome is IResult conflict)
        {
            return conflict;
        }

        db.ApplicationAuditEntries.Add((ApplicationAuditEntry)outcome);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResults.Conflict(
                ConcurrentCode,
                "Application was changed by someone else",
                $"Application {id} was changed while this request was running. Reload it and try again.");
        }

        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Application", id) : Results.Ok(response);
    }

    private static async Task<ApplicationResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var application = await db.Applications.AsNoTracking()
            .Include(a => a.Job).ThenInclude(j => j.Employer)
            .SingleOrDefaultAsync(a => a.Id == id, ct);
        if (application is null)
        {
            return null;
        }

        var audit = await db.ApplicationAuditEntries.AsNoTracking()
            .Where(e => e.ApplicationId == id).OrderBy(e => e.ChangedAtUtc).ToListAsync(ct);
        await transaction.CommitAsync(ct);
        return ApplicationResponse.From(application, audit);
    }

    private static Task<Application?> FindExistingAsync(AppDbContext db, Guid candidateId, Guid jobId, CancellationToken ct) =>
        db.Applications.AsNoTracking().SingleOrDefaultAsync(a => a.CandidateId == candidateId && a.JobId == jobId, ct);

    /// <summary>Trims the reason; blank is unset. Required for un-apply and corrections.</summary>
    private static string? Reason(string? value, bool required, ValidationErrors errors)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            if (required)
            {
                errors.Add("reason", "A short reason is required.");
            }

            return null;
        }

        if (trimmed.Length > ApplicationLimits.Reason)
        {
            errors.Add("reason", $"The reason must be at most {ApplicationLimits.Reason} characters.");
            return null;
        }

        return trimmed;
    }

    private static IResult Exists(Application existing) =>
        ApiResults.Conflict(
            ApplicationExistsCode,
            "Application already exists",
            $"This candidate already has application {existing.Id} for this job (status {existing.Status}). Use it instead of creating a second one.",
            new Dictionary<string, object?>
            {
                ["existingId"] = existing.Id,
                // Not "status": that is the reserved RFC 7807 member holding the numeric HTTP status.
                ["currentStatus"] = existing.Status,
                ["isApplied"] = existing.IsApplied,
                ["appliedAtUtc"] = existing.AppliedAtUtc,
            });

    private static IResult NotApplied(Guid id) =>
        ApiResults.Conflict(NotAppliedCode, "Not applied", $"Application {id} is not confirmed as applied, so there is nothing to un-apply.");

    private static IResult InvalidCorrection(Guid id, string detail) =>
        ApiResults.Conflict(InvalidCorrectionCode, "Correction not allowed", $"Application {id}: {detail}");
}
