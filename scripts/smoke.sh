#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"

echo "Checking health..."
curl -fsS "$BASE_URL/health/live" >/dev/null
curl -fsS "$BASE_URL/health/ready" >/dev/null

echo "Submitting ingestion..."
JOB_ID=$(curl -fsS -X POST "$BASE_URL/v1/ingestions" \
  -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: smoke-1' \
  -d '{"tenantId":"smoke","events":[{"type":"clicked","timestamp":"2024-01-01T00:00:00Z","payload":{"p":1}},{"type":"viewed","timestamp":"2024-01-01T00:00:01Z","payload":{"p":2}}]}' \
  | sed -n 's/.*"jobId":"\([^"]*\)".*/\1/p')

if [[ -z "$JOB_ID" ]]; then
  echo "Failed to parse job id"
  exit 1
fi

echo "Polling status for $JOB_ID..."
for _ in {1..30}; do
  STATUS=$(curl -fsS "$BASE_URL/v1/ingestions/$JOB_ID" | sed -n 's/.*"status":"\([^"]*\)".*/\1/p')
  if [[ "$STATUS" == "Succeeded" ]]; then
    break
  elif [[ "$STATUS" == "Failed" ]]; then
    echo "Job failed"
    exit 1
  fi
  sleep 1
done

if [[ "$STATUS" != "Succeeded" ]]; then
  echo "Timed out waiting for success"
  exit 1
fi

echo "Fetching results..."
curl -fsS "$BASE_URL/v1/results/$JOB_ID"
echo
