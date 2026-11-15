# Handoff: repository hygiene (migration step, work-mode constant, CI hardening)

**Base:** `main` @ `4791f6d` ("Removed duplicate info"); CI run #11 (43623072352) succeeded there. Nothing was committed or pushed by the AI.
**Scope:** maintenance only. No product features.

## Changed
| File | Change |
| --- | --- |
| `src/JobSearchTracker.Api/Program.cs` | Opt-in `--MigrateOnly=true` mode: applies pending EF migrations and exits. Normal startup is unchanged and never migrates. |
| `docker-compose.yml` | New one-shot `migrate` service (profile `tools`, not started by `up`); `api` and `migrate` share image `jobsearchtracker-api:local`; db host port overridable via `DB_HOST_PORT` (default 5432). |
| `docs/development.md`, `docs/data-model.md`, `.env.example` | Windows CMD migration workflow, disposable-database verification steps, `DB_HOST_PORT`. |
| `src/JobSearchTracker.Api/Data/AppDbContext.cs` | Both work-mode check constraints now use `WorkModesLimits.AllModes` (value 7, SQL text identical, so no new migration is expected). |
| `.github/workflows/ci.yml` | Added `timeout-minutes` (15/10/5), and `concurrency` that cancels superseded pull-request runs only. Jobs and steps are otherwise unchanged. |
| `.github/dependabot.yml` | New: weekly updates for NuGet, npm, GitHub Actions, Docker and Docker Compose. |
| `HANDOFF.md` | Replaced (it was stale: old base `b1b8db6`, "never ran in CI"). |

**Deleted files: none.** `README.md` and `CLAUDE.md` untouched.

Decision for you: migrations stay explicit (`docker compose run --rm migrate`), not automatic at startup. Say so if you want automatic migration instead.

## Checks (honest status)
| Check | Result |
| --- | --- |
| `docker compose config` (default and `--profile tools`) | PASS (AI): `up` services are db, api, worker; `migrate` only with the profile |
| YAML parse of `ci.yml`, `dependabot.yml`, `docker-compose.yml` | PASS (AI) |
| File list vs `4791f6d`: only the files above differ, none missing | PASS (AI) |
| `dotnet build`, `dotnet test`, `dotnet ef migrations has-pending-model-changes` | NOT RUN (no .NET SDK in the AI workspace) |
| Fresh-database init, existing-database upgrade, repeat run | NOT RUN (needs .NET and Docker daemon) |
| Frontend `npm ci/lint/test/build` | NOT RUN (no frontend change) |
| GitHub Actions / secret scan | NOT RUN (runs after you push) |
| Action SHA pinning | NOT DONE: SHAs could not be verified from here; Dependabot keeps version tags current |

## Your next steps
1. Back up the repo folder. Extract this ZIP over it, keeping `.git`; do not delete files first.
2. `git status`: expect modified `src/…/Program.cs` and `AppDbContext.cs`, `docker-compose.yml`, `.env.example`, `docs/`, `.github/workflows/ci.yml`, `HANDOFF.md` and new `.github/dependabot.yml`; no `D`.
3. Run `dotnet tool restore`, `dotnet build JobSearchTracker.slnx`, `dotnet test JobSearchTracker.slnx` and the EF model check (must report no pending changes).
4. Verify the migration workflow on a disposable project using the steps in `docs/development.md` ("Verifying the workflow on a disposable database").
5. Push a branch, open a PR, and check CI. Report the run and any log lines if it fails.
