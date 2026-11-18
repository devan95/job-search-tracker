using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

// Request and response shapes of the manual application API. Wire names are camelCase. Request properties are nullable so
// that a missing field becomes a field error (400), not a binding failure.

/// <summary>Creates the one application of a candidate for a canonical job. It starts as saved and is not applied.</summary>
public sealed record CreateApplicationRequest(Guid? CandidateId, Guid? JobId);

/// <summary>An ordinary status change. <c>reason</c> is optional here (it is required for un-apply and corrections).</summary>
public sealed record ApplicationStatusRequest(string? Status, string? Reason);

/// <summary>
/// Explicit manual confirmation that the application was submitted. <c>confirmed</c> must be true. <c>appliedAtUtc</c> is
/// when the user applied: omitted means now, an earlier time is allowed, a future time is not.
/// </summary>
public sealed record ConfirmAppliedRequest(bool? Confirmed, DateTimeOffset? AppliedAtUtc);

public sealed record UnapplyRequest(string? Reason);

/// <summary>
/// Audited correction of the status and/or the date and time applied; <c>reason</c> is required and at least one value must
/// change. <c>appliedAtUtc</c> can only be corrected while the application is applied.
/// </summary>
public sealed record CorrectApplicationRequest(string? Status, DateTimeOffset? AppliedAtUtc, string? Reason);

public sealed record ApplicationJobReference(Guid Id, string Title, JobEmployerReference Employer, string Status);

public sealed record ApplicationAuditResponse(
    string Action,
    string Actor,
    DateTimeOffset ChangedAtUtc,
    string? OldStatus,
    bool? OldIsApplied,
    DateTimeOffset? OldAppliedAtUtc,
    string NewStatus,
    bool NewIsApplied,
    DateTimeOffset? NewAppliedAtUtc,
    string? Reason)
{
    public static ApplicationAuditResponse From(ApplicationAuditEntry e) => new(
        e.Action, e.Actor, e.ChangedAtUtc, e.OldStatus, e.OldIsApplied, e.OldAppliedAtUtc, e.NewStatus, e.NewIsApplied, e.NewAppliedAtUtc, e.Reason);
}

public sealed record ApplicationSummaryResponse(
    Guid Id,
    Guid CandidateId,
    ApplicationJobReference Job,
    string Status,
    bool IsApplied,
    string? Method,
    DateTimeOffset? AppliedAtUtc,
    DateTimeOffset? AppliedConfirmedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static ApplicationSummaryResponse From(Application a) => new(
        a.Id,
        a.CandidateId,
        new ApplicationJobReference(a.Job.Id, a.Job.Title, new JobEmployerReference(a.Job.Employer.Id, a.Job.Employer.Name), a.Job.Status),
        a.Status,
        a.IsApplied,
        a.Method,
        a.AppliedAtUtc,
        a.AppliedConfirmedAtUtc,
        a.CreatedAtUtc,
        a.UpdatedAtUtc);
}

/// <summary>The application with its full audit trail, oldest first.</summary>
public sealed record ApplicationResponse(
    Guid Id,
    Guid CandidateId,
    ApplicationJobReference Job,
    string Status,
    bool IsApplied,
    string? Method,
    DateTimeOffset? AppliedAtUtc,
    DateTimeOffset? AppliedConfirmedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<ApplicationAuditResponse> AuditTrail)
{
    public static ApplicationResponse From(Application a, IEnumerable<ApplicationAuditEntry> audit)
    {
        var summary = ApplicationSummaryResponse.From(a);
        return new ApplicationResponse(
            summary.Id, summary.CandidateId, summary.Job, summary.Status, summary.IsApplied, summary.Method, summary.AppliedAtUtc,
            summary.AppliedConfirmedAtUtc, summary.CreatedAtUtc, summary.UpdatedAtUtc,
            audit.OrderBy(e => e.ChangedAtUtc).Select(ApplicationAuditResponse.From).ToList());
    }
}
