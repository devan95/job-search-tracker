# Phase 1 management API

Task: *Backend API: candidates, employers, company lists and search profiles.* It exposes the data model in
[data-model.md](data-model.md); the rules there (normalization, uniqueness, delete behaviour) are the rules here. There is
no multiline parser, duplicate preview, frontend, job discovery, employer website/career URL/ATS or
authentication in this task. The API is for the local home network only (see the Architecture page in Notion).

All paths are under `/api`. Bodies are JSON with camelCase names. Ids are GUIDs; a route id that is not a GUID matches no
route (404 without a body).

## Conventions

| Situation | Response |
| --- | --- |
| Created | `201` + `Location` header + the new resource |
| Read / update | `200` + the resource (lists return an array; no envelope) |
| Delete, add or remove an association | `204`, no body |
| Invalid input | `400` problem with `code: "validation"` and `errors` (field name -> messages). Every problem in the request is reported together |
| Unknown id | `404` problem, `code: "not_found"` |
| Duplicate name, or a delete the database restricts | `409` problem with a specific `code` (below) |

Errors are RFC 7807 `application/problem+json` documents (`type`, `title`, `status`, `detail`, plus `code`). A malformed
JSON body is a `400` problem as well. Write requests are validated before anything is saved, so a rejected request changes
nothing. Names are trimmed; uniqueness is decided on the normalized form (NFKC, whitespace runs collapsed, invariant lower
case), so `Acme Corp`, ` ACME   corp` and `ＡＣＭＥ Corp` are one name. A name is at most 200 characters.

List endpoints take `skip` (default 0) and `take` (default 100, at most 500) and return a plain array.

| `code` | Status | Meaning |
| --- | --- | --- |
| `validation` | 400 | Field errors under `errors` |
| `not_found` | 404 | The resource in the URL does not exist |
| `not_a_member` | 404 | Removing an association that does not exist |
| `duplicate_name` | 409 | Same normalized name (employers and lists: globally; profiles: per candidate). Employer conflicts include `existingId` |
| `candidate_has_profiles` | 409 | Delete of a candidate that still has search profiles (`searchProfileCount`) |
| `company_list_in_use` | 409 | Delete of a list that a search profile still selects (`searchProfileCount`) |
| `role_list_in_use` / `location_set_in_use` | 409 | Delete of a role list or location set that a search profile still selects (`searchProfileCount`) |
| `duplicate_title` | 409 | A title or alias that is already a title or alias in the same role list (after normalization) |
| `duplicate_location` | 409 | A location with the same label, scope and country as one already in the same set (after normalization) |
| `employer_in_company_list` | 409 | Delete of an employer that is still in a list (`companyListCount`) |
| `employer_has_jobs` | 409 | Delete of an employer that still has jobs (`jobCount`) |
| `duplicate_requisition` | 409 | Same employer and requisition id as an existing job (`existingId`, `matchedOn`). Cannot be overridden |
| `possible_duplicate` | 409 | Same canonical URL, or same employer + title + location (`existingId`, `matchedOn`). Resend with `confirmDifferentJob: true` if it is a different job |
| `invalid_status_transition` | 409 | The job's current status does not allow the requested status (`currentStatus`, `requestedStatus`) |
| `job_in_use` | 409 | Delete of a job that an application or source listing references |
| `concurrent_modification` | 409 | The job or application was changed or deleted by someone else while the request ran. Reload and retry |
| `candidate_has_applications` | 409 | Delete of a candidate that still has applications (`applicationCount`) |
| `application_exists` | 409 | The candidate already has an application for this job (`existingId`, `currentStatus`, `isApplied`, `appliedAtUtc`) |
| `invalid_application_transition` | 409 | The application's status does not allow the requested status (`currentStatus`, `requestedStatus`) |
| `already_applied` / `not_applied` | 409 | Confirm of an application that is already applied; un-apply of one that is not |
| `invalid_correction` | 409 | The correction would leave an inconsistent state or change nothing |

Work modes are arrays of `remote`, `onSite`, `hybrid` (case-insensitive on input; repeated values count once).

## Endpoints

