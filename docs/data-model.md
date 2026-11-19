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

## Jobs (Order 6; decisions OQ-P1.4 to OQ-P1.7, answered 2026-11-15)

Order 6 (migration `20261117060000_AddJobs`) adds two tables, so the model has eight tables in total; the heading above and the
first table describe the original six.

| Table | Purpose | Key columns |
| --- | --- | --- |
| `jobs` | Canonical job record. Global, not per candidate. Manual in Phase 1; Phase 2 source listings will point at it. | `Id`, `EmployerId`, `Title`, `NormalizedTitle`, `CanonicalUrl`, `NormalizedLocation`, `NormalizedRequisitionId`, `Status`, `CreatedVia` |
| `job_status_history` | One timestamped row per job status transition (the first row has no `FromStatus`). | `Id`, `JobId`, `FromStatus`, `ToStatus`, `ChangedAtUtc` |

Delete rules:

| Relationship | On delete | Meaning |
| --- | --- | --- |
| `employers` -> `jobs` | RESTRICT | An employer that has jobs cannot be deleted. |
| `jobs` -> `job_status_history` | CASCADE | Deleting a job removes its history only. |

Tables that will reference a job later (applications, source listings) must use RESTRICT, so a job that has either cannot
be hard-deleted (OQ-P1.6); the API maps that foreign-key failure to `409 job_in_use`.

Concurrency: `jobs` uses PostgreSQL's `xmin` system column as an optimistic concurrency token (no column or migration DDL; it is
only in the EF model), so two concurrent changes to one job cannot both be saved.

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

## Applications (Order 7; decisions OQ-P1.8 and OQ-P1.9, answered 2026-11-18)

Order 7 (migration `20261118040000_AddApplications`) adds two tables, so the model has ten tables in total.

| Table | Purpose | Key columns |
| --- | --- | --- |
| `applications` | One application per candidate and canonical job (never per source listing). | `Id`, `CandidateId`, `JobId`, `Status`, `IsApplied`, `Method`, `AppliedAtUtc`, `AppliedConfirmedAtUtc` |
| `application_audit` | Append-only trail: one row per change (who, when, old value, new value, reason). | `Id`, `ApplicationId`, `Action`, `Actor`, `ChangedAtUtc`, `OldStatus`, `OldIsApplied`, `OldAppliedAtUtc`, `NewStatus`, `NewIsApplied`, `NewAppliedAtUtc`, `Reason` |

Delete rules (OQ-P1.6: a job or candidate with an application cannot be hard-deleted):

| Relationship | On delete | Meaning |
| --- | --- | --- |
| `candidates` -> `applications` | RESTRICT | A candidate with applications cannot be deleted (API: 409 `candidate_has_applications`). |
| `jobs` -> `applications` | RESTRICT | A job with an application cannot be deleted (API: 409 `job_in_use`). |
| `applications` -> `application_audit` | RESTRICT | An application with audit rows cannot be deleted. There is no delete endpoint. |

Uniqueness: the unique index `IX_applications_CandidateId_JobId` is the only application per candidate and job.

| Column | Type | Rule |
| --- | --- | --- |
| `Status` | varchar(30) | `saved`, `applied`, `pendingVerification`, `interview`, `offer`, `rejected`, `withdrawn` (check). A new application is `saved` |
| `IsApplied` | boolean | The confirmed fact that the application was submitted. Only an explicit manual confirmation sets it. Later statuses never clear it; only an audited un-apply does |
| `Method` | varchar(20), nullable | `manual` (check); always `manual` in Phase 1. Set exactly when `IsApplied` |
| `AppliedAtUtc` | timestamptz, nullable | When the user says they applied. Back-dating is allowed; the API rejects a future time. Set exactly when `IsApplied` |
| `AppliedConfirmedAtUtc` | timestamptz, nullable | When the user confirmed it in this tool, stored separately from `AppliedAtUtc`. Set exactly when `IsApplied` |
| `CreatedAtUtc`, `UpdatedAtUtc` | timestamptz | Set by the system |

Check constraints keep the data honest even without the API: `CK_applications_applied_fact_complete` (flag, method and both
times are all set or all null), `CK_applications_status_applied_consistent` (`applied`, `interview` and `offer` require
`IsApplied`; `saved` and `pendingVerification` forbid it; `rejected` and `withdrawn` allow either), `CK_applications_status_values`
and `CK_applications_method_values`. `applications` uses `xmin` as its optimistic concurrency token like `jobs`.

Audit rows: `Action` is `created`, `statusChanged`, `appliedConfirmed`, `unapplied` or `corrected`; `Actor` is `user` (there is no
authentication in Phase 1). `CK_application_audit_reason_required` makes `unapplied` and `corrected` rows carry a non-blank
reason. The first (`created`) row has no old values. A database trigger (`trg_application_audit_append_only`, created in the
migration and not part of the EF model) rejects every UPDATE and DELETE of an audit row with SQLSTATE 23000.

Job status never changes an application (OQ-P1.5). The `applied` status and the `IsApplied` fact are separate: an application
that is `rejected`, `withdrawn`, `interview` or `offer` keeps `IsApplied`, `Method` and `AppliedAtUtc`.

