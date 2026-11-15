# Handoff: shrink Phase 0 record, ignore HANDOFF.md

**Base:** `main` @ `2871c64` ("Cleanup"), CI run 43669264079 green (read via GitHub). Nothing was committed or pushed by the AI.

## Changed
| File | Change |
| --- | --- |
| `docs/phase0-feasibility.md` | Rewritten as a short evidence record (11 KB to about 5 KB). Phase 0 gate text removed; the roadmap and status are in Notion. |
| `.gitignore` | Adds `/HANDOFF.md` (it is a per-delivery note shipped in the ZIP, as the Work Protocol says). |

**Deleted files: none** in the ZIP. One local step is needed so Git stops tracking the file (below).

## Checks
| Check | Result |
| --- | --- |
| File list vs `2871c64`: only the files above differ, none missing | PASS (AI) |
| Docs only: build, tests | NOT RUN (no code change) |

## Your next steps
1. Extract the ZIP over your clone without deleting files first.
2. Run `git rm --cached HANDOFF.md` once, then `git status`: expect `.gitignore` and `docs/phase0-feasibility.md` modified and `HANDOFF.md` deleted from the index (the file stays on disk). Stop if any other `D` appears.
3. Commit and push a branch; confirm CI is green.
