# Ingest Process Service Template (.NET 8)

Minimal production-style template for async ingestion processing with:
- `src/Api`: ASP.NET Core HTTP API
- `src/Worker`: .NET BackgroundService worker
- Postgres + EF Core migrations
- Dockerfiles + docker compose

## Architecture

1. API accepts ingestion jobs and stores `ingestion_jobs` + `raw_events`.
2. Worker claims jobs safely using `FOR UPDATE SKIP LOCKED`.
3. Worker aggregates raw events by `type` and stores `ingestion_results`.
4. API serves status and processed results.

## Endpoints

- `POST /v1/ingestions`
  - body: `{ tenantId, events: [{ type, timestamp, payload }] }`
  - optional header: `Idempotency-Key`
  - returns `202 Accepted { jobId }`
- `GET /v1/ingestions/{jobId}`
- `GET /v1/results/{jobId}`
- `GET /health/live` (liveness only; always `200` while process is running, no external dependency checks)
- `GET /health/ready` (readiness; returns `200` only when Postgres is reachable and `SELECT 1` succeeds)

## Configuration

Environment variables:
- `ConnectionStrings__Db` (preferred) or `DATABASE_URL`
- `WORKER_CONCURRENCY`
- `MAX_ATTEMPTS`
- `BASE_BACKOFF_SECONDS`
- `WORKER_POLL_SECONDS`
- `WORKER_IDLE_BACKOFF_MAX_SECONDS`
- `WORKER_LOG_NO_JOBS`
- `LOG_LEVEL`
- `RUN_MIGRATIONS_ON_STARTUP`
- `ASPNETCORE_URLS` / `PORT` (port binding notes below)

## Local Development (no Docker)

Prerequisites:
- .NET 8 SDK
- PostgreSQL

Set your database connection string:

```bash
ConnectionStrings__Db="Host=localhost;Port=5432;Database=ingest_process;Username=postgres;Password=postgres"
```

Apply migrations once:

```bash
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/Api/Api.csproj
```

Run API and Worker in two terminals:

```bash
dotnet run --project src/Api/Api.csproj
```

```bash
dotnet run --project src/Worker/Worker.csproj
```

Local health endpoints:
- `http://localhost:5000/health/live`
- `http://localhost:5000/health/ready`

## Run with Docker Compose

```bash
docker compose up --build
```

API: `http://localhost:8080`

Run smoke test:

```bash
./scripts/smoke.sh http://localhost:8080
```

## Ports and deployment (container/App Platform)

- Containerized API is configured to listen on `0.0.0.0:8080` (`ASPNETCORE_URLS=http://+:8080`).
- `Dockerfile.api` exposes port `8080`.
- `app.yaml` sets `http_port: 8080`, which matches DigitalOcean App Platform's default expectation for HTTP services.
- If your platform injects `PORT`, ensure your runtime binding resolves to `0.0.0.0:8080` (or keep `ASPNETCORE_URLS=http://+:8080`).

## DigitalOcean App Platform Deployment (Buildpack)

1. Create a new app from this GitHub repository in App Platform.
2. Add a PostgreSQL database component (DigitalOcean Managed PostgreSQL is recommended).
3. App Platform provides `DATABASE_URL` automatically when the DB is attached.
4. Set environment variables for the API service:
   - `ConnectionStrings__Db=${DATABASE_URL}`
   - `RUN_MIGRATIONS_ON_STARTUP=true` (first deploy only)
5. Deploy once, verify the app starts, then set `RUN_MIGRATIONS_ON_STARTUP=false` for normal operation.
6. Ensure the web service binds to `0.0.0.0:8080` (App Platform HTTP default).

If the UI does not let you add a Worker during initial setup, add one after app creation:
- Create a Worker component from the same repo.
- Set a run command that starts the worker process (for example: `dotnet run --project src/Worker/Worker.csproj`).
- Ensure the Worker also has `ConnectionStrings__Db=${DATABASE_URL}`.

## Smoke Test (Windows)

After deploy, run:

```powershell
.\scripts\smoke.ps1 -BaseUrl "https://<app>.ondigitalocean.app"
```

## Tests

Run all tests:

```bash
dotnet test
```

- Unit tests always run.
- Integration tests are PostgreSQL-based and run only when `ConnectionStrings__Db` is set.
- If `ConnectionStrings__Db` is not set, integration tests are skipped.
- Use a dedicated test database (recommended), for example:

```bash
ConnectionStrings__Db="Host=localhost;Port=5432;Database=ingest_process_test;Username=postgres;Password=postgres"
```

Windows helper script:

```powershell
./scripts/test.ps1
```

See script options with:

```powershell
./scripts/test.ps1 -Help
```

## Reset local DB

If you have an older local schema with mismatched identifier casing, reset the database and re-apply migrations:

```bash
dropdb ingest_process
createdb ingest_process
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/Api/Api.csproj
```

## Migrations

API runs `db.Database.Migrate()` on startup when `RUN_MIGRATIONS_ON_STARTUP=true`.
Initial migration is included under `src/Infrastructure/Migrations`.
