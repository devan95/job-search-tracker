# Phase 1 management API

Task: *Backend API: candidates, employers, company lists and search profiles.* It exposes the data model in
[data-model.md](data-model.md); the rules there (normalization, uniqueness, delete behaviour) are the rules here. There is
no multiline parser, duplicate preview, frontend, job discovery, role/location sets, employer website/career URL/ATS or
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
| `employer_in_company_list` | 409 | Delete of an employer that is still in a list (`companyListCount`) |
| `employer_has_jobs` | 409 | Delete of an employer that still has jobs (`jobCount`) |
| `duplicate_requisition` | 409 | Same employer and requisition id as an existing job (`existingId`, `matchedOn`). Cannot be overridden |
| `possible_duplicate` | 409 | Same canonical URL, or same employer + title + location (`existingId`, `matchedOn`). Resend with `confirmDifferentJob: true` if it is a different job |
| `invalid_status_transition` | 409 | The job's current status does not allow the requested status (`currentStatus`, `requestedStatus`) |
| `job_in_use` | 409 | Delete of a job that an application or source listing references (none exist yet) |
| `concurrent_modification` | 409 | The job was changed or deleted by someone else while the request ran. Reload and retry |

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

### Search profiles

| Method and path | Body | Notes |
| --- | --- | --- |
| `POST /search-profiles` | `{ "candidateId", "name", "companyListIds"?, "workModes"?, "companyMonitoringEnabled"?, "broadSearchEnabled"? }` | Unknown candidate or list ids are 400 field errors. Omitted flags are false; both may be false (a draft) |
| `GET /search-profiles?candidateId=` | | Optional candidate filter; each item includes its selected lists |
| `GET /search-profiles/{id}` | | |
| `PUT /search-profiles/{id}` | `{ "name", "workModes"?, "companyMonitoringEnabled", "broadSearchEnabled" }` | Both flags required. Omitted or null `workModes` = inherit; `[]` = explicit empty set. Never changes selected lists or `candidateId` |
| `DELETE /search-profiles/{id}` | | Removes the profile and its list selections; candidate and lists stay |
| `PUT /search-profiles/{id}/company-lists/{companyListId}` | | Select a list; selecting again is a 204 no-op |
| `DELETE /search-profiles/{id}/company-lists/{companyListId}` | | Unselect; 404 `not_a_member` if it was not selected |

`workModes` on a profile: `null` means "inherit each selected list's `defaultWorkModes`"; any array (including `[]`) overrides
the defaults of every selected list. `profileVersion` starts at 1 and is returned as stored; nothing increments it in Phase 1.
There is no source catalog or per-source enablement.

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
