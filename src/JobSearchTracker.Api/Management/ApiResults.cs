using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobSearchTracker.Api.Management;

/// <summary>
/// Error semantics shared by every management endpoint. All errors are RFC 7807 problem documents with a stable
/// machine-readable <c>code</c> extension:
/// 400 validation (field errors under <c>errors</c>), 404 not found, 409 conflict (duplicate name or a delete that the
/// database design restricts).
/// </summary>
internal static class ApiResults
{
    public const string NotFoundCode = "not_found";
    public const string DuplicateNameCode = "duplicate_name";
    public const string CandidateHasProfilesCode = "candidate_has_profiles";
    public const string CompanyListInUseCode = "company_list_in_use";
    public const string EmployerInListCode = "employer_in_company_list";
    public const string NotAMemberCode = "not_a_member";
    public const string EmployerHasJobsCode = "employer_has_jobs";
    public const string DuplicateRequisitionCode = "duplicate_requisition";
    public const string PossibleDuplicateCode = "possible_duplicate";
    public const string InvalidStatusTransitionCode = "invalid_status_transition";
    public const string JobInUseCode = "job_in_use";
    public const string ConcurrentModificationCode = "concurrent_modification";

    public static IResult NotFound(string what, Guid id) =>
        Problem(StatusCodes.Status404NotFound, NotFoundCode, $"{what} not found", $"No {what.ToLowerInvariant()} with id {id} exists.");

    public static IResult NotFound(string title, string detail, string code = NotFoundCode) =>
        Problem(StatusCodes.Status404NotFound, code, title, detail);

    public static IResult Conflict(string code, string title, string detail, IDictionary<string, object?>? extra = null) =>
        Problem(StatusCodes.Status409Conflict, code, title, detail, extra);

    private static IResult Problem(int status, string code, string title, string detail, IDictionary<string, object?>? extra = null)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (extra is not null)
        {
            foreach (var pair in extra)
            {
                extensions[pair.Key] = pair.Value;
            }
        }

        return Results.Problem(detail: detail, statusCode: status, title: title, extensions: extensions);
    }
}

/// <summary>Collects field errors so that one 400 response can report every problem in a request.</summary>
internal sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    public IResult ToResult()
    {
        var errors = _errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        return Results.ValidationProblem(
            errors,
            extensions: new Dictionary<string, object?> { ["code"] = "validation" });
    }
}

internal static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Also true for 23001: PostgreSQL 18 reports a blocked ON DELETE RESTRICT delete with that code instead of 23503.</summary>
    public static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation };

    /// <summary>Name of the violated constraint or unique index, when PostgreSQL reports one.</summary>
    public static string? ConstraintName(DbUpdateException ex) =>
        (ex.InnerException as PostgresException)?.ConstraintName;
}
