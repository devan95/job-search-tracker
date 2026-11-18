namespace JobSearchTracker.Api.Data;

/// <summary>
/// Application statuses (OQ-P1.8). The stored and wire value is the camelCase name. <see cref="Applied"/> as a status is
/// separate from the confirmed "applied" fact (<see cref="Application.IsApplied"/>, method and time): later statuses such as
/// interview, offer, rejected or withdrawn never erase that fact.
/// </summary>
public static class ApplicationStatuses
{
    public const string Saved = "saved";
    public const string Applied = "applied";
    public const string PendingVerification = "pendingVerification";
    public const string Interview = "interview";
    public const string Offer = "offer";
    public const string Rejected = "rejected";
    public const string Withdrawn = "withdrawn";

    public static readonly IReadOnlyList<string> All = [Saved, Applied, PendingVerification, Interview, Offer, Rejected, Withdrawn];

    /// <summary>Statuses that only exist for an application that was confirmed as submitted.</summary>
    public static readonly IReadOnlyList<string> RequireApplied = [Applied, Interview, Offer];

    /// <summary>Statuses that only exist while the application is not confirmed as submitted.</summary>
    public static readonly IReadOnlyList<string> RequireNotApplied = [Saved, PendingVerification];

    /// <summary>The statuses a user may request through the status endpoint. Saved and applied are never requested directly.</summary>
    public static readonly IReadOnlyList<string> UserSettable = [PendingVerification, Interview, Offer, Rejected, Withdrawn];
}

/// <summary>How an application was submitted. Phase 1 knows only manual submission.</summary>
public static class ApplicationMethods
{
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> All = [Manual];
}

public static class ApplicationAuditActions
{
    public const string Created = "created";
    public const string StatusChanged = "statusChanged";
    public const string AppliedConfirmed = "appliedConfirmed";
    public const string Unapplied = "unapplied";
    public const string Corrected = "corrected";

    public static readonly IReadOnlyList<string> All = [Created, StatusChanged, AppliedConfirmed, Unapplied, Corrected];

    /// <summary>Actions that must carry a reason.</summary>
    public static readonly IReadOnlyList<string> RequireReason = [Unapplied, Corrected];
}

/// <summary>Who made a change. Phase 1 has no authentication, so every change is made by the local user.</summary>
public static class ApplicationActors
{
    public const string User = "user";

    public static readonly IReadOnlyList<string> All = [User];
}

public static class ApplicationLimits
{
    public const int Reason = 500;
}

/// <summary>The part of an application that the audit trail records before and after a change.</summary>
public readonly record struct ApplicationState(string Status, bool IsApplied, DateTimeOffset? AppliedAtUtc);

/// <summary>Transition rules (OQ-P1.8). Kept free of the database so they can be tested directly.</summary>
public static class ApplicationRules
{
    /// <summary>
    /// Ordinary status changes. Applied is set only by confirming a manual submission, saved only by un-applying or a
    /// correction. Any status may move to withdrawn. Rejected and withdrawn are left only by an audited correction.
    /// </summary>
    public static bool IsAllowedTransition(string from, string to) => to switch
    {
        ApplicationStatuses.Withdrawn => from != ApplicationStatuses.Withdrawn,
        ApplicationStatuses.PendingVerification => from == ApplicationStatuses.Saved,
        ApplicationStatuses.Interview => from == ApplicationStatuses.Applied,
        ApplicationStatuses.Offer => from is ApplicationStatuses.Applied or ApplicationStatuses.Interview,
        ApplicationStatuses.Rejected => from is ApplicationStatuses.PendingVerification or ApplicationStatuses.Applied
            or ApplicationStatuses.Interview or ApplicationStatuses.Offer,
        _ => false,
    };

    /// <summary>Whether a status can be held together with the given confirmed-applied fact.</summary>
    public static bool IsConsistent(string status, bool isApplied) =>
        !(ApplicationStatuses.RequireApplied.Contains(status) && !isApplied)
        && !(ApplicationStatuses.RequireNotApplied.Contains(status) && isApplied);
}

/// <summary>
/// One application per candidate and canonical job (never per source listing). Saving or preparing a job creates a
/// <see cref="ApplicationStatuses.Saved"/> application that is not applied; it becomes applied only through
/// <see cref="ConfirmApplied"/>, which the caller invokes after an explicit manual confirmation. Every change returns an
/// append-only audit entry for the caller to add to the context.
/// </summary>
public class Application
{
    private Application() { }

    public Application(Guid candidateId, Guid jobId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CandidateId = candidateId;
        JobId = jobId;
        Status = ApplicationStatuses.Saved;
        IsApplied = false;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        AuditEntries.Add(new ApplicationAuditEntry(Id, ApplicationAuditActions.Created, null, State, null, now));
    }

    public Guid Id { get; private set; }
    public Guid CandidateId { get; private set; }
    public Candidate Candidate { get; set; } = null!;
    public Guid JobId { get; private set; }
    public Job Job { get; set; } = null!;

    public string Status { get; private set; } = null!;