### Candidates

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /candidates` | `{ "name" }` | 201 |
| `GET /candidates` | | Ordered by name |
| `GET /candidates/{id}` | | |
| `PUT /candidates/{id}` | `{ "name" }` | |
| `DELETE /candidates/{id}` | | 409 `candidate_has_profiles` while profiles exist |

### Employers (canonical, name only)

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /employers` | `{ "name" }` | 201, or 409 `duplicate_name` with `existingId` |
| `POST /employers/resolve` | `{ "name" }` | Find-or-create: 201 when new, 200 with the existing canonical employer otherwise |
| `GET /employers?q=` | | `q` is a normalized substring filter; ordered by normalized name |
| `GET /employers/lookup?name=` | | Exact canonical lookup: 200 or 404 |
| `GET /employers/{id}` | | |
| `DELETE /employers/{id}` | | 409 `employer_in_company_list` while it is in any list |

There is no employer rename or update in Phase 1.

### Company lists

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /company-lists` | `{ "name", "locationLabel"?, "defaultWorkModes"? }` | `defaultWorkModes` is never null: omitted, null or `[]` all store the empty set (no default stated) |
| `GET /company-lists` | | Summaries with `employerCount` |
| `GET /company-lists/{id}` | | Includes `employers`, ordered by name |
| `PUT /company-lists/{id}` | same as create | Replaces the list's own fields (omitted label is cleared, omitted modes are the empty set). Never changes membership |
| `DELETE /company-lists/{id}` | | Removes the list and its employer links; employers stay. 409 `company_list_in_use` while a profile selects it |
| `PUT /company-lists/{id}/employers/{employerId}` | | Add an employer; adding again is a 204 no-op. One employer may be in many lists |
| `DELETE /company-lists/{id}/employers/{employerId}` | | Remove the association only; 404 `not_a_member` if it does not exist |

### Company-list import (paste preview and save)

Task: *Company-list import: multiline parsing and duplicate preview.* The user pastes one company per line, checks the
preview, and then confirms the save. Both endpoints take the same body, parse it with the same code, and need an existing
list (`404 not_found` otherwise). Nothing here changes the normalization rule in [data-model.md](data-model.md).

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /company-lists/{id}/import/preview` | `{ "text" }` | `200`. Writes nothing: no employer and no list membership is created |
| `POST /company-lists/{id}/import` | `{ "text" }` | The confirmed save. `200` with the counts below; `400` and nothing saved when any row is invalid |

**Parsing rules** (pure code, no database):

- A line ends at CRLF, LF or CR, and nowhere else. **Commas are never delimiters**: `Acme, Inc.` is one company.
- Every line is trimmed. Blank and whitespace-only lines are ignored, but they still count in `rowNumber` (1-based line number in the pasted text).
- `name` is the trimmed line (what the user typed); `normalizedName` is the canonical form (NFKC, whitespace runs collapsed, invariant lower case), so `Acme Corp`, `ACME   corp` and `ＡＣＭＥ Corp` are one company.
- **Limit: at most 1,000 non-blank rows per request.** More is `400 validation` on `text`. A name is at most 200 characters (also after normalization).
- A missing `text`, or a text without any non-blank line, is `400 validation` on `text`.

**Preview response** (`200`):

```
{ "rows": [ { "rowNumber", "input", "name", "normalizedName", "status",
              "existingEmployerId", "duplicateOfRow", "message" } ],
  "summary": { "total", "new", "existingEmployer", "alreadyInList", "duplicateInInput", "invalid" } }
```

`rows` has one entry per non-blank line, in input order. `input` is the line as pasted. `name` and `normalizedName` are
`null` for an invalid row. `existingEmployerId` is set for `existing_employer` and `already_in_list`; `duplicateOfRow` is set
for `duplicate_in_input`. `message` is a short human-readable explanation of what the save will do with the row. `summary`
counts rows per status; the five counts add up to `total`.

