# Handoff: documentation de-duplication, CI secret-scan permission, Dependabot majors

**Base:** `main` @ `e4755ed` ("Hygine"), read via GitHub. Nothing was committed or pushed by the AI.

## Changed
| File | Change |
| --- | --- |
| `.github/workflows/ci.yml` | `secret-scan` job gets `pull-requests: read`. Without it gitleaks fails on every pull request ("Resource not accessible by integration"), as seen on Dependabot PRs 1-14. |
| `.github/dependabot.yml` | Ignore semver-major bumps for `dotnet/*` Docker images, `dotnet-ef`, `Microsoft.*`, `Npgsql.*` and `@types/node` (app targets .NET 10, Node 22). |
| `docs/data-model.md` | Migration commands and the deferred-requirements table replaced by links (owned by development.md and Notion). |
| `docs/api.md` | Tests paragraph replaced by a link to development.md. |
| `HANDOFF.md` | Replaced. |

**Deleted files: none.** No application code changed.

## Checks
| Check | Result |
| --- | --- |
| YAML parse of `ci.yml` and `dependabot.yml` | PASS (AI) |
| File list vs `e4755ed`: only the files above differ, none missing | PASS (AI) |
| Secret-scan fix on a real pull request | NOT RUN (needs a push) |
| `dotnet build/test`, frontend checks | NOT RUN (no code change) |

## Your next steps
1. Extract over your clone without deleting files first; `git status` must show only the files above, no `D`.
2. Push a branch and open a PR; confirm `secret-scan` is now green.
3. Then merge Dependabot PRs 2, 3, 6 and 1 (see the chat note); close 4, 5, 7, 8, 9, 11, 12; review 10, 13, 14.