    /// <summary>True only after the user confirmed that the application was submitted. Later statuses never clear it.</summary>
    public bool IsApplied { get; private set; }

    /// <summary>How it was submitted; set exactly when <see cref="IsApplied"/> is true. Always manual in Phase 1.</summary>
    public string? Method { get; private set; }

    /// <summary>The date and time the user says they applied (back-dating allowed, never in the future).</summary>
    public DateTimeOffset? AppliedAtUtc { get; private set; }

    /// <summary>When the user confirmed it in this tool. Stored separately from <see cref="AppliedAtUtc"/>.</summary>
    public DateTimeOffset? AppliedConfirmedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public List<ApplicationAuditEntry> AuditEntries { get; } = [];

    public ApplicationState State => new(Status, IsApplied, AppliedAtUtc);

    /// <summary>Ordinary status change. The caller has already checked <see cref="ApplicationRules.IsAllowedTransition"/>.</summary>
    public ApplicationAuditEntry ChangeStatus(string newStatus, string? reason, DateTimeOffset now)
    {
        var before = State;
        Status = newStatus;
        UpdatedAtUtc = now;
        return new ApplicationAuditEntry(Id, ApplicationAuditActions.StatusChanged, before, State, reason, now);
    }

    /// <summary>
    /// Records the confirmed manual submission. A saved or pending-verification application becomes applied; a rejected or
    /// withdrawn one keeps its status and only gains the applied fact. The caller has checked that it is not applied yet.
    /// </summary>
    public ApplicationAuditEntry ConfirmApplied(DateTimeOffset appliedAtUtc, DateTimeOffset now)
    {
        var before = State;
        IsApplied = true;
        Method = ApplicationMethods.Manual;
        AppliedAtUtc = appliedAtUtc;
        AppliedConfirmedAtUtc = now;
        if (Status is ApplicationStatuses.Saved or ApplicationStatuses.PendingVerification)
        {
            Status = ApplicationStatuses.Applied;
        }

        UpdatedAtUtc = now;
        return new ApplicationAuditEntry(Id, ApplicationAuditActions.AppliedConfirmed, before, State, null, now);
    }

    /// <summary>
    /// Removes the applied fact. Applied, interview and offer go back to saved; rejected and withdrawn keep their status.
    /// The old values stay in the audit entry. The caller has checked that the application is applied.
    /// </summary>
    public ApplicationAuditEntry Unapply(string reason, DateTimeOffset now)
    {
        var before = State;
        IsApplied = false;
        Method = null;
        AppliedAtUtc = null;
        AppliedConfirmedAtUtc = null;
        if (ApplicationStatuses.RequireApplied.Contains(Status))
        {
            Status = ApplicationStatuses.Saved;
        }

        UpdatedAtUtc = now;
        return new ApplicationAuditEntry(Id, ApplicationAuditActions.Unapplied, before, State, reason, now);
    }

    /// <summary>Audited correction of the status and/or the date and time applied. The caller has checked consistency.</summary>
    public ApplicationAuditEntry Correct(string newStatus, DateTimeOffset? newAppliedAtUtc, string reason, DateTimeOffset now)
    {
        var before = State;
        Status = newStatus;
        AppliedAtUtc = newAppliedAtUtc;
        UpdatedAtUtc = now;
        return new ApplicationAuditEntry(Id, ApplicationAuditActions.Corrected, before, State, reason, now);
    }
}

/// <summary>
/// One append-only audit row: who, when, old value, new value and reason (OQ-P1.9). The database rejects updates and deletes.
/// The first row of an application has no old values.
/// </summary>
public class ApplicationAuditEntry
{
    private ApplicationAuditEntry() { }

    public ApplicationAuditEntry(
        Guid applicationId, string action, ApplicationState? before, ApplicationState after, string? reason, DateTimeOffset changedAtUtc)
    {
        Id = Guid.NewGuid();
        ApplicationId = applicationId;
        Action = action;
        Actor = ApplicationActors.User;
        ChangedAtUtc = changedAtUtc;
        OldStatus = before?.Status;
        OldIsApplied = before?.IsApplied;
        OldAppliedAtUtc = before?.AppliedAtUtc;
        NewStatus = after.Status;
        NewIsApplied = after.IsApplied;
        NewAppliedAtUtc = after.AppliedAtUtc;
        Reason = reason;
    }

    public Guid Id { get; private set; }
    public Guid ApplicationId { get; private set; }
    public Application Application { get; set; } = null!;
    public string Action { get; private set; } = null!;
    public string Actor { get; private set; } = null!;
    public DateTimeOffset ChangedAtUtc { get; private set; }
    public string? OldStatus { get; private set; }
    public bool? OldIsApplied { get; private set; }
    public DateTimeOffset? OldAppliedAtUtc { get; private set; }
    public string NewStatus { get; private set; } = null!;
    public bool NewIsApplied { get; private set; }
    public DateTimeOffset? NewAppliedAtUtc { get; private set; }
    public string? Reason { get; private set; }
}
