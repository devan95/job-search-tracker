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
