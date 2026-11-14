# Handoff: Phase 1 data model (candidate, company list, search profile) - UNBUILT, see Checks

**Date:** 2026-11-14
**Task:** Task Board "Data model: candidate, company list, search profile, with first EF Core migration" (Phase 1).
**Base:** `main` @ `9dfee0a` ("Close Phase 0"), read from GitHub. Work is on local branch `feature/phase1-data-model`. Nothing was committed or pushed by the AI.
**Approvals used:** U1-U9, AC1-AC9 clarifications, package and CI approvals, as given by the user in chat on 2026-11-14 (also recorded on the Task Board item).

## Changed in this ZIP
| Area | Files |
| --- | --- |
| Model | `src/JobSearchTracker.Api/Data/Entities.cs`, `NameNormalizer.cs`, `AppDbContext.cs` |
| Migration | `Data/Migrations/20261114120000_InitialDataModel.cs`, `.Designer.cs`, `AppDbContextModelSnapshot.cs` - **written by hand** (no .NET SDK in the AI workspace) |
| Packages | `Microsoft.EntityFrameworkCore.Design` 10.0.0 in the API project; `.config/dotnet-tools.json` pins `dotnet-ef` 10.0.0 |
| Tests | `tests/JobSearchTracker.Api.Tests/`: `NameNormalizerTests`, `SchemaTests`, `DataModelBehaviourTests`, `MigrationTests`, helpers `TestDatabase`, `DatabaseTestBase` |
| CI | `.github/workflows/ci.yml`: PostgreSQL 17 service, `dotnet tool restore`, `has-pending-model-changes` step |
| Docs | `docs/data-model.md` (decisions, normalization rule, deletes, deferred requirements, assumptions), `docs/development.md` (database tests) |

## Checks (actual results)
| Check | Result |
| --- | --- |
| `dotnet build`, `dotnet test` (unit and database tests), `dotnet ef migrations has-pending-model-changes` | **NOT RUN** - no .NET SDK in the AI workspace. The C# has never been compiled. |
| Hand-written SQL mirror of the migration on PostgreSQL 16 (not 17, not the EF migration) | PASS for: 6 tables; unique names per rule; NULL vs 0 work modes; version 0 and work mode 8 rejected; candidate/employer/list RESTRICT; profile and list CASCADE to join rows only |
| Frontend lint/test/build, Docker, GitHub Actions on this branch | NOT RUN |

## Risks to check first
1. The migration, its Designer file and the model snapshot were written by hand. If CI's `has-pending-model-changes` step fails or the build fails, delete `src/JobSearchTracker.Api/Data/Migrations/` and run `dotnet ef migrations add InitialDataModel --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`, then re-run the tests. (Rename the migration id in `SchemaTests` if it changes.)
2. Database tests fail, not skip, without `JOBSEARCH_TEST_ADMIN_CONNECTION` (see `docs/development.md`).
3. Assumptions to confirm are listed in `docs/data-model.md`: `CandidateId` only on profiles; a list selected by a profile cannot be deleted; list `DefaultWorkModes` is non-null.
4. AC6 and AC9 were approved but their text was not available; they are not covered explicitly.

## Your next steps
1. Back up or check your local working tree.
2. Extract this ZIP over it, keeping `.git`.
3. Run `dotnet tool restore`, start PostgreSQL, set `JOBSEARCH_TEST_ADMIN_CONNECTION`, then `dotnet build JobSearchTracker.slnx` and `dotnet test JobSearchTracker.slnx`.
4. Review `git status` and `git diff`, commit and push to a feature branch yourself, and check the CI run.
5. Upload this ZIP to the Notion Project Hub as the latest snapshot after pushing.
