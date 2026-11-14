# Phase 1 data model

Task: *Data model: candidate, company list, search profile, with first EF Core migration.*
Approved by the user on 2026-11-14 (decisions U1-U9, acceptance criteria AC1-AC9).

## Tables (exactly six; the EF `__EFMigrationsHistory` table is not counted)

| Table | Purpose | Key columns |
| --- | --- | --- |
| `candidates` | Whoever the searches are for. No personal data beyond a display name. | `Id`, `Name` |
| `employers` | Canonical employer (name only in Phase 1). | `Id`, `Name`, `NormalizedName` (unique) |
| `company_lists` | Named, reusable list of employers. | `Id`, `Name`, `NormalizedName` (unique), `LocationLabel` (nullable), `DefaultWorkModes` |
| `company_list_employers` | Join: employers in a list. An employer can be in many lists. | PK (`CompanyListId`, `EmployerId`) |
| `search_profiles` | A candidate's reusable search configuration. | `Id`, `CandidateId`, `Name`, `NormalizedName`, `WorkModes` (nullable), `CompanyMonitoringEnabled`, `BroadSearchEnabled`, `ProfileVersion` |
| `search_profile_company_lists` | Join: company lists a profile selects. | PK (`SearchProfileId`, `CompanyListId`) |

## Decisions

| ID | Decision | Where it shows |
| --- | --- | --- |
| U1 | `LocationLabel` is nullable free text on `company_lists`. Location sets are deferred. | `company_lists.LocationLabel` |
| U2 | Employer and list names are unique after normalization; profile names are unique per candidate. | Unique indexes (below) |
| U3 | Employer stores only `Name` and `NormalizedName`. | `employers` has no website, career URL or ATS column |
| U4 | `SearchProfile.WorkModes` overrides the defaults of every list the profile selects. `NULL` inherits list defaults; an empty set (`0`) is a different, explicit value. | `search_profiles.WorkModes` nullable int |
| U5 | `CandidateId` is required; deleting a candidate that has profiles is restricted. | `search_profiles.CandidateId` FK `RESTRICT` |
| U6 | Only two discovery-mode flags; no source catalog or per-source enablement. | `CompanyMonitoringEnabled`, `BroadSearchEnabled` |
| U7 | A draft profile with both modes disabled is valid. | No check on the two flags |
| U8 | Hard deletes with the relationships below. | Foreign keys |
| U9 | `ProfileVersion` starts at 1 and must be at least 1. Nothing increments it in Phase 1. | Check constraint; constructor sets 1 |

### Work modes

`WorkModes` is a bit mask: Remote = 1, On-site = 2, Hybrid = 4. `0` is the empty set. A check constraint keeps values in
0-7 (`company_lists.DefaultWorkModes` is never null; `search_profiles.WorkModes` may be null).

### Name normalization (exact rule)

Implemented in `NameNormalizer` and used for `NormalizedName`:

1. Reject null, empty or whitespace-only input.
2. Apply Unicode normalization form NFKC.
3. Replace every run of characters for which `char.IsWhiteSpace` is true with one U+0020, and drop leading/trailing whitespace.
4. Apply `string.ToLowerInvariant()`.

The result must be at most 200 characters (so must the trimmed display name). The rule uses no current-culture API, so it
gives the same result on every machine. The display `Name` keeps what the user typed, trimmed at both ends. Changing this
rule later requires a data migration that recomputes `NormalizedName`.

### Delete behaviour

| Parent -> child | Rule | Effect |
| --- | --- | --- |
| `candidates` -> `search_profiles` | RESTRICT | A candidate with profiles cannot be deleted. |
| `search_profiles` -> `search_profile_company_lists` | CASCADE | Deleting a profile removes its join rows only. |
| `company_lists` -> `company_list_employers` | CASCADE | Deleting a list removes its employer links only. |
| `employers` -> `company_list_employers` | RESTRICT | An employer that is in a list cannot be deleted. |
| `company_lists` -> `search_profile_company_lists` | RESTRICT | A list a profile still selects cannot be deleted. |

The rows above for `company_lists`/`search_profile_company_lists` and the CASCADE/RESTRICT split were applied from the
approved "proposed cascade/restrict relationships" (U8); see "Assumptions to confirm".

## Deferred requirements (AC7)

| Requirement | Source | Deferred to |
| --- | --- | --- |
| Location sets (named, reusable, with city/state/country/remote scopes) | Product Requirements section 2 | Later phase; Phase 1 uses `LocationLabel` only |
| Role lists, aliases and equivalent titles | Product Requirements section 2 | Later phase; not in the six tables |
| Role lists and location sets as part of a Search Profile | Product Requirements section 3 | Later phase |
| Employer website, career URL, ATS and canonical resolution | Product Requirements section 2 | Phase 2 |
| Source catalog and independently enabled sources per profile | Product Requirements sections 3-4; Testing Plan "Search profiles" | Phase 2 |
| Multiline list entry, duplicate preview before saving | Product Requirements section 2; Testing Plan "Multiline lists" | Later Phase 1 task (UI/API), not this data-model task |
| Automatic `ProfileVersion` increment | U9 | Not in Phase 1 |
| API endpoints for candidates, employers, lists and profiles | - | Delivered by the Backend API task: see [api.md](api.md). UI for any of the above stays a later Phase 1 task |

## Assumptions to confirm

These were not spelled out in the approval text this work was based on:

1. `CandidateId` (U5) is on `search_profiles` only. Company lists and employers are global and reusable, so their names
   are unique across the database (U2), not per candidate.
2. A list that a profile selects cannot be deleted (RESTRICT) rather than silently removed from the profile.
3. `DefaultWorkModes` on a list is a non-null mask defaulting to the empty set.

## Applying and checking the migration

```
dotnet tool restore
dotnet ef database update --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api
dotnet ef migrations has-pending-model-changes --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api
```

The first command needs `ConnectionStrings:Default` (see `development.md`). The migration is not applied automatically at
startup. To undo it on a throw-away database: `dotnet ef database update 0 ...`.
