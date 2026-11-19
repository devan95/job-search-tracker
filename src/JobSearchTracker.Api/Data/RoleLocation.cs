namespace JobSearchTracker.Api.Data;

/// <summary>
/// Location scopes (OQ-P1.2). Stored as these exact strings. <see cref="RemoteCountry"/> needs a country code and
/// <see cref="RemoteWorldwide"/> must have none, so "Remote India" and "Remote worldwide" can never be confused.
/// </summary>
public static class LocationScopes
{
    public const string City = "city";
    public const string State = "state";
    public const string Country = "country";
    public const string RemoteCountry = "remoteCountry";
    public const string RemoteWorldwide = "remoteWorldwide";

    public static readonly string[] All = [City, State, Country, RemoteCountry, RemoteWorldwide];
}

/// <summary>A named, reusable list of target role titles. Matching against jobs is Phase 3 (S3.2); Phase 1 only stores.</summary>
public class RoleList
{
    private RoleList() { }

    public RoleList(string name)
    {
        Id = Guid.NewGuid();
        Rename(name);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public List<RoleListTitle> Titles { get; } = [];
    public List<SearchProfileRoleList> SearchProfiles { get; } = [];

    public void Rename(string name)
    {
        Name = NameNormalizer.CleanDisplayName(name);
        NormalizedName = NameNormalizer.Normalize(name);
    }
}

/// <summary>
/// One title in a role list. A primary title has no <see cref="PrimaryTitleId"/>; an alias points at its primary title.
/// Every title and alias is unique within its list after normalization (OQ-P1.1), so one lookup finds either.
/// </summary>
public class RoleListTitle
{
    private RoleListTitle() { }

    public RoleListTitle(Guid roleListId, string title, Guid? primaryTitleId = null)
    {
        Id = Guid.NewGuid();
        RoleListId = roleListId;
        PrimaryTitleId = primaryTitleId;
        Rename(title);
    }

    public Guid Id { get; private set; }
    public Guid RoleListId { get; private set; }
    public RoleList RoleList { get; set; } = null!;
    public string Title { get; private set; } = null!;
    public string NormalizedTitle { get; private set; } = null!;

    /// <summary>Null for a primary title; the primary title's id for an alias.</summary>
    public Guid? PrimaryTitleId { get; private set; }
    public RoleListTitle? PrimaryTitle { get; set; }
    public List<RoleListTitle> Aliases { get; } = [];

    public bool IsAlias => PrimaryTitleId is not null;

    public void Rename(string title)
    {
        Title = NameNormalizer.CleanDisplayName(title);
        NormalizedTitle = NameNormalizer.Normalize(title);
    }
}

/// <summary>A named, reusable set of locations. Work arrangement (remote, on-site, hybrid) is not part of a location.</summary>
public class LocationSet
{
    private LocationSet() { }

    public LocationSet(string name)
    {
        Id = Guid.NewGuid();
        Rename(name);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public List<LocationSetLocation> Locations { get; } = [];
    public List<SearchProfileLocationSet> SearchProfiles { get; } = [];

    public void Rename(string name)
    {
        Name = NameNormalizer.CleanDisplayName(name);
        NormalizedName = NameNormalizer.Normalize(name);
    }
}

/// <summary>
/// One location: a free-text label, a required scope and an optional ISO 3166-1 alpha-2 country code. The code is not
/// checked against a country list (no gazetteer), only for its shape.
/// </summary>
public class LocationSetLocation
{
    public const int CountryCodeLength = 2;

    private LocationSetLocation() { }

    public LocationSetLocation(Guid locationSetId, string label, string scope, string? countryCode)
    {
        Id = Guid.NewGuid();
        LocationSetId = locationSetId;
        Label = NameNormalizer.CleanDisplayName(label);
        NormalizedLabel = NameNormalizer.Normalize(label);
        Scope = scope;
        CountryCode = countryCode;
    }

    public Guid Id { get; private set; }
    public Guid LocationSetId { get; private set; }
    public LocationSet LocationSet { get; set; } = null!;
    public string Label { get; private set; } = null!;
    public string NormalizedLabel { get; private set; } = null!;
    public string Scope { get; private set; } = null!;

    /// <summary>Upper-case two-letter code; required for <see cref="LocationScopes.RemoteCountry"/>, forbidden for <see cref="LocationScopes.RemoteWorldwide"/>.</summary>
    public string? CountryCode { get; private set; }

    public void Change(string label, string scope, string? countryCode)
    {
        Label = NameNormalizer.CleanDisplayName(label);
        NormalizedLabel = NameNormalizer.Normalize(label);
        Scope = scope;
        CountryCode = countryCode;
    }
}

/// <summary>Join row: a search profile selects a role list.</summary>
public class SearchProfileRoleList
{
    public Guid SearchProfileId { get; set; }
    public SearchProfile SearchProfile { get; set; } = null!;
    public Guid RoleListId { get; set; }
    public RoleList RoleList { get; set; } = null!;
}

/// <summary>Join row: a search profile selects a location set.</summary>
public class SearchProfileLocationSet
{
    public Guid SearchProfileId { get; set; }
    public SearchProfile SearchProfile { get; set; } = null!;
    public Guid LocationSetId { get; set; }
    public LocationSet LocationSet { get; set; } = null!;
}
