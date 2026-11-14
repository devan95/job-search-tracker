# Handoff: Phase 1 Backend API (candidates, employers, company lists, search profiles)

**Date:** 2026-11-14
**Task:** Task Board "Backend API: candidates, employers, company lists and search profiles" (Phase 1). Status: In progress; code written, NOT built or run.
**Intended branch:** `feature/phase1-backend-api`, to be created by you from your local data-model branch or from `main` once the data-model change is merged. The AI had no Git access and made no commit, push or branch.
**Base:** the repository ZIP you supplied on 2026-11-14 and declared the latest code. It has no `.git` and no `.github/` folder, so the base commit, the data-model PR and merge status, and the CI workflow were NOT verified. Do not mark the data-model task Done on the strength of this ZIP.

## Changed (compared with the supplied ZIP)
| Area | Files |
| --- | --- |
| API | `src/JobSearchTracker.Api/Management/` (new): `CandidateEndpoints`, `EmployerEndpoints`, `CompanyListEndpoints`, `SearchProfileEndpoints`, `Contracts`, `Input`, `ApiResults` |
| Wiring | `Program.cs`: maps the four endpoint groups; adds ProblemDetails, exception handler, status-code pages, and `ThrowOnBadRequest = false` so malformed requests are always 400 problems |
| Model | `Data/Entities.cs`: `Candidate.Rename` only. No schema change, no migration |
| Tests | `tests/JobSearchTracker.Api.Tests/`: `ApiTestBase`, `CandidateApiTests`, `EmployerApiTests`, `CompanyListApiTests`, `SearchProfileApiTests` (54 test methods, all `Category=Database`) |
| Docs | `docs/api.md` (new: endpoints, error codes, examples), `docs/data-model.md` (deferred-requirements row), `docs/development.md` (link) |

## Checks (actual results)
| Check | Result |
| --- | --- |
| C# syntax parse of every new or changed file (tree-sitter) | PASS (syntax only; not a compile) |
| `dotnet build`, `dotnet test`, `dotnet ef has-pending-model-changes` | NOT RUN: no .NET SDK, and the AI workspace cannot reach NuGet to restore packages |
| Frontend lint/test/build, Docker, GitHub Actions | NOT RUN (frontend untouched) |

The code has never been compiled. Expect that a first build might report small compile errors; fix them rather than redesign.

## Decisions made inside the approved scope (confirm or change)
1. `POST /employers` returns 409 on a duplicate normalized name; `POST /employers/resolve` is the find-or-create. `GET /employers/lookup?name=` is the exact canonical lookup.
2. Membership and list selection change only through the association endpoints (`PUT`/`DELETE .../employers/{id}`, `.../company-lists/{id}`); `PUT` on a list or profile never changes them. Adding twice is a 204 no-op; removing a non-member is 404 `not_a_member`.
3. Unknown candidate or list ids in a profile create body are 400 field errors, not 404.
4. Work modes cross the API as arrays (`remote`, `onSite`, `hybrid`). Profile `workModes`: omitted or null = inherit, `[]` = explicit empty set. List `defaultWorkModes` is never null (omitted = empty set).
5. Profile `PUT` requires both discovery flags (so an omitted flag cannot silently turn a mode off). `profileVersion` is returned and never incremented.
6. There is no employer rename/update (not in the task scope). List endpoints use `skip`/`take` (default 100, max 500) and return plain arrays.
7. Added `UseExceptionHandler`/`UseStatusCodePages`/`AddProblemDetails` globally. This also affects the health endpoints' error bodies only for unexpected failures.

## Your next steps
1. Back up or check your local working tree.
2. Create `feature/phase1-backend-api`, extract this ZIP over the working tree keeping `.git` (the ZIP has no `.git/` and no `.github/`, so CI configuration is untouched).
3. `dotnet tool restore`, start PostgreSQL, set `JOBSEARCH_TEST_ADMIN_CONNECTION`, then `dotnet build JobSearchTracker.slnx` and `dotnet test JobSearchTracker.slnx`. Report any compile or test failure to the AI.
4. Review `git status` and `git diff`, commit and push yourself, and check the CI run.
5. After pushing, upload this ZIP to the Notion Project Hub as the latest snapshot.
