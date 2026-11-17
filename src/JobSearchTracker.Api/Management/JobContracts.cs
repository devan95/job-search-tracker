using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

// Request and response shapes of the canonical job API. Wire names are camelCase. Request properties are nullable so that
// a missing field becomes a field error (400), not a binding failure.

/// <summary>
/// Create and PUT use the same body; PUT replaces every field, so an omitted optional field is cleared. The employer is a
/// typed name that is resolved (find-or-create) like POST /employers/resolve. Status is never part of this body.
/// <c>confirmDifferentJob</c> overrides a "possible duplicate" warning only (same URL, or same employer + title + location);
/// it never overrides a requisition id match.
/// </summary>
public sealed record JobRequest(
    string? Employer,
    string? Title,
    string? ApplyUrl,
    string? Location,
    string? WorkMode,
    string? EmploymentType,
    string? Department,
    int? ExperienceMinYears,
    int? ExperienceMaxYears,
    string? RequisitionId,
    DateOnly? PostedDate,
    string? Description,
    string? FoundOn,
    bool? ConfirmDifferentJob);

public sealed record JobStatusRequest(string? Status);

public sealed record JobEmployerReference(Guid Id, string Name);

public sealed record JobStatusChangeResponse(string? FromStatus, string ToStatus, DateTimeOffset ChangedAtUtc);

public sealed record JobSummaryResponse(
    Guid Id,
    JobEmployerReference Employer,
    string Title,
    string? ApplyUrl,
    string? Location,
    string? WorkMode,
    string? EmploymentType,
    string Status,
    string CreatedVia,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static JobSummaryResponse From(Job j) => new(
        j.Id,
        new JobEmployerReference(j.Employer.Id, j.Employer.Name),
        j.Title,
        j.ApplyUrl,
        j.Location,
        j.WorkMode,
        j.EmploymentType,
        j.Status,
        j.CreatedVia,
        j.CreatedAtUtc,
        j.UpdatedAtUtc);
}

public sealed record JobResponse(
    Guid Id,
    JobEmployerReference Employer,
    string Title,
    string? ApplyUrl,
    string? CanonicalUrl,
    string? Location,
    string? WorkMode,
    string? EmploymentType,
    string? Department,
    int? ExperienceMinYears,
    int? ExperienceMaxYears,
    string? RequisitionId,
    DateOnly? PostedDate,
    string? Description,
    string? FoundOn,
    string Status,
    string CreatedVia,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<JobStatusChangeResponse> StatusHistory)
{
    public static JobResponse From(Job j, IEnumerable<JobStatusHistory> history) => new(
        j.Id,
        new JobEmployerReference(j.Employer.Id, j.Employer.Name),
        j.Title,
        j.ApplyUrl,
        j.CanonicalUrl,
        j.Location,
        j.WorkMode,
        j.EmploymentType,
        j.Department,
        j.ExperienceMinYears,
        j.ExperienceMaxYears,
        j.RequisitionId,
        j.PostedDate,
        j.Description,
        j.FoundOn,
        j.Status,
        j.CreatedVia,
        j.CreatedAtUtc,
        j.UpdatedAtUtc,
        history.OrderBy(h => h.ChangedAtUtc)
            .Select(h => new JobStatusChangeResponse(h.FromStatus, h.ToStatus, h.ChangedAtUtc))
            .ToList());
}
