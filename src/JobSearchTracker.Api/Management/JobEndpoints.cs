using System.Data;
using JobSearchTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Canonical jobs, entered manually (Phase 1). Jobs are global. The employer is a typed name resolved like
/// <c>POST /employers/resolve</c>. Duplicate rules (OQ-P1.7): same employer + same requisition id is blocked with no
/// override; same canonical URL, or same employer + normalized title + location, is a 409 warning that the client may
/// override with <c>confirmDifferentJob</c>; different requisition ids never match. Status changes are timestamped history.
/// </summary>
public static class JobEndpoints
{
    private const string RequisitionIndex = "IX_jobs_EmployerId_NormalizedRequisitionId";
    private const string EmployerNameIndex = "IX_employers_NormalizedName";

    [Flags]
    private enum JobChecks
    {
        None = 0,
        Requisition = 1,
        Url = 2,
        TitleLocation = 4,
        All = Requisition | Url | TitleLocation,
    }

    private sealed record JobDuplicate(Guid ExistingId, string MatchedOn, bool IsHard);

    public static RouteGroupBuilder MapJobEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/jobs").WithTags("Jobs");

        group.MapPost("/", CreateAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/status", ChangeStatusAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);

        return api;
    }

    private static async Task<IResult> CreateAsync(JobRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!JobInput.TryParse(request, errors, out var input))
        {
            return errors.ToResult();
        }

        for (var attempt = 0; ; attempt++)
        {
            var employer = await db.Employers.SingleOrDefaultAsync(e => e.NormalizedName == input.EmployerNormalized, ct);
            var duplicate = await FindDuplicateAsync(db, employer?.Id, input.Details, Guid.Empty, JobChecks.All, ct);
            if (duplicate is not null && (duplicate.IsHard || !input.ConfirmDifferentJob))
            {
                return Duplicate(duplicate);
            }

            // The employer is created in the same save as the job, so a rejected job never leaves a new employer behind.
            var job = new Job(employer ?? new Employer(input.EmployerName), input.Details, DateTimeOffset.UtcNow);
            db.Jobs.Add(job);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                var constraint = DbErrors.ConstraintName(ex);
                db.ChangeTracker.Clear();
                if (constraint == RequisitionIndex)
                {
                    return await RequisitionConflictAsync(db, job.EmployerId, input.Details, Guid.Empty, ct);
                }

                if (constraint == EmployerNameIndex && attempt == 0)
                {
                    continue; // Lost a race creating the same employer: look it up again and retry once.
                }

                throw;
            }

