using System.Text.RegularExpressions;
using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Sources;

/// <summary>How a source pages through its results.</summary>
public enum SourcePagination
{
    /// <summary>One response holds everything.</summary>
    None,

    /// <summary>skip/limit or page-number style.</summary>
    Offset,

    /// <summary>An opaque cursor or next-page link.</summary>
    Cursor,
}

/// <summary>What a source can do. Declared by the adapter, never guessed by the caller.</summary>
/// <param name="Method">One of <see cref="SourceMethods"/>. Link-only and email-derived can never search live.</param>
/// <param name="Pagination">How the source pages.</param>
/// <param name="RequiresCredentials">True when the source needs a key or token. Credentials are never stored in Git or logs.</param>
public sealed record SourceCapabilities(string Method, SourcePagination Pagination, bool RequiresCredentials)
{
    public bool CanSearchLive => SourceMethods.CanSearchLive(Method);
}

/// <summary>What to check. <see cref="EmployerId"/> is null for a source not tied to one employer (a broad search).</summary>
public sealed record SourceRequest(Guid? EmployerId, string? BoardId = null, string? Url = null);

/// <summary>
/// One job as a source reports it, already mapped to the canonical job vocabulary (<see cref="JobWorkModes"/>,
/// <see cref="JobEmploymentTypes"/>). The description is sanitized text; the raw provider payload is never kept (S2.1 D2).
/// </summary>
public sealed record SourceListingData(
    string ExternalId,
    string Title,
    string? SourceUrl,
    string? ApplyUrl = null,
    string? Location = null,
    string? WorkMode = null,
    string? EmploymentType = null,
    string? Department = null,
    string? RequisitionId = null,
    int? ExperienceMinYears = null,
    int? ExperienceMaxYears = null,
    DateTimeOffset? PostedAtUtc = null,
    DateTimeOffset? UpdatedAtUtc = null,
    string? Description = null);

/// <summary>
/// What an adapter returns: listings plus an honest outcome. Build it with the factory methods. An adapter reports only
/// what it actually saw; the common path (<see cref="SourceScanService"/>) enforces the invariants again.
/// </summary>
public sealed record SourceFetchResult
{
    private SourceFetchResult(string outcome, IReadOnlyList<SourceListingData> listings, string? error, string? nextStep, bool retryable, int rejected)
    {
        Outcome = outcome;
        Listings = listings;
        ErrorText = error;
        RecommendedNextStep = nextStep;
        Retryable = retryable;
        ListingsRejected = rejected;
    }

    public string Outcome { get; }
    public IReadOnlyList<SourceListingData> Listings { get; }
    public string? ErrorText { get; }
    public string? RecommendedNextStep { get; }

    /// <summary>True when trying again may help (a timeout, HTTP 429 or 5xx). Never true for Unsupported or NotChecked.</summary>
    public bool Retryable { get; }

    /// <summary>Items the adapter dropped because they were invalid (counted, never silently lost).</summary>
    public int ListingsRejected { get; }

    /// <summary>Everything was read. An empty list is a real "no jobs" answer only here.</summary>
    public static SourceFetchResult Success(IReadOnlyList<SourceListingData> listings, int rejected = 0) =>
        new(ScanOutcomes.Success, listings, null, null, false, rejected);

    /// <summary>Some listings were read but the read did not complete (for example a failure mid-pagination).</summary>
    public static SourceFetchResult Partial(IReadOnlyList<SourceListingData> listings, string error, string? nextStep = null, bool retryable = false, int rejected = 0) =>
        new(ScanOutcomes.Partial, listings, error, nextStep, retryable, rejected);

    public static SourceFetchResult Failed(string error, string? nextStep = null, bool retryable = false) =>
        new(ScanOutcomes.Failed, [], error, nextStep, retryable, 0);

    /// <summary>The source cannot be searched this way (unknown ATS, no API). Never shown as zero results.</summary>
    public static SourceFetchResult Unsupported(string reason, string? nextStep = null) =>
        new(ScanOutcomes.Unsupported, [], reason, nextStep, false, 0);

    /// <summary>The source was not searched (link-only, email-derived, disabled, missing setup).</summary>
    public static SourceFetchResult NotChecked(string reason, string? nextStep = null) =>
        new(ScanOutcomes.NotChecked, [], reason, nextStep, false, 0);
}

/// <summary>The one contract every source, real or fake, plugs into.</summary>
public interface ISourceAdapter
{
    /// <summary>Stable source type name stored on listings and outcomes, for example "greenhouse". At most <see cref="SourceScanLimits.SourceType"/> characters.</summary>
    string SourceType { get; }

    SourceCapabilities Capabilities { get; }

    /// <summary>
    /// Reads the source. Honour the token: the common path cancels it on timeout. Return an outcome instead of throwing
    /// where possible; an exception is recorded as Failed with the exception type only.
    /// </summary>
    Task<SourceFetchResult> FetchAsync(SourceRequest request, CancellationToken cancellationToken);
}

/// <summary>Registry of the adapters available to the common path. A source type can be registered once.</summary>
public sealed class SourceAdapterRegistry
{
    private readonly Dictionary<string, ISourceAdapter> _adapters;

    public SourceAdapterRegistry(IEnumerable<ISourceAdapter> adapters)
    {
        _adapters = new Dictionary<string, ISourceAdapter>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            if (string.IsNullOrWhiteSpace(adapter.SourceType) || adapter.SourceType.Length > SourceScanLimits.SourceType)
            {
                throw new ArgumentException($"Source type '{adapter.SourceType}' is blank or longer than {SourceScanLimits.SourceType} characters.");
            }

            if (!SourceMethods.All.Contains(adapter.Capabilities.Method))
            {
                throw new ArgumentException($"Source '{adapter.SourceType}' declares the unknown method '{adapter.Capabilities.Method}'.");
            }

            if (!_adapters.TryAdd(adapter.SourceType, adapter))
            {
                throw new ArgumentException($"Source type '{adapter.SourceType}' is registered twice.");
            }
        }
    }

    public IReadOnlyCollection<ISourceAdapter> All => _adapters.Values;

    public ISourceAdapter? Find(string sourceType) => _adapters.GetValueOrDefault(sourceType);
}

/// <summary>Makes error and next-step text safe to store and show: one line, no secrets, bounded.</summary>
public static partial class SafeText
{
    // A keyword followed by = or :, then the whole value. An optional auth scheme (Bearer, Basic) is part of the value, so
    // "Authorization: Bearer abc" is redacted completely, not just the word "Bearer".
    [GeneratedRegex(@"(?i)\b(token|key|secret|password|passwd|authorization|bearer|api[_-]?key)\b\s*[=:]\s*(?:(?:bearer|basic)\s+)?\S+")]
    private static partial Regex SecretPattern();

    // A bare "Bearer <credential>" with no keyword before it, for example in a copied header or a provider message.
    [GeneratedRegex(@"(?i)\bbearer\s+\S+")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    public static string? Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var redacted = SecretPattern().Replace(text, m => $"{m.Groups[1].Value}=[redacted]");
        redacted = BearerPattern().Replace(redacted, "bearer=[redacted]");
        var singleLine = WhiteSpace().Replace(redacted, " ").Trim();
        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        return singleLine[..(maxLength - 1)] + "…";
    }
}
