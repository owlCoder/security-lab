#requires -Version 7.4
[CmdletBinding()]
param([switch]$NoBuild, [switch]$UseExistingServer)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$savedConnection = [Environment]::GetEnvironmentVariable('PRG04_TEST_ADMIN_CONNECTION_STRING')
$savedPassword = [Environment]::GetEnvironmentVariable('POSTGRES_PASSWORD')
$container = $null
$runId = [Guid]::NewGuid().ToString('N')
$results = Join-Path $repositoryRoot "TestResults/competencies-postgres-$runId"

Push-Location $repositoryRoot
try {
    Get-Command dotnet -ErrorAction Stop | Out-Null
    if ($UseExistingServer) {
        if ([string]::IsNullOrWhiteSpace($savedConnection)) {
            throw 'Set PRG04_TEST_ADMIN_CONNECTION_STRING to a dedicated TEST server with CREATE DATABASE permission.'
        }
    }
    else {
        Get-Command docker -ErrorAction Stop | Out-Null
        $env:POSTGRES_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        $container = "securitylab-prg04-$runId"
        docker run --detach --rm --name $container --publish '127.0.0.1::5432' `
            --env POSTGRES_USER=prg04_test --env POSTGRES_DB=postgres --env POSTGRES_PASSWORD `
            --env POSTGRES_HOST_AUTH_METHOD=scram-sha-256 `
            --health-cmd 'pg_isready -h 127.0.0.1 -U prg04_test -d postgres' `
            --health-interval 1s --health-timeout 3s --health-retries 30 postgres:17-alpine | Out-Null
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        do {
            $health = docker inspect --format '{{.State.Health.Status}}' $container
            if ($health -eq 'healthy') { break }
            if ($health -eq 'unhealthy' -or [DateTime]::UtcNow -ge $deadline) {
                throw 'The isolated PRG-04 PostgreSQL container did not become healthy.'
            }
            Start-Sleep -Seconds 1
        } while ($true)
        $bindings = @(docker port $container 5432/tcp)
        if ($bindings.Count -ne 1 -or $bindings[0] -notmatch '^127\.0\.0\.1:(\d+)$') {
            throw 'Expected a single loopback PostgreSQL port.'
        }
        $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
        $connection['Host'] = '127.0.0.1'
        $connection['Port'] = $Matches[1]
        $connection['Database'] = 'postgres'
        $connection['Username'] = 'prg04_test'
        $connection['Password'] = $env:POSTGRES_PASSWORD
        $env:PRG04_TEST_ADMIN_CONNECTION_STRING = $connection.ConnectionString
    }

    $buildArguments = @()
    if ($NoBuild) { $buildArguments += '--no-build' }
    dotnet test backend/Platform.Competencies.Tests/Platform.Competencies.Tests.csproj --configuration Release `
        @buildArguments -m:1 -p:UseSharedCompilation=false --disable-build-servers `
        --logger 'trx;LogFileName=competencies-postgres.trx' --results-directory $results

    $report = Join-Path $results 'competencies-postgres.trx'
    if (-not (Test-Path -LiteralPath $report)) { throw 'The test runner did not produce PRG-04 TRX evidence.' }
    [xml]$trx = Get-Content -LiteralPath $report -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 1 -or [int]$counters.executed -ne [int]$counters.total -or
        [int]$counters.passed -ne [int]$counters.total -or [int]$counters.notExecuted -ne 0) {
        throw 'PRG-04 requires all tests to execute and pass, with zero skipped cases.'
    }
    $persistenceIds = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object {
        ($_.TestMethod.className -split ',', 2)[0] -eq 'Platform.Competencies.Tests.CompetencyPersistenceTests'
    } | ForEach-Object { $_.id })
    $persistenceResults = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testId -in $persistenceIds })
    if ($persistenceResults.Count -lt 8 -or @($persistenceResults | Where-Object { $_.outcome -ne 'Passed' }).Count -gt 0) {
        throw 'Expected at least eight passing CompetencyPersistenceTests against PostgreSQL.'
    }
    Write-Host "PRG-04 PostgreSQL PASS: $($counters.passed)/$($counters.total), persistence: $($persistenceResults.Count). Results: $report"
}
finally {
    try {
        if ($container) {
            $PSNativeCommandUseErrorActionPreference = $false
            docker rm --force --volumes $container | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove the disposable PRG-04 container $container." }
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('PRG04_TEST_ADMIN_CONNECTION_STRING', $savedConnection)
        [Environment]::SetEnvironmentVariable('POSTGRES_PASSWORD', $savedPassword)
        Pop-Location
    }
}
