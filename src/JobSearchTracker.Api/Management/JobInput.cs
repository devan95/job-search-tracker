using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

/// <summary>A job request after validation: the typed employer name and the normalized job fields.</summary>
internal sealed record ParsedJobRequest(string EmployerName, string EmployerNormalized, JobDetails Details, bool ConfirmDifferentJob);

/// <summary>Validation of job requests against the confirmed field list (OQ-P1.4). Every problem is reported together.</summary>
internal static class JobInput
{
    public static bool TryParse(JobRequest? request, ValidationErrors errors, out ParsedJobRequest parsed)
    {
        parsed = null!;
        request ??= new JobRequest(null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        Input.TryUniqueName(request.Employer, "employer", errors, out var employerName, out var employerNormalized);

        var title = Text(request.Title, "title", "A title", JobLimits.Title, errors);
        if (title is null && string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add("title", "A title is required.");
        }

        var normalizedTitle = Normalized(title, "title", "A title", errors);

        var applyUrl = Text(request.ApplyUrl, "applyUrl", "The job URL", JobLimits.Url, errors);
        string? canonicalUrl = null;
        if (applyUrl is not null)
        {
            if (JobUrlCanonicalizer.TryCanonicalize(applyUrl, out var canonical, out var urlError))
            {
                canonicalUrl = canonical;
            }
            else
            {
                errors.Add("applyUrl", urlError!);
                applyUrl = null;
            }
        }

        var location = Text(request.Location, "location", "The location", JobLimits.Location, errors);
        var normalizedLocation = Normalized(location, "location", "The location", errors);

        var workMode = Choice(request.WorkMode, JobWorkModes.All, "workMode", "work mode", errors);
        var employmentType = Choice(request.EmploymentType, JobEmploymentTypes.All, "employmentType", "employment type", errors);
        var department = Text(request.Department, "department", "The department", JobLimits.Department, errors);

        var minYears = Years(request.ExperienceMinYears, "experienceMinYears", errors);
        var maxYears = Years(request.ExperienceMaxYears, "experienceMaxYears", errors);
        if (minYears is not null && maxYears is not null && minYears > maxYears)
        {
            errors.Add("experienceMaxYears", "The maximum experience must not be less than the minimum.");
        }

        var requisitionId = Text(request.RequisitionId, "requisitionId", "The requisition id", JobLimits.RequisitionId, errors);
        var normalizedRequisition = Normalized(requisitionId, "requisitionId", "The requisition id", errors);

        var description = Text(request.Description, "description", "The description", JobLimits.Description, errors);
        var foundOn = Text(request.FoundOn, "foundOn", "Found on", JobLimits.FoundOn, errors);

        if (errors.Any)
        {
            return false;
        }

        var details = new JobDetails(
            title!, normalizedTitle!, applyUrl, canonicalUrl, location, normalizedLocation, workMode, employmentType,
            department, minYears, maxYears, requisitionId, normalizedRequisition, request.PostedDate, description, foundOn);
        parsed = new ParsedJobRequest(employerName, employerNormalized, details, request.ConfirmDifferentJob == true);
        return true;
    }

    /// <summary>Validates a requested status change. Only the user-settable statuses are accepted.</summary>
    public static string? TryStatus(string? value, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add("status", "A status is required.");
            return null;
        }

        return Choice(value, JobStatuses.UserSettable, "status", "status", errors);
    }

    /// <summary>Case-insensitive match against the allowed values; returns the canonical spelling. Blank is unset (null).</summary>
    public static string? Choice(string? value, IReadOnlyList<string> allowed, string field, string label, ValidationErrors errors)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            errors.Add(field, $"Unknown {label} '{trimmed}'. Allowed values: {string.Join(", ", allowed)}.");
        }

        return match;
    }

    /// <summary>Trims; blank becomes null; reports a value that is longer than the limit.</summary>
    private static string? Text(string? value, string field, string label, int maxLength, ValidationErrors errors)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            errors.Add(field, $"{label} must be at most {maxLength} characters.");
            return null;
        }

        return trimmed;
    }

    private static string? Normalized(string? text, string field, string label, ValidationErrors errors)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            return NameNormalizer.Normalize(text);
        }
        catch (ArgumentException)
        {
            errors.Add(field, $"{label} must be at most {NameNormalizer.MaxLength} characters after normalization.");
            return null;
        }
    }

    private static int? Years(int? value, string field, ValidationErrors errors)
    {
        if (value is null)
        {
            return null;
        }

        if (value is < 0 or > JobLimits.MaxExperienceYears)
        {
            errors.Add(field, $"Years of experience must be a whole number from 0 to {JobLimits.MaxExperienceYears}.");
            return null;
        }

        return value;
    }
}