## Role lists and location sets (Order 4; decisions OQ-P1.1 to OQ-P1.3, answered 2026-11-18)

Order 4 (migration `20261118120000_AddRoleListsAndLocationSets`) adds six tables, so the model has sixteen tables in total. This section supersedes the location-set and role-list/alias entries in the deferred-requirements list at the end of this file.

| Table | Purpose | Key columns |
| --- | --- | --- |
| `role_lists` | Named, reusable list of target role titles. | `Id`, `Name`, `NormalizedName` (unique) |
| `role_list_titles` | Primary titles and their aliases, in one table. | `Id`, `RoleListId`, `Title`, `NormalizedTitle`, `PrimaryTitleId` (null for a primary title) |
| `location_sets` | Named, reusable set of locations. | `Id`, `Name`, `NormalizedName` (unique) |
| `location_set_locations` | One location of a set. | `Id`, `LocationSetId`, `Label`, `NormalizedLabel`, `Scope`, `CountryCode` (nullable) |
| `search_profile_role_lists` | Join: role lists a profile selects. | PK (`SearchProfileId`, `RoleListId`) |
| `search_profile_location_sets` | Join: location sets a profile selects. | PK (`SearchProfileId`, `LocationSetId`) |

| ID | Decision | Where it shows |
| --- | --- | --- |
| OQ-P1.1 | An entry is one primary title plus zero or more aliases. Aliases use the name normalizer and are unique within a list; the same title may be in different lists. Phase 1 only stores them; all matching is Phase 3 S3.2. | Unique index `IX_role_list_titles_RoleListId_NormalizedTitle`; `PrimaryTitleId` |
| OQ-P1.2 | A location is a free-text label, a required scope (`city`, `state`, `country`, `remoteCountry`, `remoteWorldwide`) and an optional ISO 3166-1 alpha-2 country code. No built-in place list, nothing hardcoded to one country. Work mode stays separate (`WorkModes`). No regions such as EMEA or APAC. | `Scope`, `CountryCode` and their checks |
| OQ-P1.3 | U9 stays: selecting a role list or location set does not change `ProfileVersion` in Phase 1; the increment rule is S3.1. | No code touches `ProfileVersion` |

Titles and aliases share one namespace per list, so one lookup finds either and an alias can never equal another title or
alias of its list. An alias row points at its primary title through `PrimaryTitleId` (a self reference, `CASCADE`); an alias of an
alias is not possible through the API, and `CK_role_list_titles_not_own_primary` stops a row from being its own primary.

Location checks: `CK_location_set_locations_scope_values` (the five scopes), `CK_location_set_locations_country_code_format`
(two upper-case letters, not checked against a country list), `CK_location_set_locations_scope_country` (`remoteCountry`
needs a code, `remoteWorldwide` has none) and `CK_location_set_locations_label_not_blank`. So "Remote India"
(`remoteCountry` + `IN`) and "Remote worldwide" cannot be confused. Uniqueness inside a set is two partial unique indexes:
(`LocationSetId`, `Scope`, `NormalizedLabel`) where the country is null, and the same plus `CountryCode` where it is not,
so a missing country counts as a value of its own and the same label may repeat with different countries.

| Parent -> child | Rule | Effect |
| --- | --- | --- |
| `role_lists` -> `role_list_titles` | CASCADE | Deleting a list removes its titles and aliases. |
| `role_list_titles` -> `role_list_titles` (`PrimaryTitleId`) | CASCADE | Deleting a primary title removes its aliases. |
| `location_sets` -> `location_set_locations` | CASCADE | Deleting a set removes its locations. |
| `search_profiles` -> `search_profile_role_lists` / `search_profile_location_sets` | CASCADE | Deleting a profile removes its selections only. |
| `role_lists` -> `search_profile_role_lists` | RESTRICT | A role list a profile still selects cannot be deleted (API: 409 `role_list_in_use`). |
| `location_sets` -> `search_profile_location_sets` | RESTRICT | A location set a profile still selects cannot be deleted (API: 409 `location_set_in_use`). |

Assumptions to confirm (not settled by the answers above):

1. A title and an alias also may not be equal within one list (the single namespace), not only two aliases.
2. `remoteWorldwide` forbids a country code and `remoteCountry` requires one.
3. The same label may repeat in one set only with a different scope or country.
4. A list or set that a profile selects cannot be deleted (RESTRICT), as for company lists.

## Deferred requirements (AC7)

Not in this model: location sets, role lists and aliases, employer website/career URL/ATS, source catalog and per-source enablement, automatic `ProfileVersion` increment. Their owner and phase are on the Notion Task Board and Product Requirements; the API is in [api.md](api.md).

## Assumptions to confirm

These were not spelled out in the approval text this work was based on:

1. `CandidateId` (U5) is on `search_profiles` only. Company lists and employers are global and reusable, so their names
   are unique across the database (U2), not per candidate.
2. A list that a profile selects cannot be deleted (RESTRICT) rather than silently removed from the profile.
3. `DefaultWorkModes` on a list is a non-null mask defaulting to the empty set.

## Applying and checking the migration

See "Database schema (migrations)" and "Tests" in [development.md](development.md). To undo on a throw-away database: `dotnet ef database update 0 --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`.
