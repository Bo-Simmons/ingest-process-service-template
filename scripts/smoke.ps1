[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl
)

$ErrorActionPreference = "Stop"

function Get-PropertyValueCaseInsensitive {
    param(
        [Parameter(Mandatory = $true)]
        $Object,
        [Parameter(Mandatory = $true)]
        [string[]]$Names
    )

    if ($null -eq $Object) {
        return $null
    }

    $properties = @{}
    foreach ($p in $Object.PSObject.Properties) {
        $properties[$p.Name.ToLowerInvariant()] = $p.Value
    }

    foreach ($name in $Names) {
        $key = $name.ToLowerInvariant()
        if ($properties.ContainsKey($key)) {
            return $properties[$key]
        }
    }

    return $null
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('GET', 'POST', 'PUT', 'PATCH', 'DELETE')]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [string]$Uri,
        [hashtable]$Headers,
        [string]$Body
    )

    $requestParams = @{
        Method      = $Method
        Uri         = $Uri
        ErrorAction = 'Stop'
    }

    if ($Headers) {
        $requestParams.Headers = $Headers
    }

    if ($PSBoundParameters.ContainsKey('Body')) {
        $requestParams.Body = $Body
        $requestParams.ContentType = 'application/json'
    }

    try {
        $response = Invoke-WebRequest @requestParams
        if ([string]::IsNullOrWhiteSpace($response.Content)) {
            return $null
        }

        return $response.Content | ConvertFrom-Json -Depth 20
    }
    catch {
        Write-Error "Request failed: $Method $Uri"

        if ($_.Exception.Response -and $_.Exception.Response.GetResponseStream()) {
            $stream = $_.Exception.Response.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            $errorBody = $reader.ReadToEnd()
            $reader.Close()

            if (-not [string]::IsNullOrWhiteSpace($errorBody)) {
                Write-Host "Response body:"
                Write-Host $errorBody
            }
        }

        throw
    }
}

$BaseUrl = $BaseUrl.TrimEnd('/')
Write-Host "Using base URL: $BaseUrl"

Write-Host "Checking health endpoints..."
Invoke-JsonRequest -Method GET -Uri "$BaseUrl/health/live" | Out-Null
Invoke-JsonRequest -Method GET -Uri "$BaseUrl/health/ready" | Out-Null

$payload = @{
    tenantId = 't1'
    events   = @(
        @{
            type      = 'a'
            timestamp = [DateTime]::UtcNow.ToString('o')
            payload   = @{ source = 'smoke.ps1'; sequence = 1; amount = 10 }
        },
        @{
            type      = 'b'
            timestamp = [DateTime]::UtcNow.AddSeconds(1).ToString('o')
            payload   = @{ source = 'smoke.ps1'; sequence = 2; amount = 20 }
        },
        @{
            type      = 'a'
            timestamp = [DateTime]::UtcNow.AddSeconds(2).ToString('o')
            payload   = @{ source = 'smoke.ps1'; sequence = 3; amount = 30 }
        }
    )
}

$payloadJson = $payload | ConvertTo-Json -Depth 10
$idempotencyKey = "smoke-$(Get-Date -Format 'yyyyMMddHHmmssfff')"

Write-Host "Submitting ingestion with idempotency key: $idempotencyKey"
$headers = @{ 'Idempotency-Key' = $idempotencyKey }
$postResponse = Invoke-JsonRequest -Method POST -Uri "$BaseUrl/v1/ingestions" -Headers $headers -Body $payloadJson
$jobId = Get-PropertyValueCaseInsensitive -Object $postResponse -Names @('jobId', 'jobID', 'id')

if ([string]::IsNullOrWhiteSpace($jobId)) {
    Write-Host "POST response:"
    $postResponse | ConvertTo-Json -Depth 20
    throw "Unable to parse jobId from ingestion response."
}

Write-Host "Polling ingestion status for jobId: $jobId"
$deadline = (Get-Date).AddSeconds(60)
$status = $null

while ((Get-Date) -lt $deadline) {
    $statusResponse = Invoke-JsonRequest -Method GET -Uri "$BaseUrl/v1/ingestions/$jobId"
    $status = Get-PropertyValueCaseInsensitive -Object $statusResponse -Names @('status')

    if ($status -in @('Succeeded', 'Failed')) {
        break
    }

    Start-Sleep -Seconds 1
}

if ($status -notin @('Succeeded', 'Failed')) {
    throw "Timed out waiting for terminal status for jobId '$jobId'. Last status: '$status'."
}

if ($status -eq 'Failed') {
    throw "Ingestion job '$jobId' finished with status Failed."
}

Write-Host "Fetching results for jobId: $jobId"
$results = Invoke-JsonRequest -Method GET -Uri "$BaseUrl/v1/results/$jobId"

Write-Host "Replaying ingestion with same idempotency key to verify idempotency..."
$replayResponse = Invoke-JsonRequest -Method POST -Uri "$BaseUrl/v1/ingestions" -Headers $headers -Body $payloadJson
$replayJobId = Get-PropertyValueCaseInsensitive -Object $replayResponse -Names @('jobId', 'jobID', 'id')

if ([string]::IsNullOrWhiteSpace($replayJobId)) {
    Write-Host "Replay response:"
    $replayResponse | ConvertTo-Json -Depth 20
    throw "Unable to parse jobId from replay ingestion response."
}

if ($replayJobId -ne $jobId) {
    Write-Host "First response:"
    $postResponse | ConvertTo-Json -Depth 20
    Write-Host "Replay response:"
    $replayResponse | ConvertTo-Json -Depth 20
    throw "Idempotency check failed. Expected replay jobId '$jobId' but got '$replayJobId'."
}

Write-Host "jobId: $jobId"
Write-Host "finalStatus: $status"
Write-Host "results:"
$results | ConvertTo-Json -Depth 20
