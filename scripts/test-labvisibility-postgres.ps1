[CmdletBinding()]
param([switch]$NoBuild)

# AUT-04 (KT2-09): runs the LabVisibility PostgreSQL tests against a disposable PostgreSQL container.
# The database and its random password exist only for this run.

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$savedConnection = [Environment]::GetEnvironmentVariable('LABVISIBILITY_TEST_POSTGRES')
$savedPassword = [Environment]::GetEnvironmentVariable('POSTGRES_PASSWORD')
$container = $null
$runId = [Guid]::NewGuid().ToString('N')
$results = Join-Path $repositoryRoot "TestResults/labvisibility-postgres-$runId"

Push-Location $repositoryRoot
try {
    Get-Command dotnet -ErrorAction Stop | Out-Null
    Get-Command docker -ErrorAction Stop | Out-Null

    $bytes = New-Object byte[] 32
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $env:POSTGRES_PASSWORD = ($bytes | ForEach-Object { $_.ToString('x2') }) -join ''

    $container = "securitylab-labvisibility-$runId"
    docker run --detach --rm --name $container --publish '127.0.0.1::5432' `
        --env POSTGRES_USER=labvisibility_test --env POSTGRES_DB=labvisibility --env POSTGRES_PASSWORD `
        --health-cmd 'pg_isready -h 127.0.0.1 -U labvisibility_test -d labvisibility' `
        --health-interval 1s --health-timeout 3s --health-retries 30 postgres:17-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start the disposable LabVisibility PostgreSQL container.' }

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $health = docker inspect --format '{{.State.Health.Status}}' $container
        if ($health -eq 'healthy') { break }
        if ($health -eq 'unhealthy' -or [DateTime]::UtcNow -ge $deadline) {
            throw 'The disposable LabVisibility PostgreSQL container did not become healthy.'
        }
        Start-Sleep -Seconds 1
    } while ($true)

    $bindings = @(docker port $container 5432/tcp)
    if ($bindings.Count -ne 1 -or $bindings[0] -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Expected a single loopback PostgreSQL port.'
    }

    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = $Matches[1]
    $connection['Database'] = 'labvisibility'
    $connection['Username'] = 'labvisibility_test'
    $connection['Password'] = $env:POSTGRES_PASSWORD
    $env:LABVISIBILITY_TEST_POSTGRES = $connection.ConnectionString

    $arguments = @(
        'test', 'backend/Platform.Tests/Platform.Tests.csproj',
        '--configuration', 'Release',
        '--filter', 'FullyQualifiedName~Platform.Tests.LabVisibility.LabVisibilityPostgres',
        '--logger', 'trx;LogFileName=labvisibility-postgres.trx',
        '--results-directory', $results)
    if ($NoBuild) { $arguments += '--no-build' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'LabVisibility PostgreSQL tests failed.' }

    $report = Join-Path $results 'labvisibility-postgres.trx'
    if (-not (Test-Path -LiteralPath $report)) { throw 'The test runner did not produce LabVisibility TRX evidence.' }
    [xml]$trx = Get-Content -LiteralPath $report -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 1 -or [int]$counters.executed -ne [int]$counters.total -or
        [int]$counters.passed -ne [int]$counters.total -or [int]$counters.notExecuted -ne 0) {
        throw 'LabVisibility requires all PostgreSQL tests to execute and pass, with zero skipped cases.'
    }

    $databaseIds = @($trx.TestRun.TestDefinitions.UnitTest | Where-Object {
        ($_.TestMethod.className -split ',', 2)[0] -like 'Platform.Tests.LabVisibility.LabVisibilityPostgres*'
    } | ForEach-Object { $_.id })
    $databaseResults = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.testId -in $databaseIds })
    if ($databaseResults.Count -lt 14 -or @($databaseResults | Where-Object { $_.outcome -ne 'Passed' }).Count -gt 0) {
        throw 'Expected at least 14 passing LabVisibility PostgreSQL tests (8 store/reader + 6 HTTP).'
    }

    Write-Host "LabVisibility PostgreSQL PASS: $($counters.passed)/$($counters.total), database tests: $($databaseResults.Count). Results: $report"
}
finally {
    try {
        if ($container) {
            docker rm --force --volumes $container | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Could not remove the disposable LabVisibility container $container." }
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable('LABVISIBILITY_TEST_POSTGRES', $savedConnection)
        [Environment]::SetEnvironmentVariable('POSTGRES_PASSWORD', $savedPassword)
        Pop-Location
    }
}