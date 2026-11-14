using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

/// <summary>Request validation that reuses the data-model rules (NameNormalizer, length limits, work-mode names).</summary>
internal static class Input
{
    public const int DefaultTake = 100;
    public const int MaxTake = 500;

    /// <summary>Validates a display name (required, at most 200 characters once trimmed).</summary>
    public static bool TryDisplayName(string? value, string field, ValidationErrors errors, out string display)
    {
        display = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(field, "A name is required.");
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > NameNormalizer.MaxLength)
        {
            errors.Add(field, $"A name must be at most {NameNormalizer.MaxLength} characters.");
            return false;
        }

        display = trimmed;
        return true;
    }

    /// <summary>Validates a name that must be unique after normalization and returns both forms.</summary>
    public static bool TryUniqueName(string? value, string field, ValidationErrors errors, out string display, out string normalized)
    {
        normalized = string.Empty;
        if (!TryDisplayName(value, field, errors, out display))
        {
            return false;
        }

        try
        {
            normalized = NameNormalizer.Normalize(value);
            return true;
        }
        catch (ArgumentException)
        {
            // Only reachable when NFKC expands the name beyond the limit.
            errors.Add(field, $"A name must be at most {NameNormalizer.MaxLength} characters after normalization.");
            return false;
        }
    }

    /// <summary>Trims the label; blank becomes null. Returns false when it is longer than the data model allows.</summary>
    public static bool TryLocationLabel(string? value, string field, ValidationErrors errors, out string? label)
    {
        var trimmed = value?.Trim();
        label = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        if (label is { Length: > CompanyList.LocationLabelMaxLength })
        {
            errors.Add(field, $"A location label must be at most {CompanyList.LocationLabelMaxLength} characters.");
            return false;
        }

        return true;
    }

    public static bool TryPaging(int? skip, int? take, ValidationErrors errors, out int skipValue, out int takeValue)
    {
        skipValue = skip ?? 0;
        takeValue = take ?? DefaultTake;
        var ok = true;
        if (skipValue < 0)
        {
            errors.Add("skip", "skip must be 0 or greater.");
            ok = false;
        }

        if (takeValue is < 1 or > MaxTake)
        {
            errors.Add("take", $"take must be between 1 and {MaxTake}.");
            ok = false;
        }

        return ok;
    }
}

/// <summary>
/// Work modes cross the API as an array of names (<c>remote</c>, <c>onSite</c>, <c>hybrid</c>, case-insensitive on input)
/// and are stored as the bit mask of the data model. Repeated names are treated as one; the empty array is the empty set.
/// </summary>
internal static class WorkModesMapping
{
    private static readonly (string Name, WorkModes Mode)[] Names =
    [
        ("remote", WorkModes.Remote),
        ("onSite", WorkModes.OnSite),
        ("hybrid", WorkModes.Hybrid),
    ];

    public static bool TryParse(IReadOnlyList<string>? values, string field, ValidationErrors errors, out WorkModes modes)
    {
        modes = WorkModes.None;
        if (values is null)
        {
            return true;
        }

        var ok = true;
        foreach (var value in values)
        {
            var match = Names.FirstOrDefault(n => string.Equals(n.Name, value?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
            {
                errors.Add(field, $"Unknown work mode '{value}'. Allowed values: remote, onSite, hybrid.");
                ok = false;
                continue;
            }

            modes |= match.Mode;
        }

        return ok;
    }

    public static IReadOnlyList<string> ToNames(WorkModes modes) =>
        Names.Where(n => modes.HasFlag(n.Mode)).Select(n => n.Name).ToList();
}
