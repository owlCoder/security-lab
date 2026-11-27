[CmdletBinding()]
param([switch]$NoBuild)

# Runs LAB-02 HTTP integration tests against a disposable loopback PostgreSQL.
$ErrorActionPreference = 'Stop'
$container = 'securitylab-lab02-' + [Guid]::NewGuid().ToString('N')
$started = $false
$savedConnection = $env:LAB02_TEST_POSTGRES
$savedPassword = $env:POSTGRES_PASSWORD
$savedIdentity = $env:ConnectionStrings__Identity
$savedAudit = $env:ConnectionStrings__Audit
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root ('TestResults/metadata-' + [Guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    # Do not let Development hosts use credentials from the developer's .env.
    $env:ConnectionStrings__Identity = ' '
    $env:ConnectionStrings__Audit = ' '
    $passwordBytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($passwordBytes) } finally { $random.Dispose() }
    $env:POSTGRES_PASSWORD = [BitConverter]::ToString($passwordBytes).Replace('-', '')
    docker run --detach --rm --name $container --publish '127.0.0.1::5432' `
        --env POSTGRES_USER=lab02 --env POSTGRES_DB=postgres --env POSTGRES_PASSWORD `
        --health-cmd 'pg_isready -U lab02 -d postgres' --health-interval 1s `
        --health-timeout 3s --health-retries 30 postgres:17-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start LAB-02 PostgreSQL.' }
    $started = $true
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    do {
        $health = docker inspect --format '{{.State.Health.Status}}' $container
        if ($health -eq 'healthy') { break }
        if ($health -eq 'unhealthy' -or [DateTime]::UtcNow -ge $deadline) { throw 'LAB-02 PostgreSQL did not become healthy.' }
        Start-Sleep -Seconds 1
    } while ($true)
    $binding = docker port $container 5432/tcp
    if ($binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Expected a single loopback port.' }
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection['Host'] = '127.0.0.1'
    $connection['Port'] = $Matches[1]
    $connection['Database'] = 'lab02_test_' + [Guid]::NewGuid().ToString('N')
    $connection['Username'] = 'lab02'
    $connection['Password'] = $env:POSTGRES_PASSWORD
    $env:LAB02_TEST_POSTGRES = $connection.ConnectionString
    $arguments = @('test', 'backend/Laboratories.Tests/Laboratories.Tests.csproj',
        '--configuration', 'Release', '--filter', 'FullyQualifiedName~LaboratoryMetadataCatalogTests',
        '--logger', 'trx;LogFileName=metadata.trx', '--results-directory', $results)
    if ($NoBuild) { $arguments += '--no-build' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'LAB-02 HTTP tests failed.' }
    [xml]$report = Get-Content -LiteralPath (Join-Path $results 'metadata.trx') -Raw
    $counters = $report.TestRun.ResultSummary.Counters
    if ([int]$counters.total -ne 7 -or [int]$counters.executed -ne 7 -or
        [int]$counters.passed -ne 7 -or [int]$counters.failed -ne 0 -or
        [int]$counters.notExecuted -ne 0) {
        throw 'Expected all seven catalog integration cases to run and pass.'
    }
    Write-Output "LAB-02 PostgreSQL HTTP integration: 7 passed. Evidence: $results"
}
finally {
    try {
        if ($started) { docker rm --force --volumes $container | Out-Null }
    }
    finally {
        $env:LAB02_TEST_POSTGRES = $savedConnection
        $env:POSTGRES_PASSWORD = $savedPassword
        $env:ConnectionStrings__Identity = $savedIdentity
        $env:ConnectionStrings__Audit = $savedAudit
        Pop-Location
    }
}
