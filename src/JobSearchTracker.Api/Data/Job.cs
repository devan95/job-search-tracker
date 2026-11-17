namespace JobSearchTracker.Api.Data;

/// <summary>
/// Job lifecycle statuses. The stored and wire value is the camelCase name. A user can set only
/// <see cref="UserSettable"/> values; <see cref="New"/> and <see cref="VerificationPending"/> are set by the system only
/// (Phase 2 source scans) and are valid in the database from the start so that no later migration has to widen the check.
/// A job status never changes an application's status.
/// </summary>
public static class JobStatuses
{
    public const string New = "new";
    public const string Open = "open";
    public const string Closed = "closed";
    public const string Expired = "expired";
    public const string Reopened = "reopened";
    public const string Unavailable = "unavailable";
    public const string VerificationPending = "verificationPending";

    public static readonly IReadOnlyList<string> All = [New, Open, Closed, Expired, Reopened, Unavailable, VerificationPending];

    /// <summary>The statuses a user may request. "Open again" is <see cref="Reopened"/>.</summary>
    public static readonly IReadOnlyList<string> UserSettable = [Closed, Expired, Unavailable, Reopened];
}

/// <summary>Work arrangement of one job (a single value, unlike the bit mask used for lists and profiles).</summary>
public static class JobWorkModes
{
    public const string Remote = "remote";
    public const string OnSite = "onSite";
    public const string Hybrid = "hybrid";

    public static readonly IReadOnlyList<string> All = [Remote, OnSite, Hybrid];
}

public static class JobEmploymentTypes
{
    public const string FullTime = "fullTime";
    public const string PartTime = "partTime";
    public const string Contract = "contract";
    public const string Internship = "internship";
    public const string Temporary = "temporary";
    public const string Other = "other";

    public static readonly IReadOnlyList<string> All = [FullTime, PartTime, Contract, Internship, Temporary, Other];
}

/// <summary>How a canonical job record came to exist. Phase 2 adds source-created values.</summary>
public static class JobOrigins
{
    public const string Manual = "manual";
}

/// <summary>Length limits shared by the model configuration, the API validation and the tests.</summary>
public static class JobLimits
{
    public const int Title = NameNormalizer.MaxLength;
    public const int Location = NameNormalizer.MaxLength;
    public const int Department = 200;
    public const int FoundOn = 200;
    public const int RequisitionId = 100;
    public const int Url = 2000;
    public const int Description = 50_000;
    public const int MaxExperienceYears = 60;
}

/// <summary>
/// The user-editable fields of a job after validation, with the normalized forms used for duplicate checks.
/// Field names follow Phase 2 S2.1 D1.
/// </summary>
public sealed record JobDetails(
    string Title,
    string NormalizedTitle,
    string? ApplyUrl,
    string? CanonicalUrl,
    string? Location,
    string? NormalizedLocation,
    string? WorkMode,
    string? EmploymentType,
    string? Department,
    int? ExperienceMinYears,
    int? ExperienceMaxYears,
    string? RequisitionId,
    string? NormalizedRequisitionId,
    DateOnly? PostedDate,
    string? Description,
    string? FoundOn);

/// <summary>
/// Canonical job. Jobs are global (not per candidate). Phase 1 creates them manually; Phase 2 source listings will
/// point at the same record. The employer is required and cannot be deleted while it has jobs.
/// </summary>
public class Job
{
    private Job() { }

    public Job(Employer employer, JobDetails details, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Employer = employer;
        EmployerId = employer.Id;
        CreatedVia = JobOrigins.Manual;
        Status = JobStatuses.Open;
        CreatedAtUtc = now;
        Apply(details, now);
        StatusHistory.Add(new JobStatusHistory(Id, null, JobStatuses.Open, now));
    }

    public Guid Id { get; private set; }
    public Guid EmployerId { get; private set; }
    public Employer Employer { get; private set; } = null!;

    public string Title { get; private set; } = null!;
    public string NormalizedTitle { get; private set; } = null!;

    /// <summary>The job or apply URL as the user typed it (trimmed). Always http or https.</summary>
    public string? ApplyUrl { get; private set; }

    /// <summary>Canonical form of <see cref="ApplyUrl"/>, used for duplicate detection. See <see cref="JobUrlCanonicalizer"/>.</summary>
    public string? CanonicalUrl { get; private set; }

    /// <summary>One free-text field; several places are separated by semicolons.</summary>
    public string? Location { get; private set; }
    public string? NormalizedLocation { get; private set; }

    public string? WorkMode { get; private set; }
    public string? EmploymentType { get; private set; }
    public string? Department { get; private set; }
    public int? ExperienceMinYears { get; private set; }
    public int? ExperienceMaxYears { get; private set; }
    public string? RequisitionId { get; private set; }
    public string? NormalizedRequisitionId { get; private set; }
    public DateOnly? PostedDate { get; private set; }

    /// <summary>Plain text.</summary>
    public string? Description { get; private set; }

    /// <summary>Where the user found the job (free text). Manual jobs only.</summary>
    public string? FoundOn { get; private set; }

    public string CreatedVia { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public List<JobStatusHistory> StatusHistory { get; } = [];

    public void SetEmployer(Employer employer)
    {
        Employer = employer;
        EmployerId = employer.Id;
    }

    /// <summary>Replaces every user-editable field. Status, origin and creation time are never touched.</summary>
    public void Apply(JobDetails details, DateTimeOffset now)
    {
        Title = details.Title;
        NormalizedTitle = details.NormalizedTitle;
        ApplyUrl = details.ApplyUrl;
        CanonicalUrl = details.CanonicalUrl;
        Location = details.Location;
        NormalizedLocation = details.NormalizedLocation;
        WorkMode = details.WorkMode;
        EmploymentType = details.EmploymentType;
        Department = details.Department;
        ExperienceMinYears = details.ExperienceMinYears;
        ExperienceMaxYears = details.ExperienceMaxYears;
        RequisitionId = details.RequisitionId;
        NormalizedRequisitionId = details.NormalizedRequisitionId;
        PostedDate = details.PostedDate;
        Description = details.Description;
        FoundOn = details.FoundOn;
        UpdatedAtUtc = now;
    }

    /// <summary>Sets the new status and returns the history row for the caller to add to the context.</summary>
    public JobStatusHistory ChangeStatus(string newStatus, DateTimeOffset now)
    {
        var entry = new JobStatusHistory(Id, Status, newStatus, now);
        Status = newStatus;
        UpdatedAtUtc = now;
        return entry;
    }
}

/// <summary>One timestamped status transition. The first row of a job has no <see cref="FromStatus"/>.</summary>
public class JobStatusHistory
{
    private JobStatusHistory() { }

    public JobStatusHistory(Guid jobId, string? fromStatus, string toStatus, DateTimeOffset changedAtUtc)
    {
        Id = Guid.NewGuid();
        JobId = jobId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ChangedAtUtc = changedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public Job Job { get; set; } = null!;
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = null!;
    public DateTimeOffset ChangedAtUtc { get; private set; }
}
