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
- Backend: `dotnet build JobSearchTracker.slnx` and `dotnet test JobSearchTracker.slnx`
- Frontend (in `web/`): `npm ci`, `npm run lint`, `npm test`, `npm run build`
- Stack: copy `.env.example` to `.env`, set `POSTGRES_PASSWORD`, then `docker compose up --build`
