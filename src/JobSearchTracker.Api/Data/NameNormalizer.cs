using System.Text;

namespace JobSearchTracker.Api.Data;

/// <summary>
/// Deterministic, culture-invariant name handling for employers, company lists and search profiles.
/// The exact rule is documented in docs/data-model.md and must change only together with a data migration.
/// </summary>
public static class NameNormalizer
{
    /// <summary>Maximum length of a display name and of its normalized form.</summary>
    public const int MaxLength = 200;

    /// <summary>The name as the user typed it, with leading and trailing whitespace removed.</summary>
    public static string CleanDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A name is required.", nameof(value));
        }

        var display = value.Trim();
        if (display.Length > MaxLength)
        {
            throw new ArgumentException($"A name must be at most {MaxLength} characters.", nameof(value));
        }

        return display;
    }

    /// <summary>
    /// Normalized form used for uniqueness:
    /// 1. Unicode NFKC; 2. every run of Unicode whitespace becomes one U+0020 and the ends are trimmed;
    /// 3. <see cref="string.ToLowerInvariant"/>. No current-culture API is used.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A name is required.", nameof(value));
        }

        var composed = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(composed.Length);
        var pendingSpace = false;
        foreach (var ch in composed)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        var normalized = builder.ToString().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A name is required.", nameof(value));
        }

        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException($"A name must be at most {MaxLength} characters after normalization.", nameof(value));
        }

        return normalized;
    }
}
