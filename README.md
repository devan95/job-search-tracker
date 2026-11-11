# Job Search & Application Tracker

Local-first tracker. Requirements/decisions live in Notion (Project Hub); code, tests and CI live here.

## Prerequisites
.NET 10 SDK, Node 20+, Docker Desktop, `dotnet tool install -g dotnet-ef`

## Run (first milestone)
```powershell
copy deploy\.env.sample deploy\.env      # set a local password
docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d
dotnet ef migrations add Initial --project apps/server   # first time only
dotnet run --project apps/server                         # http://localhost:5080
cd apps/web; npm install; npm run dev                    # http://localhost:5173
```
Health: `GET /api/health/live` (no DB), `GET /api/health/ready` (DB check). Migrations apply on startup in Development.

## Test
```powershell
dotnet test tests/backend
cd apps/web; npm run lint; npm run build
```
Secrets never go in git: use `deploy/.env` and user-secrets (both ignored/sample-only).