| `status` | Meaning | What the save does |
| --- | --- | --- |
| `new` | No canonical employer has this normalized name | Creates the employer and adds it to the list |
| `existing_employer` | The employer exists but is not in this list | Reuses the employer and adds it to the list |
| `already_in_list` | The employer is already in this list | Skips the row |
| `duplicate_in_input` | An earlier row has the same normalized name (`duplicateOfRow` is the first such row) | Skips the row |
| `invalid` | The row cannot be used (for example longer than 200 characters); `message` says why | Blocks the whole save |

**Save response** (`200`): `{ "created", "reused", "addedToList", "skipped" }`.

- `created`: new canonical employers created (and added to the list).
- `reused`: existing canonical employers matched by normalized name that were added to the list (`existing_employer` rows).
- `addedToList`: list memberships added (`created + reused`).
- `skipped`: rows that changed nothing (`already_in_list` and `duplicate_in_input`).
- `created + reused + skipped` equals the number of non-blank rows.

The save is stateless: it parses the text again, so it cannot disagree with a preview of the same text, and a row's status is
decided at save time against the current database. If any row is `invalid`, the answer is `400 validation` with one message
per invalid row under `errors.text` (`Row 6: ...`) and nothing is saved. Otherwise everything is saved in one transaction.
Missing employers are created with the same race-safe find-or-create as `POST /employers/resolve`; no duplicate employer and
no duplicate membership can result. Running the same import twice changes nothing the second time (`created`, `reused` and
`addedToList` are `0`). Employers are never deleted and other lists are never touched.

```
POST /api/company-lists/2a9e.../import/preview
{ "text": "Acme\n  GLOBEX \nInitech, Inc.\n\ninitech, inc." }
-> 200 { "rows": [
           { "rowNumber": 1, "input": "Acme", "name": "Acme", "normalizedName": "acme", "status": "already_in_list",
             "existingEmployerId": "b1d2...", "duplicateOfRow": null, "message": "Already in this list; it will be skipped." },
           { "rowNumber": 2, "input": "  GLOBEX ", "name": "GLOBEX", "normalizedName": "globex", "status": "existing_employer",
             "existingEmployerId": "c7e8...", "duplicateOfRow": null, "message": "The company already exists; it will be reused and added to the list." },
           { "rowNumber": 3, "input": "Initech, Inc.", "name": "Initech, Inc.", "normalizedName": "initech, inc.", "status": "new",
             "existingEmployerId": null, "duplicateOfRow": null, "message": "New company; it will be created and added to the list." },
           { "rowNumber": 5, "input": "initech, inc.", "name": "initech, inc.", "normalizedName": "initech, inc.", "status": "duplicate_in_input",
             "existingEmployerId": null, "duplicateOfRow": 3, "message": "Same company as row 3; it will be skipped." } ],
         "summary": { "total": 4, "new": 1, "existingEmployer": 1, "alreadyInList": 1, "duplicateInInput": 1, "invalid": 0 } }

POST /api/company-lists/2a9e.../import
{ "text": "Acme\n  GLOBEX \nInitech, Inc.\n\ninitech, inc." }
-> 200 { "created": 1, "reused": 1, "addedToList": 2, "skipped": 2 }
```

### Role lists (Order 4; decision OQ-P1.1)

