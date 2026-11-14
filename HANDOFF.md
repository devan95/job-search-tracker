# Handoff: fix backend CI (missing Api/Data folder)

**Date:** 2026-11-14
**Task:** Phase 0 - make the GitHub Actions backend job pass. Task Board: "First GitHub Actions CI run is green".
**Base:** `main` @ `45686e9` ("Push changes"), read via the GitHub connector. Nothing committed or pushed by the AI.

## Problem
CI run #4: backend FAIL (CS0234 `JobSearchTracker.Api.Data` not found, CS0246 `AppDbContext` not found).
Frontend PASS (7 tests), secret scan PASS.

## Cause
`.gitignore` contained an unanchored `data/` rule. With `core.ignorecase=true` (Git for Windows default) it also matches
`src/JobSearchTracker.Api/Data/`, so `Data/AppDbContext.cs` was never committed. Local builds passed because the file
existed on disk. Reproduced in the AI workspace with `git -c core.ignorecase=true check-ignore -v` (ignored before the fix,
not ignored after).

## Changes in this ZIP
| Area | Change |
| --- | --- |
| `.gitignore` | `data/` -> `/data/` (repo-root only), with an explanatory comment |
| `src/JobSearchTracker.Api/Data/AppDbContext.cs` | Added. Minimal empty context: `AppDbContext(DbContextOptions<AppDbContext>) : DbContext`. **Compare with your local copy before overwriting** (see step 2) |
| Repo root | 11 empty stray files are removed from this tree (`CACHED`, `[api`, `[api]`, `[internal]`, `[worker`, `[worker]`, `cd`, `copy`, `dir`, `docker`, `notepad`) |
| `HANDOFF.md` | Replaced with this note |

No other source, test, Docker or CI changes.

## Checks
| Check | Result |
| --- | --- |
| `.gitignore` no longer hides `Data/` (simulated Windows ignore-case) | PASS (AI workspace) |
| Root `/data/` still ignored | PASS (AI workspace) |
| `dotnet restore/build/test` | NOT RUN (no .NET SDK in the AI workspace) |
| `docker compose build/up` | NOT RUN (not touched by this change; no Docker daemon access in the AI workspace) |
| Frontend lint/test/build | NOT RUN this session (no frontend change; CI run #4 reported PASS) |
| GitHub Actions after push | NOT RUN (unconfirmed until you push and report the run) |

## Known limitations
- `AppDbContext.cs` was recreated from how it is used (`AddDbContext<AppDbContext>`, `db.Database.CanConnectAsync`). Your local file
  may differ; keep yours if it does and only fix the ignore rule.
- Phase 0 is **not** green: live Greenhouse/Lever/Ashby fixtures, Gmail, Adzuna/Jooble and NAS checks are still open.
- `docs/phase0-feasibility.md` on `main` is still the old matrix (the phase0-gate docs change is not in `main`).

## Your next steps
1. Back up or check your local working tree at `C:\Users\Devan Shah\Repos\job-search-tracker`.
2. Before extracting, check whether your local `src\JobSearchTracker.Api\Data\AppDbContext.cs` exists and copy it aside.
3. Extract this ZIP into that folder, keeping `.git`. Then delete the 11 stray files if they are still present
   (`git rm` them; they are tracked in `main`). If your local `AppDbContext.cs` differed, restore it over the ZIP version.
4. Run `git status`: `src/JobSearchTracker.Api/Data/AppDbContext.cs` must appear as untracked/added (not missing, not ignored).
   Then `dotnet build JobSearchTracker.slnx -c Release` and `dotnet test JobSearchTracker.slnx -c Release`.
5. Commit and push yourself, open the Actions tab and report the backend/frontend/secret-scan result.
6. Upload this ZIP to the Notion Project Hub as the latest snapshot after pushing.
