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

## Run locally without Docker (`dotnet run`)

By default, the API listens on `http://localhost:5000` when running locally unless you override URLs.

```bash
dotnet run --project src/Api/Api.csproj
```

Optional explicit override (local):

```bash
ASPNETCORE_URLS=http://localhost:5000 dotnet run --project src/Api/Api.csproj
```

Run the worker in a second terminal:

```bash
dotnet run --project src/Worker/Worker.csproj
```

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