Named, reusable lists of target role titles. Nothing is hardcoded: any title in any language is accepted. A list has primary
**titles**, and each title has zero or more **aliases** (equivalent titles). Titles and aliases are normalized with the
name normalizer and are unique within one list, so an alias can never equal another title or alias of the same list; the same
title may be in different lists. Phase 1 only stores them: how a job title is matched against a list is Phase 3 (S3.2).

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /role-lists` | `{ "name" }` | 201. 409 `duplicate_name` |
| `GET /role-lists?skip=&take=` | | Summaries (`titleCount`, `aliasCount`) in name order, paged (default 100, max 500) |
| `GET /role-lists/{id}` | | Includes `titles` (name order), each with its `aliases` |
| `PUT /role-lists/{id}` | `{ "name" }` | Renames only; never changes titles |
| `DELETE /role-lists/{id}` | | Removes the list with its titles and aliases. 409 `role_list_in_use` while a profile selects it |
| `POST /role-lists/{id}/titles` | `{ "title", "aliases"? }` | 201 with the title and its aliases. 400 for a blank value, an alias equal to its own title, or more than 50 aliases. 409 `duplicate_title` when any value is already in the list; nothing is saved then. A repeated alias in one request is kept once |
| `PUT /role-lists/{id}/titles/{titleId}` | `{ "title", "aliases"? }` | Replaces the title and its whole alias set (omitted `aliases` = none) in one transaction. The title's own current values do not clash with themselves. 404 for an alias id |
| `DELETE /role-lists/{id}/titles/{titleId}` | | Removes the title and its aliases; the list stays |

### Location sets (Order 4; decision OQ-P1.2)

Named, reusable sets of locations. A location has a free-text `label`, a required `scope` and an optional `countryCode`
(ISO 3166-1 alpha-2, upper-cased on save, checked for shape only: there is no built-in place list). Scopes: `city`, `state`,
`country`, `remoteCountry` (remote within a country; `countryCode` required) and `remoteWorldwide` (`countryCode` must be
empty). "Remote India" is `remoteCountry` + `IN`; "Remote worldwide" is `remoteWorldwide`, so they never match each other.
Work arrangement (`remote`, `onSite`, `hybrid`) is a different field and is not part of a location. `scope` is case-insensitive
on input. A set cannot hold the same label (after normalization) with the same scope and country twice; the same label with
another country or scope is allowed. A multiline paste is a client feature: the client sends one `POST` per line with the
scope chosen for the whole paste, and each location is then edited on its own.

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /location-sets` | `{ "name" }` | 201. 409 `duplicate_name` |
| `GET /location-sets?skip=&take=` | | Summaries (`locationCount`) in name order, paged |
| `GET /location-sets/{id}` | | Includes `locations` (`id`, `label`, `scope`, `countryCode`) ordered by label |
| `PUT /location-sets/{id}` | `{ "name" }` | Renames only |
| `DELETE /location-sets/{id}` | | Removes the set with its locations. 409 `location_set_in_use` while a profile selects it |
| `POST /location-sets/{id}/locations` | `{ "label", "scope", "countryCode"? }` | 201. 400 for a missing or unknown scope, a bad code, no code for `remoteCountry` or a code for `remoteWorldwide`. 409 `duplicate_location` |
| `PUT /location-sets/{id}/locations/{locationId}` | same as create | Replaces label, scope and country |
| `DELETE /location-sets/{id}/locations/{locationId}` | | |

### Search profiles

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /search-profiles` | `{ "candidateId", "name", "companyListIds"?, "roleListIds"?, "locationSetIds"?, "workModes"?, "companyMonitoringEnabled"?, "broadSearchEnabled"? }` | Unknown candidate, list or set ids are 400 field errors. Omitted flags are false; both may be false (a draft) |
| `GET /search-profiles?candidateId=` | | Optional candidate filter; each item includes its selected `companyLists`, `roleLists` and `locationSets` (`id`, `name`, in name order) |
| `GET /search-profiles/{id}` | | |
| `PUT /search-profiles/{id}` | `{ "name", "workModes"?, "companyMonitoringEnabled", "broadSearchEnabled" }` | Both flags required. Omitted or null `workModes` = inherit; `[]` = explicit empty set. Never changes selected lists or `candidateId` |
| `DELETE /search-profiles/{id}` | | Removes the profile and its list selections; candidate and lists stay |
| `PUT /search-profiles/{id}/company-lists/{companyListId}` | | Select a list; selecting again is a 204 no-op |
| `DELETE /search-profiles/{id}/company-lists/{companyListId}` | | Unselect; 404 `not_a_member` if it was not selected |
| `PUT /search-profiles/{id}/role-lists/{roleListId}` | | Select a role list; selecting again is a 204 no-op |
| `DELETE /search-profiles/{id}/role-lists/{roleListId}` | | Unselect; 404 `not_a_member` if it was not selected |
| `PUT /search-profiles/{id}/location-sets/{locationSetId}` | | Select a location set; selecting again is a 204 no-op |
| `DELETE /search-profiles/{id}/location-sets/{locationSetId}` | | Unselect; 404 `not_a_member` if it was not selected |

`workModes` on a profile: `null` means "inherit each selected list's `defaultWorkModes`"; any array (including `[]`) overrides
the defaults of every selected list. Company lists, role lists and location sets are selected and unselected independently of
each other, and a profile may select any number of each. `profileVersion` starts at 1 and is returned as stored; nothing
increments it in Phase 1, including selecting or unselecting a role list or location set (U9, OQ-P1.3; the increment rule is
S3.1). There is no source catalog or per-source enablement.

### Jobs

Jobs are global and entered manually in Phase 1 (no source, Gmail or AI). The employer is a typed name resolved like
`POST /employers/resolve`: an existing employer is reused, otherwise one is created in the same save as the job, so a rejected
job never leaves an employer behind.

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /jobs` | job body (below) | 201 with the full job; status starts `open` with one history row. 409 on a duplicate |
| `GET /jobs?q=&employerId=&status=&workMode=&employmentType=&skip=&take=` | | Summaries, newest first. `q` is a normalized substring of the title or employer name. Filters are case-insensitive on input; unknown values are 400 |
| `GET /jobs/{id}` | | Full job including `statusHistory` (oldest first) |
| `PUT /jobs/{id}` | job body | Replaces every field: an omitted optional field is cleared. Never changes status, `createdVia` or `createdAtUtc` |
| `POST /jobs/{id}/status` | `{ "status" }` | Timestamped transition, see below. Returns the full job |
| `DELETE /jobs/{id}` | | 204. 409 `job_in_use` when an application or source listing references it |

