# Instructions for AI coding sessions

Project: Job Search & Application Tracker (local-first, .NET 10 + React/TypeScript + PostgreSQL).
Requirements, decisions and tasks live in the Notion "Job Search & Application Tracker — Project Hub".

## Before editing
1. Read the relevant Notion pages (Work Protocol, Product Requirements, Architecture, Testing Plan) and the Task Board item.
2. Inspect the real repository state (structure, Git status, branch, CI). Do not assume the layout in this file is current.
3. Preserve existing and uncommitted work. Work one small task at a time.

## Rules
- Implement only requirements marked Confirmed. Do not change product semantics (Applied, source status, scan success) silently.
- Never commit secrets, real `.env` files, candidate data or resumes. Use synthetic data.
- Do not commit, push, merge, run destructive migrations or submit applications without explicit user approval.
- Report tests honestly: PASS, FAIL, NOT RUN or BLOCKED. A test that was not run is not passing.
- Core tracking must keep working when Gmail, AI or any single source is unavailable.
- Delivery: every change set the user is expected to push is delivered as a complete repository ZIP (no `.git`,
  no secrets, no build output) with a HANDOFF.md. See the Work Protocol in Notion.

## Commands
- One-time: `dotnet tool restore` (pins dotnet-ef from `.config/dotnet-tools.json`)
- Backend: `dotnet build JobSearchTracker.slnx` and `dotnet test JobSearchTracker.slnx`
- Database tests (`Category=Database`) need PostgreSQL and the `JOBSEARCH_TEST_ADMIN_CONNECTION` variable; they fail,
  never skip, without it. See `docs/development.md`. Without a database: `--filter "Category!=Database"` (this does not
  prove the migration or schema).
- EF model check (also run by CI): `dotnet ef migrations has-pending-model-changes --project src/JobSearchTracker.Api --startup-project src/JobSearchTracker.Api`
- Frontend (in `web/`): `npm ci`, `npm run lint`, `npm test`, `npm run build`
- Stack: copy `.env.example` to `.env`, set `POSTGRES_PASSWORD`, then `docker compose up --build`

## ZIP delivery checklist
Before sending a ZIP, compare its file list with the base commit's `git ls-files` (or the supplied tree).
- The ZIP must contain the complete tree, including hidden files and folders: `.github/`, `.config/`, `.gitignore`,
  `.gitattributes`, `.dockerignore`, `.editorconfig`, `.env.example`, `web/.gitignore`, `web/.oxlintrc.json`.
- A tracked file missing from the ZIP is a blocker, not a detail. Never assume the user's replace step keeps files that
  are absent from the ZIP; it may delete them.
- `HANDOFF.md` must list every intentionally deleted file, or say "Deleted files: none".
- If the base is a ZIP without `.github/` or `.git/`, say so and ask for a complete snapshot before packaging.