            return Results.Created($"/api/jobs/{job.Id}", JobResponse.From(job, job.StatusHistory));
        }
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db, string? q, Guid? employerId, string? status, string? workMode, string? employmentType,
        int? skip, int? take, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        Input.TryPaging(skip, take, errors, out var skipValue, out var takeValue);

        string? needle = null;
        if (!string.IsNullOrWhiteSpace(q) && Input.TryUniqueName(q, "q", errors, out _, out var normalizedQuery))
        {
            needle = normalizedQuery;
        }

        var statusFilter = JobInput.Choice(status, JobStatuses.All, "status", "status", errors);
        var workModeFilter = JobInput.Choice(workMode, JobWorkModes.All, "workMode", "work mode", errors);
        var employmentTypeFilter = JobInput.Choice(employmentType, JobEmploymentTypes.All, "employmentType", "employment type", errors);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        IQueryable<Job> query = db.Jobs.AsNoTracking().Include(j => j.Employer);
        if (needle is not null)
        {
            query = query.Where(j => j.NormalizedTitle.Contains(needle) || j.Employer.NormalizedName.Contains(needle));
        }

        if (employerId is not null)
        {
            query = query.Where(j => j.EmployerId == employerId);
        }

        if (statusFilter is not null)
        {
            query = query.Where(j => j.Status == statusFilter);
        }

        if (workModeFilter is not null)
        {
            query = query.Where(j => j.WorkMode == workModeFilter);
        }

        if (employmentTypeFilter is not null)
        {
            query = query.Where(j => j.EmploymentType == employmentTypeFilter);
        }

        var jobs = await query
            .OrderByDescending(j => j.CreatedAtUtc).ThenBy(j => j.Id)
            .Skip(skipValue).Take(takeValue)
            .ToListAsync(ct);
        return Results.Ok(jobs.Select(JobSummaryResponse.From).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Job", id) : Results.Ok(response);
    }

    private static async Task<IResult> UpdateAsync(Guid id, JobRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        if (!JobInput.TryParse(request, errors, out var input))
        {
            return errors.ToResult();
        }

        var details = input.Details;
        for (var attempt = 0; ; attempt++)
        {
            var job = await db.Jobs.Include(j => j.Employer).SingleOrDefaultAsync(j => j.Id == id, ct);
            if (job is null)
            {
                return ApiResults.NotFound("Job", id);
            }

            var employer = await db.Employers.SingleOrDefaultAsync(e => e.NormalizedName == input.EmployerNormalized, ct);
            var employerChanged = employer is null || employer.Id != job.EmployerId;

            // Only re-check what this update changes, so editing a job that was once confirmed as "a different job"
            // is not blocked again by the job it was confirmed against.
            var checks = JobChecks.None;
            var requisitionChanged = job.NormalizedRequisitionId != details.NormalizedRequisitionId;
            if (employerChanged || requisitionChanged)
            {
                checks |= JobChecks.Requisition;
            }

            // A changed requisition id (including clearing it) changes which jobs the URL and title + location rules can
            // match, so those two warnings are re-checked too; otherwise clearing the id would silently skip them.
            if (job.CanonicalUrl != details.CanonicalUrl || requisitionChanged)
            {
                checks |= JobChecks.Url;
            }

            if (employerChanged || requisitionChanged || job.NormalizedTitle != details.NormalizedTitle || job.NormalizedLocation != details.NormalizedLocation)
            {
                checks |= JobChecks.TitleLocation;
            }

            if (checks != JobChecks.None)
            {
                var duplicate = await FindDuplicateAsync(db, employer?.Id, details, job.Id, checks, ct);
                if (duplicate is not null && (duplicate.IsHard || !input.ConfirmDifferentJob))
                {
                    return Duplicate(duplicate);
                }
            }

            if (employerChanged)
            {
                var target = employer ?? new Employer(input.EmployerName);
                if (employer is null)
                {
                    db.Employers.Add(target);
                }

                job.SetEmployer(target);
            }

            job.Apply(details, DateTimeOffset.UtcNow);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ConcurrentChange(id);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
            {
                var constraint = DbErrors.ConstraintName(ex);
                var employerId = job.EmployerId;
                db.ChangeTracker.Clear();
                if (constraint == RequisitionIndex)
                {
                    return await RequisitionConflictAsync(db, employerId, details, id, ct);
                }

                if (constraint == EmployerNameIndex && attempt == 0)
                {
                    continue;
                }

                throw;
            }

            var response = await LoadAsync(db, id, ct);
            return response is null ? ApiResults.NotFound("Job", id) : Results.Ok(response);
        }
    }

    private static async Task<IResult> ChangeStatusAsync(Guid id, JobStatusRequest? request, AppDbContext db, CancellationToken ct)
    {
        var errors = new ValidationErrors();
        var status = JobInput.TryStatus(request?.Status, errors);
        if (errors.Any)
        {
            return errors.ToResult();
        }

        var job = await db.Jobs.Include(j => j.Employer).SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
        {
            return ApiResults.NotFound("Job", id);
        }

        if (!IsAllowedTransition(job.Status, status!))
        {
            return ApiResults.Conflict(
                ApiResults.InvalidStatusTransitionCode,
                "Status change not allowed",
                $"A job that is {job.Status} cannot be set to {status}.",
                new Dictionary<string, object?> { ["currentStatus"] = job.Status, ["requestedStatus"] = status });
        }

        // The job row carries a version (xmin), so of two concurrent changes made from the same status only one is saved; the
        // other gets 409. The status update and its history row are saved in one transaction.
        var entry = job.ChangeStatus(status!, DateTimeOffset.UtcNow);
        db.JobStatusHistories.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange(id);
        }

        var response = await LoadAsync(db, id, ct);
        return response is null ? ApiResults.NotFound("Job", id) : Results.Ok(response);
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var job = await db.Jobs.SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
        {
            return ApiResults.NotFound("Job", id);
        }

        db.Jobs.Remove(job);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConcurrentChange(id);
        }
        catch (DbUpdateException ex) when (DbErrors.IsForeignKeyViolation(ex))
        {
            // Tables that reference jobs (applications, source listings) use RESTRICT, so a job they point at cannot go.
            return ApiResults.Conflict(
                ApiResults.JobInUseCode,
                "Job is in use",
                $"Job {id} has an application or a source listing and cannot be deleted. Set it to closed or expired instead.");
        }

        return Results.NoContent();
    }

    /// <summary>
    /// A user may set closed, expired or unavailable on an open or reopened job (or switch between those three), and may
    /// open a closed, expired or unavailable job again, which sets reopened. New and verification-pending jobs are
    /// system-managed and cannot be changed by the user.
    /// </summary>
    private static bool IsAllowedTransition(string from, string to) => to switch
    {
        JobStatuses.Reopened => from is JobStatuses.Closed or JobStatuses.Expired or JobStatuses.Unavailable,
        JobStatuses.Closed or JobStatuses.Expired or JobStatuses.Unavailable =>
            from != to && from is JobStatuses.Open or JobStatuses.Reopened or JobStatuses.Closed or JobStatuses.Expired or JobStatuses.Unavailable,
        _ => false,
    };

    /// <summary>Reads the job and its history in one repeatable-read transaction, so both always come from the same snapshot.</summary>
    private static async Task<JobResponse?> LoadAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var job = await db.Jobs.AsNoTracking().Include(j => j.Employer).SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
        {
            return null;
        }

        var history = await HistoryAsync(db, id, ct);
        await transaction.CommitAsync(ct);
        return JobResponse.From(job, history);
    }

    private static IResult ConcurrentChange(Guid id) =>
        ApiResults.Conflict(
            ApiResults.ConcurrentModificationCode,
            "Job was changed by someone else",
            $"Job {id} was changed or deleted while this request was running. Reload it and try again.");

    private static async Task<List<JobStatusHistory>> HistoryAsync(AppDbContext db, Guid jobId, CancellationToken ct) =>
        await db.JobStatusHistories.AsNoTracking().Where(h => h.JobId == jobId).OrderBy(h => h.ChangedAtUtc).ToListAsync(ct);

    private static async Task<JobDuplicate?> FindDuplicateAsync(
        AppDbContext db, Guid? employerId, JobDetails details, Guid excludeJobId, JobChecks checks, CancellationToken ct)
    {
        var requisition = details.NormalizedRequisitionId;

        if (checks.HasFlag(JobChecks.Requisition) && employerId is { } requisitionEmployer && requisition is not null)
        {
            var existing = await db.Jobs.AsNoTracking()
                .Where(j => j.EmployerId == requisitionEmployer && j.NormalizedRequisitionId == requisition && j.Id != excludeJobId)
                .Select(j => (Guid?)j.Id)
                .FirstOrDefaultAsync(ct);
            if (existing is { } hardId)
            {
                return new JobDuplicate(hardId, "requisitionId", IsHard: true);
            }
        }

        if (checks.HasFlag(JobChecks.Url) && details.CanonicalUrl is { } canonicalUrl)
        {
            var query = db.Jobs.AsNoTracking().Where(j => j.CanonicalUrl == canonicalUrl && j.Id != excludeJobId);
            if (requisition is not null)
            {
                // Different requisition ids never match.
                query = query.Where(j => j.NormalizedRequisitionId == null || j.NormalizedRequisitionId == requisition);
            }

            var existing = await query.OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(ct);
            if (existing is { } urlId)
            {
                return new JobDuplicate(urlId, "canonicalUrl", IsHard: false);
            }
        }

        if (checks.HasFlag(JobChecks.TitleLocation) && employerId is { } employer)
        {
            var title = details.NormalizedTitle;
            var location = details.NormalizedLocation;
            var query = db.Jobs.AsNoTracking()
                .Where(j => j.EmployerId == employer && j.NormalizedTitle == title && j.NormalizedLocation == location && j.Id != excludeJobId);
            if (requisition is not null)
            {
                query = query.Where(j => j.NormalizedRequisitionId == null || j.NormalizedRequisitionId == requisition);
            }

            var existing = await query.OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id).Select(j => (Guid?)j.Id).FirstOrDefaultAsync(ct);
            if (existing is { } titleId)
            {
                return new JobDuplicate(titleId, "employerTitleLocation", IsHard: false);
            }
        }

        return null;
    }

    private static async Task<IResult> RequisitionConflictAsync(
        AppDbContext db, Guid employerId, JobDetails details, Guid excludeJobId, CancellationToken ct)
    {
        var existing = await db.Jobs.AsNoTracking()
            .Where(j => j.EmployerId == employerId && j.NormalizedRequisitionId == details.NormalizedRequisitionId && j.Id != excludeJobId)
            .Select(j => j.Id)
            .FirstAsync(ct);
        return Duplicate(new JobDuplicate(existing, "requisitionId", IsHard: true));
    }

    private static IResult Duplicate(JobDuplicate duplicate)
    {
        var extra = new Dictionary<string, object?> { ["existingId"] = duplicate.ExistingId, ["matchedOn"] = duplicate.MatchedOn };
        return duplicate.IsHard
            ? ApiResults.Conflict(
                ApiResults.DuplicateRequisitionCode,
                "Requisition id already exists for this employer",
                $"Job {duplicate.ExistingId} has the same employer and requisition id. This cannot be overridden.",
                extra)
            : ApiResults.Conflict(
                ApiResults.PossibleDuplicateCode,
                "Possible duplicate job",
                $"Job {duplicate.ExistingId} looks like the same job (match on {duplicate.MatchedOn}). If it is a different job, " +
                "send the request again with confirmDifferentJob set to true.",
                extra);
    }
}