Job body: `employer` and `title` are required; optional: `applyUrl`, `location`, `workMode` (`remote`, `onSite`, `hybrid`),
`employmentType` (`fullTime`, `partTime`, `contract`, `internship`, `temporary`, `other`), `department`, `experienceMinYears`,
`experienceMaxYears` (whole numbers 0 to 60, min <= max), `requisitionId`, `postedDate` (`yyyy-MM-dd`), `description` (plain
text), `foundOn`, and `confirmDifferentJob`. Blank optional text is stored as unset. Limits: title and location 200 characters
(after normalization), department and found on 200, requisition id 100, URL 2000, description 50 000. A `status` in the body is
ignored. Responses add `id`, `createdVia` (`manual`), `createdAtUtc`, `updatedAtUtc`, `status`, `canonicalUrl` and the employer
as `{ id, name }`.

**Status.** A user may request `closed`, `expired`, `unavailable` or `reopened` ("open again"). `reopened` is allowed only from
`closed`, `expired` or `unavailable`; the other three are allowed from `open`, `reopened` or one of the other two, but not to the
same status. Anything else is 409 `invalid_status_transition`. `new` and `verificationPending` are system-only (400 when requested,
and a job in either cannot be changed by the user). A job status never changes an application's status.

**Duplicates (warn and confirm).** Checked on create, and on update only for what the update changes. Changing or clearing the
requisition id also re-checks the URL and the title + location rules, because it changes which jobs they can match.

1. Same employer + same requisition id (normalized): 409 `duplicate_requisition`, never overridable.
2. Different requisition ids never match, whatever the title, location or URL are.
3. Same canonical URL, or same employer + normalized title + normalized location: 409 `possible_duplicate` with `existingId` and
   `matchedOn` (`canonicalUrl` or `employerTitleLocation`). Resend with `"confirmDifferentJob": true` to create or save it anyway.

**URL canonicalization** (reused by Phase 2 S2.8). Only absolute http or https URLs without a user name or password are accepted.
The canonical form lower-cases the host (punycode for international names), drops a leading `www.`, the fragment, a default port
and a trailing slash, keeps the path case and the scheme, removes tracking parameters and sorts the remaining parameters.
Tracking parameters (names are case-insensitive): `utm_*`, `fbclid`, `gclid`, `dclid`, `msclkid`, `twclid`, `yclid`, `mc_cid`,
`mc_eid`, `_hsenc`, `_hsmi`, `trk`, `trackingId`, `refId`, `gh_src`, `lever-source`, `lever-origin`. Job-identifying parameters such as
`gh_jid` and `currentJobId` are kept. `ApplyUrl` is stored as typed; `canonicalUrl` is returned for reference.

