using System.Security.Cryptography;
using System.Text;

namespace JobSearchTracker.Api.Data;

/// <summary>
/// Honest outcome of one source check (Testing Plan §3). The stored and wire value is the camelCase name.
/// "Not checked" is <see cref="NotChecked"/>: a source that was never actually searched is never reported as zero results.
/// </summary>
public static class ScanOutcomes
{
    public const string Success = "success";
    public const string Partial = "partial";
    public const string Failed = "failed";
    public const string Unsupported = "unsupported";
    public const string NotChecked = "notChecked";

    public static readonly IReadOnlyList<string> All = [Success, Partial, Failed, Unsupported, NotChecked];

    /// <summary>Outcomes that mean listings were really read from the source.</summary>
    public static bool ReadListings(string outcome) => outcome is Success or Partial;
}

/// <summary>How a source is reached. Link-only and email-derived sources never search live (see <see cref="CannotSearchLive"/>).</summary>
public static class SourceMethods
{
    public const string Api = "api";
    public const string CareerPage = "careerPage";
    public const string LinkOnly = "linkOnly";
    public const string EmailDerived = "emailDerived";

    public static readonly IReadOnlyList<string> All = [Api, CareerPage, LinkOnly, EmailDerived];

    /// <summary>Methods that only hand the user a link or read mail: they can never report Success or Partial for a live search.</summary>
    public static readonly IReadOnlyList<string> CannotSearchLive = [LinkOnly, EmailDerived];

    public static bool CanSearchLive(string method) => !CannotSearchLive.Contains(method);
}

public static class ScanTriggers
{
    public const string Manual = "manual";
    public const string Scheduled = "scheduled";
    public const string BoardTest = "boardTest";

    public static readonly IReadOnlyList<string> All = [Manual, Scheduled, BoardTest];
}

/// <summary>Source type names known so far. The column is free text (a source catalog arrives with S2.9), so this list is not a check.</summary>
public static class SourceTypes
{
    public const string Greenhouse = "greenhouse";
    public const string Lever = "lever";
    public const string Ashby = "ashby";
}

public static class SourceScanLimits
{
    public const int SourceType = 30;
    public const int ExternalId = 200;
    public const int Url = JobLimits.Url;
    public const int ContentHash = 64;
    public const int ErrorText = 500;
    public const int NextStep = 500;
}

/// <summary>Content hash of a listing's sanitized description (D1/D2: the hash is kept, the raw payload never is).</summary>
public static class ListingContent
{
    /// <summary>Lower-case hex SHA-256 of the text with every whitespace run collapsed to one space. Null and blank hash alike.</summary>
    public static string Hash(string? sanitizedText)
    {
        var collapsed = string.Join(' ', (sanitizedText ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(collapsed))).ToLowerInvariant();
    }
}

/// <summary>
/// One listing of a canonical job on one source. A job can have several listings; (source type, external id) is unique.
/// A job that has a listing cannot be hard-deleted (RESTRICT).
/// </summary>
public class SourceListing
{
    private SourceListing() { }

    public SourceListing(Guid jobId, string sourceType, string externalId, string? sourceUrl, string contentHash,
        DateTimeOffset? sourceUpdatedAtUtc, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        JobId = jobId;
        SourceType = sourceType;
        ExternalId = externalId;
        SourceUrl = sourceUrl;
        ContentHash = contentHash;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        FirstSeenAtUtc = now;
        LastSeenAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string ExternalId { get; private set; } = null!;
    public string? SourceUrl { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the sanitized description, see <see cref="ListingContent"/>.</summary>
    public string ContentHash { get; private set; } = null!;

    /// <summary>The "updated" time the source itself reports, when it reports one.</summary>
    public DateTimeOffset? SourceUpdatedAtUtc { get; private set; }

    public DateTimeOffset FirstSeenAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    /// <summary>Records that a successful scan saw the listing again. First seen never changes.</summary>
    public void Seen(string contentHash, DateTimeOffset? sourceUpdatedAtUtc, DateTimeOffset now)
    {
        ContentHash = contentHash;
        SourceUpdatedAtUtc = sourceUpdatedAtUtc;
        if (now > LastSeenAtUtc)
        {
            LastSeenAtUtc = now;
        }
    }
}

/// <summary>One scan: which profile (if any), why it ran, when, and the overall outcome (null while it is still running).</summary>
public class ScanRun
{
    private ScanRun() { }

    public ScanRun(Guid? searchProfileId, string trigger, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        SearchProfileId = searchProfileId;
        Trigger = trigger;
        StartedAtUtc = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Null for a run without a profile (a board test). Set to null if the profile is later deleted; history stays.</summary>
    public Guid? SearchProfileId { get; private set; }

    public string Trigger { get; private set; } = null!;
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? FinishedAtUtc { get; private set; }
    public string? Outcome { get; private set; }

    public List<SourceOutcome> Outcomes { get; } = [];

    public void Finish(string outcome, DateTimeOffset now)
    {
        Outcome = outcome;
        FinishedAtUtc = now < StartedAtUtc ? StartedAtUtc : now;
    }
}

/// <summary>What actually happened when one source was checked in one run.</summary>
public class SourceOutcome
{
    private SourceOutcome() { }

    public SourceOutcome(Guid scanRunId, string sourceType, string method, Guid? employerId, string outcome, string? errorText,
        string? recommendedNextStep, int listingsFound, int listingsRejected, int attempts, DateTimeOffset startedAtUtc,
        DateTimeOffset finishedAtUtc)
    {
        Id = Guid.NewGuid();
        ScanRunId = scanRunId;
        SourceType = sourceType;
        Method = method;
        EmployerId = employerId;
        Outcome = outcome;
        ErrorText = errorText;
        RecommendedNextStep = recommendedNextStep;
        ListingsFound = listingsFound;
        ListingsRejected = listingsRejected;
        Attempts = attempts;
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = finishedAtUtc < startedAtUtc ? startedAtUtc : finishedAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid ScanRunId { get; private set; }
    public ScanRun ScanRun { get; set; } = null!;
    public string SourceType { get; private set; } = null!;

    /// <summary>How the source was reached; see <see cref="SourceMethods"/>.</summary>
    public string Method { get; private set; } = null!;

    /// <summary>Null for a source that is not tied to one employer (a broad search). Set to null if the employer is deleted.</summary>
    public Guid? EmployerId { get; private set; }

    public string Outcome { get; private set; } = null!;

    /// <summary>Safe text only: no payload, secret or stack trace.</summary>
    public string? ErrorText { get; private set; }

    public string? RecommendedNextStep { get; private set; }
    public int ListingsFound { get; private set; }
    public int ListingsRejected { get; private set; }

    /// <summary>How many tries the common path made (1 = no retry).</summary>
    public int Attempts { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset FinishedAtUtc { get; private set; }
}
