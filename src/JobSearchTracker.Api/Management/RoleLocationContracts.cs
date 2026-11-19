using JobSearchTracker.Api.Data;

namespace JobSearchTracker.Api.Management;

// Request and response shapes of the role-list and location-set API (wire names are camelCase).

public sealed record RoleListRequest(string? Name);

/// <summary>PUT replaces the title and its whole alias set: omitted <c>aliases</c> means no aliases.</summary>
public sealed record RoleTitleRequest(string? Title, IReadOnlyList<string>? Aliases);

public sealed record RoleAliasResponse(Guid Id, string Alias);

public sealed record RoleTitleResponse(Guid Id, string Title, IReadOnlyList<RoleAliasResponse> Aliases);

public sealed record RoleListSummaryResponse(Guid Id, string Name, int TitleCount, int AliasCount);

public sealed record RoleListResponse(Guid Id, string Name, int TitleCount, int AliasCount, IReadOnlyList<RoleTitleResponse> Titles);

public sealed record LocationSetRequest(string? Name);

/// <summary><c>scope</c> is required; <c>countryCode</c> is required for <c>remoteCountry</c> and must be empty for <c>remoteWorldwide</c>.</summary>
public sealed record LocationRequest(string? Label, string? Scope, string? CountryCode);

public sealed record LocationResponse(Guid Id, string Label, string Scope, string? CountryCode)
{
    public static LocationResponse From(LocationSetLocation l) => new(l.Id, l.Label, l.Scope, l.CountryCode);
}

public sealed record LocationSetSummaryResponse(Guid Id, string Name, int LocationCount);

public sealed record LocationSetResponse(Guid Id, string Name, int LocationCount, IReadOnlyList<LocationResponse> Locations);

public sealed record RoleListReference(Guid Id, string Name);

public sealed record LocationSetReference(Guid Id, string Name);
