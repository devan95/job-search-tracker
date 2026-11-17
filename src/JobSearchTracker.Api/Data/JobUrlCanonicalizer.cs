using System.Text;

namespace JobSearchTracker.Api.Data;

/// <summary>
/// Canonical form of a job URL, used to detect that two records point at the same posting. The exact rule is documented
/// in docs/api.md (section Jobs) and is reused by Phase 2 S2.8:
/// http or https only; no user name or password; host lower case in ASCII (punycode) form without a leading "www.";
/// default port, fragment and trailing slash dropped; the path keeps its case; query parameters on the fixed tracking list
/// (<see cref="IsTrackingParameter"/>) are removed and the remaining ones are sorted so that their order does not matter.
/// Job-identifying parameters such as gh_jid and currentJobId are always kept. The scheme is not folded: http and https
/// URLs of the same page are different canonical URLs.
/// </summary>
public static class JobUrlCanonicalizer
{
    private static readonly string[] TrackingNames =
    [
        "fbclid", "gclid", "dclid", "msclkid", "twclid", "yclid", "mc_cid", "mc_eid", "_hsenc", "_hsmi",
        "trk", "trackingId", "refId", "gh_src", "lever-source", "lever-origin",
    ];

    private const string TrackingPrefix = "utm_";

    /// <summary>The fixed tracking parameter names (case-insensitive) and the prefix, for documentation and tests.</summary>
    public static IReadOnlyList<string> TrackingParameterNames => TrackingNames;

    public static bool IsTrackingParameter(string name) =>
        name.StartsWith(TrackingPrefix, StringComparison.OrdinalIgnoreCase)
        || Array.Exists(TrackingNames, known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns false with a message when the value is not a usable job URL.</summary>
    public static bool TryCanonicalize(string? value, out string canonical, out string? error)
    {
        canonical = string.Empty;
        error = null;

        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = "A URL is required.";
            return false;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(uri.Host))
        {
            error = "The URL must be an absolute http or https URL.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "The URL must not contain a user name or password.";
            return false;
        }

        var host = (uri.HostNameType == UriHostNameType.IPv6 ? uri.Host : uri.IdnHost).ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal) && host.Length > "www.".Length)
        {
            host = host["www.".Length..];
        }

        var builder = new StringBuilder();
        builder.Append(uri.Scheme).Append("://").Append(host);
        if (!uri.IsDefaultPort)
        {
            builder.Append(':').Append(uri.Port);
        }

        builder.Append(uri.AbsolutePath.TrimEnd('/'));

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTrackingParameter(NameOf(pair)))
            .OrderBy(pair => pair, StringComparer.Ordinal)
            .ToList();
        if (kept.Count > 0)
        {
            builder.Append('?').Append(string.Join('&', kept));
        }

        if (builder.Length > JobLimits.Url)
        {
            error = $"The URL must be at most {JobLimits.Url} characters.";
            return false;
        }

        canonical = builder.ToString();
        return true;
    }

    private static string NameOf(string pair)
    {
        var separator = pair.IndexOf('=');
        return separator < 0 ? pair : pair[..separator];
    }
}
