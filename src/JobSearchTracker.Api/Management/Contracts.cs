using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

// Request and response shapes of the Phase 1 management API. Wire names are camelCase (ASP.NET Core default).
// Request properties are nullable so that a missing field becomes a field error (400), not a binding failure.

public sealed record CandidateRequest(string? Name);

public sealed record CandidateResponse(Guid Id, string Name)
{
    public static CandidateResponse From(Candidate c) => new(c.Id, c.Name);
}

public sealed record EmployerRequest(string? Name);

public sealed record EmployerResponse(Guid Id, string Name, string NormalizedName)
{
    public static EmployerResponse From(Employer e) => new(e.Id, e.Name, e.NormalizedName);
}

/// <summary>PUT replaces the list's own fields: an omitted <c>defaultWorkModes</c> is the empty set, an omitted <c>locationLabel</c> clears it.</summary>
public sealed record CompanyListRequest(string? Name, string? LocationLabel, IReadOnlyList<string>? DefaultWorkModes);

public sealed record CompanyListSummaryResponse(
    Guid Id,
    string Name,
    string? LocationLabel,
    IReadOnlyList<string> DefaultWorkModes,
    int EmployerCount);

public sealed record CompanyListResponse(
    Guid Id,
    string Name,
    string? LocationLabel,
    IReadOnlyList<string> DefaultWorkModes,
    int EmployerCount,
    IReadOnlyList<EmployerResponse> Employers);

public sealed record CreateSearchProfileRequest(
    Guid? CandidateId,
    string? Name,
    IReadOnlyList<Guid>? CompanyListIds,
    IReadOnlyList<string>? WorkModes,
    bool? CompanyMonitoringEnabled,
    bool? BroadSearchEnabled,
    IReadOnlyList<Guid>? RoleListIds = null,
    IReadOnlyList<Guid>? LocationSetIds = null);

/// <summary>
/// PUT replaces the profile's own fields; both flags are required. An omitted or null <c>workModes</c> means "inherit the
/// selected lists' defaults"; an empty array is an explicit empty set. Selected lists change only through the
/// association endpoints.
/// </summary>
public sealed record UpdateSearchProfileRequest(
    string? Name,
    IReadOnlyList<string>? WorkModes,
    bool? CompanyMonitoringEnabled,
    bool? BroadSearchEnabled);

public sealed record CompanyListReference(Guid Id, string Name, IReadOnlyList<string> DefaultWorkModes);

public sealed record SearchProfileResponse(
    Guid Id,
    Guid CandidateId,
    string Name,
    IReadOnlyList<string>? WorkModes,
    bool CompanyMonitoringEnabled,
    bool BroadSearchEnabled,
    int ProfileVersion,
    IReadOnlyList<CompanyListReference> CompanyLists,
    IReadOnlyList<RoleListReference> RoleLists,
    IReadOnlyList<LocationSetReference> LocationSets);
