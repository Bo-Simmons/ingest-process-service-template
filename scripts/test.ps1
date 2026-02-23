[CmdletBinding()]
param(
    [string]$DbHost = "localhost",
    [int]$DbPort = 5432,
    [string]$DbUser = "postgres",
    [string]$DbPassword = "postgres",
    [string]$TestDatabase = "ingest_process_test",
    [switch]$DropAndRecreate,
    [switch]$Truncate
)

$ErrorActionPreference = "Stop"

# Build and export the PostgreSQL connection string used by integration tests.
$connectionString = "Host=$DbHost;Port=$DbPort;Database=$TestDatabase;Username=$DbUser;Password=$DbPassword"
$env:ConnectionStrings__Db = $connectionString
$env:PGPASSWORD = $DbPassword

Write-Host "ConnectionStrings__Db set to test database '$TestDatabase'."
Write-Host "Connection string: $connectionString"

if ($DropAndRecreate) {
    Write-Host "Dropping and recreating test database '$TestDatabase'..."
    & dropdb --if-exists -h $DbHost -p $DbPort -U $DbUser $TestDatabase
    & createdb -h $DbHost -p $DbPort -U $DbUser $TestDatabase
}
elseif ($Truncate) {
    Write-Host "Truncating test tables in '$TestDatabase'..."
    $truncateSql = "TRUNCATE TABLE ingestion_results, raw_events, ingestion_jobs RESTART IDENTITY CASCADE;"
    & psql -h $DbHost -p $DbPort -U $DbUser -d $TestDatabase -c $truncateSql
}

Write-Host "Running dotnet test..."
& dotnet test
