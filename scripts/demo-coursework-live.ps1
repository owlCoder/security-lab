[CmdletBinding()]
param([switch]$UseExistingServer, [switch]$NoBuild, [switch]$AttemptDemo)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$savedConnection = [Environment]::GetEnvironmentVariable('COURSEWORK_TEST_POSTGRES')
$savedPassword = [Environment]::GetEnvironmentVariable('POSTGRES_PASSWORD')
$savedRunId = [Environment]::GetEnvironmentVariable('COURSEWORK_LIVE_RUN_ID')
$container = $null
$runId = [Guid]::NewGuid().ToString('N')

Push-Location $repositoryRoot
try {
    Get-Command dotnet -ErrorAction Stop | Out-Null
    if (-not $NoBuild) {
        dotnet build backend/Platform.Coursework.Demo/Platform.Coursework.Demo.csproj --configuration Release `
            -m:1 -p:UseSharedCompilation=false --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'The live demonstration build failed.' }
        if (-not $AttemptDemo) {
            dotnet build backend/Platform.Coursework.AdminTool/Platform.Coursework.AdminTool.csproj --configuration Release `
                -m:1 -p:UseSharedCompilation=false --disable-build-servers
            if ($LASTEXITCODE -ne 0) { throw 'The operator tool build failed.' }
        }
    }
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
        $container = "securitylab-coursework-live-$runId"
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

    $env:COURSEWORK_LIVE_RUN_ID = $runId
    $demoArguments = @()
    $scenario = 'ADM08'
    $evidenceName = 'live-administration.json'
    if ($AttemptDemo) {
        $demoArguments = @('--attempt-demo')
        $scenario = 'ACC04'
        $evidenceName = 'live-attempts.json'
    }

    dotnet backend/Platform.Coursework.Demo/bin/Release/net10.0/Platform.Coursework.Demo.dll @demoArguments
    if ($LASTEXITCODE -ne 0) { throw "The $scenario HTTPS/Kestrel demonstration failed." }

    $reportPath = Join-Path (Join-Path $repositoryRoot "TestResults/coursework-live-$runId") $evidenceName
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'No evidence was produced for this run.' }
    $report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $checks = @($report.checks)
    $http = @($checks | Where-Object { $_.kind -eq 'http' })
    $failed = @($checks | Where-Object { $_.passed -ne $true })
    if ($report.runId -ne $runId -or $report.scenario -ne $scenario -or
        $report.succeeded -ne $true -or $report.isolatedStateRemoved -ne $true -or $failed.Count -ne 0) {
        throw 'The live evidence is incomplete or does not belong to this run.'
    }
    if ($AttemptDemo) {
        if ($http.Count -lt 10 -or $report.providers -notcontains 'local HTTPS ATT stub' -or
            $report.providers -notcontains 'synthetic course membership') {
            throw 'The ACC-04 evidence did not confirm the required HTTPS and demo-provider checks.'
        }
        Write-Host "LIVE ACC04 evidence verified: $($checks.Count) checks, $($http.Count) HTTP checks."
        Write-Host 'ATT and course membership are demo fixtures; the API, Identity sessions and Access policies are real.'
    }
    else {
        if ($report.originalDelegateCookieReused -ne $true -or [int]$report.apiRestarts -lt 1 -or
            $checks.Count -lt 30 -or $http.Count -lt 25) {
            throw 'The ADM-08 evidence did not confirm the required HTTP, restart and same-cookie checks.'
        }
        Write-Host "LIVE ADM08 evidence verified: $($checks.Count) checks, $($http.Count) HTTP checks; API restart, same cookie and cleanup confirmed."
    }
}

finally {
    try {
        if ($container) { docker rm --force $container 2>$null | Out-Null }
    }
    finally {
        [Environment]::SetEnvironmentVariable('COURSEWORK_TEST_POSTGRES', $savedConnection)
        [Environment]::SetEnvironmentVariable('POSTGRES_PASSWORD', $savedPassword)
        [Environment]::SetEnvironmentVariable('COURSEWORK_LIVE_RUN_ID', $savedRunId)
        Pop-Location
    }
}
