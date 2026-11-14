# Handoff: Phase 0 gate docs, with live ATS fixture evidence (docs only)

**Date:** 2026-11-14
**Task:** Put the Phase 0 matrix and acceptance criteria on `main` (`docs/phase0-feasibility.md`) and record the user-run live ATS
fixture evidence. **This ZIP supersedes `job-search-tracker-phase0-gate-docs-2026-11-14.zip`**; use only this one.
**Base:** `main` @ `9ba4ffc` ("Fixed CI"), read via the GitHub connector. Nothing committed or pushed by the AI.

## Changed in this ZIP
| File | Change |
| --- | --- |
| `docs/phase0-feasibility.md` | Evidence-based capability matrix and acceptance criteria. Greenhouse, Lever, Ashby and "live ATS fixtures" rows now VERIFIED (user-reported) with an evidence section and a list of what is not covered |
| `HANDOFF.md` | Replaced with this note |

No source, test, Docker, CI or `.gitignore` changes. Everything else is identical to `main` @ `9ba4ffc`.

## Evidence added (user-reported, not re-run by the AI)
- Greenhouse / GitLab: HTTP 200, 251 jobs (`meta.total` 251). Lever / Meesho: HTTP 200, 49 postings, `skip` pagination tested (5 + 5 distinct IDs).
  Ashby / OP Labs: HTTP 200, 5 jobs. Three trimmed 3-record fixtures kept outside Git (9,572 bytes in total).
- The AI could not reach these hosts (proxy 403, endpoints refused by the session allowlist) and has not seen the raw JSON or fixture files.

## Not covered (remaining, none blocks Phase 1)
Fixtures not in the repo (job-data terms to review first); descriptions excluded so content-field parsing is untested; pagination
only checked for Lever; no empty/closed/malformed cases; no automated integration tests. These are Phase 2 work.
One unresolved observation: the hosted Greenhouse page showed about 48 postings to the AI versus 251 from the API (probably
truncated or paginated page text; not checked).

## Checks
| Check | Result |
| --- | --- |
| Live ATS API calls (user's PC) | PASS (user-reported) |
| Docs re-read for consistency with the Testing Plan and Notion Task Board | PASS (AI review, no tooling) |
| `dotnet build/test`, `docker compose`, frontend tests, Actions | NOT RUN (docs only; no .NET SDK in the AI workspace) |

## Status
**Phase 0 is not green.** The user has not confirmed it. Criterion 3 is still open for Adzuna, Jooble, Gmail, OmniRoute, NAS and
branch protection (verify or explicitly defer). Phase 1 has not been started.

## Your next steps
1. Back up or check `C:\Users\Devan Shah\Repos\job-search-tracker`.
2. Extract this ZIP into the folder, keeping `.git`.
3. Review `git status` and `git diff` (expect only `docs/phase0-feasibility.md` and `HANDOFF.md`), then commit and push yourself.
4. Upload this ZIP to the Notion Project Hub as the latest snapshot after pushing.
5. Tell the AI which remaining Phase 0 items you defer (with a reason), then confirm Phase 0 green.
