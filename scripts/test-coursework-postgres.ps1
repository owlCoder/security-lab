[CmdletBinding()]
param([switch]$UseExistingServer, [switch]$Demonstrate)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$savedConnection = [Environment]::GetEnvironmentVariable('COURSEWORK_TEST_POSTGRES')
$savedPassword = [Environment]::GetEnvironmentVariable('POSTGRES_PASSWORD')
$container = $null
$runId = [Guid]::NewGuid().ToString('N')
$results = Join-Path $repositoryRoot "TestResults/coursework-postgres-$runId"

Push-Location $repositoryRoot
try {
    Get-Command dotnet -ErrorAction Stop | Out-Null
    if ($UseExistingServer) {
        if ([string]::IsNullOrWhiteSpace($savedConnection)) {
            throw 'Set COURSEWORK_TEST_POSTGRES to a dedicated TEST server with CREATE DATABASE permission.'
        }
    }
    else {
        Get-Command docker -ErrorAction Stop | Out-Null
        $secretBytes = New-Object byte[] 32
        $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $rng.GetBytes($secretBytes) } finally { $rng.Dispose() }
        $env:POSTGRES_PASSWORD = [Convert]::ToBase64String($secretBytes)
        $container = "securitylab-coursework-$runId"
        docker run --detach --rm --name $container --publish '127.0.0.1::5432' `
            --env POSTGRES_USER=coursework_test --env POSTGRES_DB=postgres --env POSTGRES_PASSWORD `
            --health-cmd 'pg_isready -h 127.0.0.1 -U coursework_test -d postgres' `
            --health-interval 1s --health-timeout 3s --health-retries 30 postgres:17-alpine | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not start the isolated PostgreSQL test container.' }
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        do {
            $health = docker inspect --format '{{.State.Health.Status}}' $container
            if ($LASTEXITCODE -ne 0) { throw 'Could not inspect PostgreSQL.' }
            if ($health -eq 'healthy') { break }
            if ($health -eq 'unhealthy' -or [DateTime]::UtcNow -ge $deadline) {
                throw 'The PostgreSQL test container did not become healthy.'
            }
            Start-Sleep -Seconds 1
        } while ($true)
        $binding = @(docker port $container 5432/tcp)
        $portMatch = if ($binding.Count -eq 1) { [regex]::Match($binding[0], '^127\.0\.0\.1:(\d+)$') } else { $null }
        if ($LASTEXITCODE -ne 0 -or $null -eq $portMatch -or -not $portMatch.Success) {
            throw 'Could not resolve the isolated PostgreSQL port.'
        }
        $connection = New-Object System.Data.Common.DbConnectionStringBuilder
        $connection['Host'] = '127.0.0.1'
        $connection['Port'] = $portMatch.Groups[1].Value
        $connection['Database'] = 'postgres'
        $connection['Username'] = 'coursework_test'
        $connection['Password'] = $env:POSTGRES_PASSWORD
        $env:COURSEWORK_TEST_POSTGRES = $connection.ConnectionString
    }

    dotnet test backend/Platform.Coursework.Tests/Platform.Coursework.Tests.csproj --configuration Release `
        -m:1 -p:UseSharedCompilation=false --disable-build-servers `
        --logger 'trx;LogFileName=administration-postgres.trx' --results-directory $results
    if ($LASTEXITCODE -ne 0) { throw 'ADM-08 PostgreSQL tests failed.' }

    $report = Join-Path $results 'administration-postgres.trx'
    if (-not (Test-Path -LiteralPath $report)) { throw 'The test runner did not produce TRX evidence.' }
    [xml]$trx = Get-Content -LiteralPath $report -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 1 -or [int]$counters.passed -ne [int]$counters.total) {
        throw 'Some PostgreSQL tests were not executed successfully (including skipped cases).'
    }
    foreach ($fixture in @('Platform.Coursework.Tests.AdministrationPostgresTests', 'Platform.Coursework.Tests.AdministrationHttpTests', 'Platform.Coursework.Tests.AttemptProviderAdapterTests')) {
        $discovered = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object {
            ($_.TestMethod.className -split ',', 2)[0] -eq $fixture
        })
        if ($discovered.Count -eq 0) { throw "Required integration fixture was not discovered: $fixture" }
    }
    Write-Host "Coursework PostgreSQL/HTTP/adapter PASS: $($counters.passed)/$($counters.total). Results: $report"
    if ($Demonstrate) {
        # Fresh fixtures: real ADM-08 providers; ACC-04 ATT/membership contract doubles are explicit.
        Write-Host 'ADM-08 uses real Identity/catalog/PostgreSQL/Audit; ACC-04 uses real Identity with explicit ATT and membership contract doubles.'
        dotnet test backend/Platform.Coursework.Tests/Platform.Coursework.Tests.csproj --configuration Release --no-build `
            --filter 'FullyQualifiedName~RealLoginDelegationSettingsRevocationRejectsTheExactPreviouslyIssuedCookie|FullyQualifiedName~Acc04RealSessionUsesSharedMembershipAndAttContractForOwnerReadAndCasWrite' `
            --logger 'console;verbosity=detailed'
        if ($LASTEXITCODE -ne 0) { throw 'The real-session HTTP demonstration failed.' }
    }

}
finally {
    try {
        if ($container) { docker rm --force $container 2>$null | Out-Null }
    }
    finally {
        [Environment]::SetEnvironmentVariable('COURSEWORK_TEST_POSTGRES', $savedConnection)
        [Environment]::SetEnvironmentVariable('POSTGRES_PASSWORD', $savedPassword)
        Pop-Location
    }
}
