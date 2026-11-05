#requires -Version 7.4
[CmdletBinding()]
param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function New-AuditTestConnectionString($port, $user, $password) {
    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['Host'] = '127.0.0.1'
    $builder['Port'] = $port
    $builder['Database'] = 'securitylab_audit_tests'
    $builder['Username'] = $user
    $builder['Password'] = $password
    return $builder.ConnectionString
}

function Assert-AuditPostgresResults($resultsDirectory) {
    $remaining = @(
        'Platform.Audit.Tests.PostgresAuditEventStoreTests'
        'Platform.Audit.Tests.AuditRuntimePermissionsTests'
        'Platform.Audit.Tests.AuditHostPostgresTests'
    )
    $required = $remaining
    foreach ($file in Get-ChildItem -LiteralPath $resultsDirectory -Filter '*.trx' -Recurse) {
        [xml]$report = Get-Content -Raw -LiteralPath $file.FullName
        $fixtures = @{}
        foreach ($test in $report.TestRun.TestDefinitions.UnitTest) {
            $fixture = ($test.TestMethod.className -split ',', 2)[0]
            if ($fixture -in $required) {
                $fixtures[$test.id] = $fixture
            }
        }
        foreach ($result in $report.TestRun.Results.UnitTestResult) {
            if (-not $fixtures.ContainsKey($result.testId)) {
                continue
            }
            if ($result.outcome -ne 'Passed') {
                throw "PostgreSQL test '$($result.testName)' did not pass: $($result.outcome)."
            }
            $remaining = @($remaining | Where-Object { $_ -ne $fixtures[$result.testId] })
        }
    }
    if ($remaining.Count -gt 0) {
        throw "No passing PostgreSQL tests found for: $($remaining -join ', ')."
    }
}

$repositoryRoot = Join-Path $PSScriptRoot '../..'
$runId = [Guid]::NewGuid().ToString('N')
$resultsDirectory = Join-Path $repositoryRoot "TestResults/audit-s09-$runId"
$container = $null
$savedEnvironment = @{}
foreach ($name in 'POSTGRES_PASSWORD', 'AUDIT_RUNTIME_PASSWORD', 'AUDIT_MIGRATION_CONNECTION_STRING',
    'AUDIT_TEST_ADMIN_CONNECTION_STRING', 'AUDIT_TEST_RUNTIME_CONNECTION_STRING') {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
}

Push-Location $repositoryRoot
try {
    Get-Command docker -ErrorAction Stop | Out-Null
    if (-not $NoBuild) {
        dotnet build backend/SecurityLab.slnx --configuration Release
    }
    dotnet tool restore

    # Disposable credentials and an OS-assigned loopback port also work in fork PRs.
    $env:POSTGRES_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $env:AUDIT_RUNTIME_PASSWORD = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $container = docker run --detach --rm --name "securitylab-audit-$runId" --publish '127.0.0.1::5432' `
        --env POSTGRES_USER=securitylab_bootstrap --env POSTGRES_DB=securitylab_audit_tests `
        --env POSTGRES_PASSWORD --env POSTGRES_HOST_AUTH_METHOD=scram-sha-256 `
        --health-cmd 'pg_isready -h 127.0.0.1 -U securitylab_bootstrap -d securitylab_audit_tests' `
        --health-interval 1s --health-timeout 3s --health-retries 30 postgres:17-alpine

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $health = docker inspect --format '{{.State.Health.Status}}' $container
        if ($health -eq 'healthy') {
            break
        }
        if ($health -eq 'unhealthy' -or [DateTime]::UtcNow -ge $deadline) {
            throw 'The isolated PostgreSQL container did not become healthy.'
        }
        Start-Sleep -Seconds 1
    } while ($true)

    $bindings = @(docker port $container 5432/tcp)
    if ($bindings.Count -ne 1 -or $bindings[0] -notmatch '^127\.0\.0\.1:(\d+)$') {
        throw 'Expected a single loopback PostgreSQL port.'
    }
    $port = $Matches[1]
    $env:AUDIT_TEST_ADMIN_CONNECTION_STRING =
        New-AuditTestConnectionString $port 'securitylab_bootstrap' $env:POSTGRES_PASSWORD
    $env:AUDIT_TEST_RUNTIME_CONNECTION_STRING =
        New-AuditTestConnectionString $port 'securitylab_audit_runtime' $env:AUDIT_RUNTIME_PASSWORD
    $env:AUDIT_MIGRATION_CONNECTION_STRING = $env:AUDIT_TEST_ADMIN_CONNECTION_STRING

    $auditProject = 'backend/Platform.Audit/Platform.Audit.csproj'
    dotnet ef database update --project $auditProject --startup-project $auditProject `
        --context AuditDbContext --configuration Release --no-build
    Get-Content -Raw -LiteralPath 'scripts/audit/bootstrap-runtime.sql' |
        docker exec -i --env AUDIT_RUNTIME_PASSWORD $container `
            psql --set=ON_ERROR_STOP=1 --username=securitylab_bootstrap --dbname=securitylab_audit_tests
    $env:AUDIT_MIGRATION_CONNECTION_STRING = $null
    $env:POSTGRES_PASSWORD = $null
    $env:AUDIT_RUNTIME_PASSWORD = $null

    dotnet test backend/SecurityLab.slnx --configuration Release --no-build `
        --logger trx --results-directory $resultsDirectory
    Assert-AuditPostgresResults $resultsDirectory
    Write-Output "Audit PostgreSQL fixtures passed. TRX: $resultsDirectory"
} finally {
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
    }
    Pop-Location
    if ($container) {
        $PSNativeCommandUseErrorActionPreference = $false
        docker rm --force --volumes $container | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not remove the disposable Audit container $container."
        }
    }
}
