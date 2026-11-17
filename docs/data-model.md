# Phase 1 data model

Task: *Data model: candidate, company list, search profile, with first EF Core migration.*
Approved by the user on 2026-11-14 (decisions U1-U9, acceptance criteria AC1-AC9).

## Tables (exactly eight; the EF `__EFMigrationsHistory` table is not counted)

| Table | Purpose | Key columns |
| --- | --- | --- |
| `candidates` | Whoever the searches are for. No personal data beyond a display name. | `Id`, `Name` |
| `employers` | Canonical employer (name only in Phase 1). | `Id`, `Name`, `NormalizedName` (unique) |
| `company_lists` | Named, reusable list of employers. | `Id`, `Name`, `NormalizedName` (unique), `LocationLabel` (nullable), `DefaultWorkModes` |
| `company_list_employers` | Join: employers in a list. An employer can be in many lists. | PK (`CompanyListId`, `EmployerId`) |
| `search_profiles` | A candidate's reusable search configuration. | `Id`, `CandidateId`, `Name`, `NormalizedName`, `WorkModes` (nullable), `CompanyMonitoringEnabled`, `BroadSearchEnabled`, `ProfileVersion` |
| `search_profile_company_lists` | Join: company lists a profile selects. | PK (`SearchProfileId`, `CompanyListId`) |
| `jobs` | Canonical job record (Order 6, migration `20261117060000_AddJobs`). Global, not per candidate. Manual in Phase 1; Phase 2 source listings will point at it. | `Id`, `EmployerId`, `Title`, `NormalizedTitle`, `CanonicalUrl`, `NormalizedLocation`, `NormalizedRequisitionId`, `Status`, `CreatedVia` |
| `job_status_history` | One timestamped row per job status transition (the first row has no `FromStatus`). | `Id`, `JobId`, `FromStatus`, `ToStatus`, `ChangedAtUtc` |

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
| `employers` -> `jobs` | RESTRICT | An employer that has jobs cannot be deleted. |
| `jobs` -> `job_status_history` | CASCADE | Deleting a job removes its history only. |

Tables that will reference a job later (applications, source listings) must use RESTRICT, so a job that has either cannot
be hard-deleted (OQ-P1.6); the API maps that foreign-key failure to `409 job_in_use`.

The rows above for `company_lists`/`search_profile_company_lists` and the CASCADE/RESTRICT split were applied from the
approved "proposed cascade/restrict relationships" (U8); see "Assumptions to confirm".

## Jobs (Order 6; decisions OQ-P1.4 to OQ-P1.7, answered 2026-11-15)

Field names follow Phase 2 S2.1 D1, which Phase 2 extends (source listings, content hash, updated times) without renaming.

| Column | Type | Rule |
| --- | --- | --- |
| `EmployerId` | uuid | Required; FK to `employers`, RESTRICT. Resolved by typed name (find-or-create) |
| `Title` / `NormalizedTitle` | varchar(200) | Required, not blank (check). Normalized with the name normalizer |
| `ApplyUrl` / `CanonicalUrl` | varchar(2000), nullable | http or https only. `CanonicalUrl` is the duplicate key, see api.md (Jobs) |
| `Location` / `NormalizedLocation` | varchar(200), nullable | One free-text field; several places separated by semicolons |
| `WorkMode` | varchar(10), nullable | `remote`, `onSite`, `hybrid` (check); one value, not a mask |
| `EmploymentType` | varchar(20), nullable | `fullTime`, `partTime`, `contract`, `internship`, `temporary`, `other` (check) |
| `Department`, `RequisitionId` / `NormalizedRequisitionId`, `FoundOn` | varchar, nullable | `FoundOn` is for manual jobs |
| `ExperienceMinYears`, `ExperienceMaxYears` | integer, nullable | Whole numbers, >= 0, min <= max (checks); the API limits them to 60 |
| `PostedDate` | date, nullable | |
| `Description` | text, nullable | Plain text; the API limits it to 50 000 characters |
| `CreatedVia` | varchar(20) | `manual` for every Phase 1 job; Phase 2 adds source-created values |
| `Status` | varchar(30) | `new`, `open`, `closed`, `expired`, `reopened`, `unavailable`, `verificationPending` (check). A new manual job is `open`. `new` and `verificationPending` are system-only and already allowed so Phase 2 needs no check change |
| `CreatedAtUtc`, `UpdatedAtUtc` | timestamptz | Set by the system |

No salary, notes, contacts or application deadline (OQ-P1.4). Job status never changes an application's status (OQ-P1.5).

Indexes: unique (`EmployerId`, `NormalizedRequisitionId`) where the requisition is not null (same employer + same requisition
id is one job, no override); (`EmployerId`, `NormalizedTitle`); (`CanonicalUrl`). Different requisition ids are different jobs, and a
job without a requisition id is not constrained. The URL and title + location rules are application-level warnings (OQ-P1.7),
not unique indexes, because the user may confirm them.

## Deferred requirements (AC7)

Not in this model: location sets, role lists and aliases, applications, job source listings, scan runs, employer website/career URL/ATS, source catalog and per-source enablement, automatic `ProfileVersion` increment. Their owner and phase are on the Notion Task Board and Product Requirements; the API is in [api.md](api.md).

## Assumptions to confirm

These were not spelled out in the approval text this work was based on:

1. `CandidateId` (U5) is on `search_profiles` only. Company lists and employers are global and reusable, so their names
   are unique across the database (U2), not per candidate.
2. A list that a profile selects cannot be deleted (RESTRICT) rather than silently removed from the profile.
3. `DefaultWorkModes` on a list is a non-null mask defaulting to the empty set.

## Applying and checking the migration

See "Database schema (migrations)" and "Tests" in [development.md](development.md). To undo on a throw-away database: `dotnet ef database update 0 --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`.
