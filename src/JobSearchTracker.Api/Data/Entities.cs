namespace JobSearchTracker.Api.Data;

/// <summary>Work arrangements. A set is stored as a bit mask; 0 is the empty set.</summary>
[Flags]
public enum WorkModes
{
    None = 0,
    Remote = 1,
    OnSite = 2,
    Hybrid = 4,
}

/// <summary>Constants shared by the model configuration and the tests.</summary>
public static class WorkModesLimits
{
    public const int AllModes = (int)(WorkModes.Remote | WorkModes.OnSite | WorkModes.Hybrid);
}

public class Candidate
{
    private Candidate() { }

    public Candidate(string name)
    {
        Id = Guid.NewGuid();
        Name = NameNormalizer.CleanDisplayName(name);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public List<SearchProfile> SearchProfiles { get; } = [];
}

/// <summary>Canonical employer. Phase 1 stores only the name; website, career URL and ATS arrive in Phase 2.</summary>
public class Employer
{
    private Employer() { }

    public Employer(string name)
    {
        Id = Guid.NewGuid();
        Rename(name);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;
    public List<CompanyListEmployer> CompanyLists { get; } = [];

    public void Rename(string name)
    {
        Name = NameNormalizer.CleanDisplayName(name);
        NormalizedName = NameNormalizer.Normalize(name);
    }
}

public class CompanyList
{
    public const int LocationLabelMaxLength = 200;

    private CompanyList() { }

    public CompanyList(string name, string? locationLabel = null, WorkModes defaultWorkModes = WorkModes.None)
    {
        Id = Guid.NewGuid();
        Rename(name);
        SetLocationLabel(locationLabel);
        DefaultWorkModes = defaultWorkModes;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;

    /// <summary>Free-text location grouping chosen by the user. Null when not set. Location sets are deferred.</summary>
    public string? LocationLabel { get; private set; }

    /// <summary>Default work modes for the companies in this list; a Search Profile may override them.</summary>
    public WorkModes DefaultWorkModes { get; set; }

    public List<CompanyListEmployer> Employers { get; } = [];
    public List<SearchProfileCompanyList> SearchProfiles { get; } = [];

    public void Rename(string name)
    {
        Name = NameNormalizer.CleanDisplayName(name);
        NormalizedName = NameNormalizer.Normalize(name);
    }

    public void SetLocationLabel(string? locationLabel)
    {
        var trimmed = locationLabel?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            LocationLabel = null;
            return;
        }

        if (trimmed.Length > LocationLabelMaxLength)
        {
            throw new ArgumentException($"A location label must be at most {LocationLabelMaxLength} characters.", nameof(locationLabel));
        }

        LocationLabel = trimmed;
    }
}

/// <summary>Join row: an employer belongs to a company list. An employer may be in many lists.</summary>
public class CompanyListEmployer
{
    public Guid CompanyListId { get; set; }
    public CompanyList CompanyList { get; set; } = null!;
    public Guid EmployerId { get; set; }
    public Employer Employer { get; set; } = null!;
}

public class SearchProfile
{
    private SearchProfile() { }

    public SearchProfile(Guid candidateId, string name)
    {
        Id = Guid.NewGuid();
        CandidateId = candidateId;
        Rename(name);
    }

    public Guid Id { get; private set; }
    public Guid CandidateId { get; private set; }
    public Candidate Candidate { get; set; } = null!;
    public string Name { get; private set; } = null!;
    public string NormalizedName { get; private set; } = null!;

    /// <summary>
    /// Work-mode override for every list this profile selects. Null inherits each list's defaults;
    /// an empty set (<see cref="WorkModes.None"/>) is a distinct, explicit value.
    /// </summary>
    public WorkModes? WorkModes { get; set; }

    public bool CompanyMonitoringEnabled { get; set; }
    public bool BroadSearchEnabled { get; set; }

    /// <summary>Starts at 1, never below 1. Phase 1 has no automatic increment logic.</summary>
    public int ProfileVersion { get; set; } = 1;

    public List<SearchProfileCompanyList> CompanyLists { get; } = [];

    public void Rename(string name)
    {
        Name = NameNormalizer.CleanDisplayName(name);
        NormalizedName = NameNormalizer.Normalize(name);
    }
}

/// <summary>Join row: a search profile selects a company list.</summary>
public class SearchProfileCompanyList
{
    public Guid SearchProfileId { get; set; }
    public SearchProfile SearchProfile { get; set; } = null!;
    public Guid CompanyListId { get; set; }
    public CompanyList CompanyList { get; set; } = null!;
}