**Delete.** A job with no application and no source listing is hard-deleted with its status history; the employer stays. Closed
or expired is the archive for jobs that have an application. `DELETE /employers/{id}` also returns 409 `employer_has_jobs`
(`jobCount`) while the employer has jobs.

**Concurrency.** Status changes, updates and deletes are version-checked (PostgreSQL `xmin`): of two concurrent changes to the
same job only one is saved and the other gets 409 `concurrent_modification`. `GET` and the response of a change read the job
and its status history from one snapshot.

### Applications

Manual application tracking (Order 7; rules OQ-P1.8 and OQ-P1.9). There is one application per candidate and canonical job,
whatever source the job came from. The user alone says that an application was submitted: creating, saving or preparing an
application never marks it applied. Phase 1 has no authentication, so every audit row's `actor` is `user`. There is no
endpoint to delete an application or edit its audit trail.

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /applications` | `{ "candidateId", "jobId" }` | 201 with a `saved` application that is not applied. Unknown ids are 400 field errors. 409 `application_exists` (with `existingId`, `currentStatus`, `isApplied`, `appliedAtUtc`; not `status`, which is the HTTP status) when the candidate already has one for the job |
| `GET /applications?candidateId=&jobId=&status=&applied=&skip=&take=` | | Summaries (no audit trail), most recently updated first. Use `candidateId` + `jobId` to look up the existing application, for example for a repost warning |
| `GET /applications/{id}` | | The application with its `auditTrail`, oldest first |
| `POST /applications/{id}/status` | `{ "status", "reason"? }` | Ordinary status change, see below |
| `POST /applications/{id}/confirm-applied` | `{ "confirmed": true, "appliedAtUtc"? }` | The explicit manual confirmation. `confirmed` must be `true`. `appliedAtUtc` defaults to now, may be earlier, and is 400 when in the future |
| `POST /applications/{id}/unapply` | `{ "reason" }` | Removes the applied fact. Reason required (at most 500 characters) |
| `POST /applications/{id}/correct` | `{ "status"?, "appliedAtUtc"?, "reason" }` | Audited correction. Reason required; at least one of `status` and `appliedAtUtc` |

Responses carry `id`, `candidateId`, `job` (`id`, `title`, `employer`, `status`), `status`, `isApplied`, `method`, `appliedAtUtc`,
`appliedConfirmedAtUtc`, `createdAtUtc`, `updatedAtUtc` and, on a single application, `auditTrail`. Each audit item has `action`,
`actor`, `changedAtUtc`, `oldStatus`, `oldIsApplied`, `oldAppliedAtUtc`, `newStatus`, `newIsApplied`, `newAppliedAtUtc` and `reason`.
Statuses: `saved`, `applied`, `pendingVerification`, `interview`, `offer`, `rejected`, `withdrawn`.

**Status endpoint.** Accepts `pendingVerification`, `interview`, `offer`, `rejected` and `withdrawn` (`saved` and `applied` are 400:
`applied` comes only from confirming, `saved` only from un-applying or a correction). Allowed moves: `pendingVerification` from
`saved`; `interview` from `applied`; `offer` from `applied` or `interview`; `rejected` from `pendingVerification`, `applied`,
`interview` or `offer`; `withdrawn` from any status except `withdrawn`. Anything else is 409 `invalid_application_transition`
(`currentStatus`, `requestedStatus`). Rejected and withdrawn applications are reopened only by a correction.

**Confirm applied.** Allowed while the application is not applied (`saved`, `pendingVerification`, or `rejected`/`withdrawn` that
were never confirmed). `saved` and `pendingVerification` become `applied`; `rejected` and `withdrawn` keep their status. It stores
`method: "manual"`, the time applied and, separately, the time of confirmation. An uncertain submission stays `pendingVerification`
(not applied). Already applied is 409 `already_applied`.

**Un-apply.** Clears the flag, method and both times. `applied`, `interview` and `offer` go back to `saved`; `rejected` and
`withdrawn` keep their status. The old values stay in the audit row. Not applied is 409 `not_applied`.

**Correct.** The new state must still make sense: `applied`, `interview` and `offer` need a confirmed application, `saved` and
`pendingVerification` are not allowed on an applied one, and `appliedAtUtc` can only be corrected while applied (never into the
future). Anything else, or a correction that changes nothing, is 409 `invalid_correction`. A rejected or withdrawn application is
reopened by correcting its status.

**Audit.** Every create, status change, confirmation, un-apply and correction appends one audit row in the same transaction as the
change. Concurrent changes to one application are version-checked (`xmin`): the loser gets 409 `concurrent_modification`.

Related conflict codes: 409 `candidate_has_applications` (`applicationCount`) on `DELETE /candidates/{id}`, and the existing 409
`job_in_use` on `DELETE /jobs/{id}` now applies when an application exists.

### Source listings, scan runs and outcomes (Phase 2 S2.1 + S2.2)

No endpoint is added: these tables are written by the source common path (`SourceScanService`, see
[development.md](development.md#source-adapter-contract-phase-2-s22)) and read by later stories (S2.4 board test, S2.7 run-now,
S2.10 source status). The only visible change is that `DELETE /jobs/{id}` now also returns 409 `job_in_use` when a job has a
source listing (the foreign key is RESTRICT, the same mapping as for applications). Outcome values on the wire, when later
stories expose them, are `success`, `partial`, `failed`, `unsupported` and `notChecked`.

## Examples

```
POST /api/candidates
{ "name": "Asha Rao" }
-> 201 { "id": "6f0c...", "name": "Asha Rao" }

