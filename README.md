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
- `LOG_LEVEL`
- `ASPNETCORE_URLS=http://+:8080`

## Local run (docker compose)

```bash
docker compose up --build
```

API: `http://localhost:8080`

Run smoke test:

```bash
./scripts/smoke.sh http://localhost:8080
```

## Tests

```bash
dotnet test
```

- Unit tests validate aggregation + backoff logic.
- Integration test uses **SQLite fallback** (for CI environments where Docker/Testcontainers may be unavailable), and simulates worker processing after API submission.

## Deployment notes (DigitalOcean App Platform)

- `app.yaml` defines one web service (`Dockerfile.api`) and one worker (`Dockerfile.worker`).
- Attach a managed Postgres database and set `ConnectionStrings__Db`.
- Internal app port is `8080`.

## Reset local DB

If you have an older local schema with mismatched identifier casing, reset the database and re-apply migrations:

```bash
dropdb ingest_process
createdb ingest_process
dotnet ef database update --project src/Infrastructure/Infrastructure.csproj --startup-project src/Api/Api.csproj
```

## Migrations

API runs `db.Database.Migrate()` on startup.
Initial migration is included under `src/Infrastructure/Migrations`.
