# Handoff: restore CI workflow, refresh docs (no application code changed)

**Date:** 2026-11-14
**Task:** Restore `.github/workflows/ci.yml` (deleted in commit `b1b8db6`, "Phase1. Part2") and refresh stale repository docs.
**Base:** `main` @ `b1b8db6`, read via the GitHub connector. Nothing was committed or pushed by the AI.

## Why CI went missing
`ci.yml` existed through `e350bb6` and was the only file deleted by `b1b8db6`. That commit's handoff says the ZIP it was built
from had no `.github/` folder, and the ZIP it produced had none either. Extracting a ZIP does not delete files, so the local
replace step most likely removed files that were absent from the ZIP (the same happened to the early scaffold files in
`8211cab`). This is an inference; the Part 2 ZIP and the local steps were not visible to the AI.
**Consequence:** no CI ran for `b1b8db6`, so the Backend API code (54 database tests) has never run in CI.

## Changed in this ZIP (compared with `main` @ `b1b8db6`)
| File | Change |
| --- | --- |
| `.github/workflows/ci.yml` | **Restored**, byte-identical to the version in `e350bb6` (and `089d01b`). Not edited. |
| `.github/pull_request_template.md` | New: checks, safety and "no unexpected deleted files" checklist |
| `README.md` | Rewritten: states what exists today (was still "Phase 0, no entities, not built yet"); layout now lists `.github/`, `.config/`, docs |
| `CLAUDE.md` | Commands now include `dotnet tool restore`, database tests and the EF model check; new "ZIP delivery checklist" |
| `docs/phase0-feasibility.md` | One cell: criterion 2 now cites commit `9dfee0a` instead of "the change that adds this section" |
| `HANDOFF.md` | Replaced with this note |

**Deleted files: none.** No source, test, migration, Docker, `.gitignore` or web changes.

## Checks
| Check | Result |
| --- | --- |
| ZIP tree compared with `b1b8db6`: only the files above differ; no tracked file missing | PASS (AI, file comparison) |
| `ci.yml` byte-identical to `e350bb6` | PASS (AI, `cmp`) |
| `dotnet build`, `dotnet test`, frontend checks, Docker, GitHub Actions | NOT RUN (docs/config only; no .NET SDK or Docker in the AI workspace) |

## Review notes (not changed, no compiler available)
- `WorkModesLimits.AllModes` in `Data/Entities.cs` is unused; the check constraints in `AppDbContext.cs` hard-code `7`.
  Either use it or delete it in a later change that can be compiled.
- Suggestions for later, not applied: Dependabot for NuGet/npm/Actions/Docker, and CI niceties (timeouts, concurrency,
  pinning action versions). `ci.yml` was deliberately left untouched.

## Your next steps
1. Back up or check `C:\Users\Devan Shah\Repo\job-search-tracker` and make sure your clone is on `main` @ `b1b8db6` (or newer).
2. Extract this ZIP over the folder, keeping `.git`. **Do not delete files first.**
3. Run `git status`. Expect: new `.github/workflows/ci.yml` and `.github/pull_request_template.md`; modified `README.md`,
   `CLAUDE.md`, `HANDOFF.md`, `docs/phase0-feasibility.md`; **no deleted (`D`) files**. Stop and ask if you see any `D`.
4. Prefer a branch and pull request so CI runs before merge. Commit and push yourself.
5. Check the CI run. It will be the first CI run for the Backend API code, so it may report compile or test failures.
   Paste the log to the AI if it fails.
6. Upload this ZIP to the Notion Project Hub as the latest snapshot after pushing.