POST /api/employers/resolve
{ "name": "  ACME   Corp " }
-> 201 { "id": "b1d2...", "name": "ACME   Corp", "normalizedName": "acme corp" }

POST /api/company-lists
{ "name": "Ahmedabad product companies", "locationLabel": "Ahmedabad", "defaultWorkModes": ["hybrid", "onSite"] }
-> 201 { "id": "2a9e...", "name": "Ahmedabad product companies", "locationLabel": "Ahmedabad",
         "defaultWorkModes": ["onSite", "hybrid"], "employerCount": 0, "employers": [] }

PUT /api/company-lists/2a9e.../employers/b1d2...
-> 204

POST /api/search-profiles
{ "candidateId": "6f0c...", "name": "Backend roles", "companyListIds": ["2a9e..."], "companyMonitoringEnabled": true }
-> 201 { "id": "c3f4...", "candidateId": "6f0c...", "name": "Backend roles", "workModes": null,
         "companyMonitoringEnabled": true, "broadSearchEnabled": false, "profileVersion": 1,
         "companyLists": [ { "id": "2a9e...", "name": "Ahmedabad product companies", "defaultWorkModes": ["onSite", "hybrid"] } ] }

POST /api/jobs
{ "employer": "ACME Corp", "title": "Backend Engineer", "applyUrl": "https://acme.com/jobs/7?utm_source=mail", "location": "Pune; Remote India", "workMode": "hybrid" }
-> 201 { "id": "d4a1...", "employer": { "id": "b1d2...", "name": "ACME   Corp" }, "title": "Backend Engineer",
         "canonicalUrl": "https://acme.com/jobs/7", "status": "open", "createdVia": "manual",
         "statusHistory": [ { "fromStatus": null, "toStatus": "open", "changedAtUtc": "..." } ], ... }

POST /api/jobs   (same employer, title and location again)
-> 409 { "code": "possible_duplicate", "existingId": "d4a1...", "matchedOn": "employerTitleLocation", ... }

DELETE /api/company-lists/2a9e...
-> 409 { "title": "Company list is used by a search profile", "status": 409,
         "code": "company_list_in_use", "searchProfileCount": 1, ... }

POST /api/company-lists
{ "name": " ", "defaultWorkModes": ["moon base"] }
-> 400 { "status": 400, "code": "validation",
         "errors": { "name": ["A name is required."], "defaultWorkModes": ["Unknown work mode 'moon base'. Allowed values: remote, onSite, hybrid."] } }
```

## Tests

The API tests are database tests; see [development.md](development.md) ("Tests") for the required environment.
