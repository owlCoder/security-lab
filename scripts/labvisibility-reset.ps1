# AUT-04 (KT2-09): resets the local LabVisibility data to the demo seed with one command.
# Drops only the lab_visibility schema in the local docker-compose database, then starts the API once
# in Development so the real startup code applies the InitialLabVisibility migration and the demo seed
# (labs 4444... published and 5555... draft). Other modules' schemas are not touched.
# Local development only; never point it at a shared or production database.
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [int]$Port = 5089
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Read-EnvFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing $path. Copy .env.example to .env and set POSTGRES_PASSWORD (README, local PostgreSQL)."
    }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $path) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#') -or -not $trimmed.Contains('=')) { continue }
        $parts = $trimmed.Split('=', 2)
        $values[$parts[0].Trim()] = $parts[1].Trim()
    }
    foreach ($key in 'POSTGRES_USER', 'POSTGRES_DB', 'POSTGRES_PASSWORD') {
        if (-not $values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($values[$key])) {
            throw "$key is missing in .env."
        }
    }
    if ($values['POSTGRES_PASSWORD'] -like 'CHANGE_ME*') {
        throw 'Set your own POSTGRES_PASSWORD in .env first.'
    }
    return $values
}

# Docker Desktop has "docker compose"; older installs only "docker-compose".
function Invoke-Compose {
    if ($script:composeCommand -eq 'docker-compose') { & docker-compose @args } else { & docker compose @args }
}

function Invoke-Psql([hashtable]$envValues, [string]$sql) {
    $output = Invoke-Compose exec -T db psql -v ON_ERROR_STOP=1 -t -A `
        -U $envValues['POSTGRES_USER'] -d $envValues['POSTGRES_DB'] -c $sql
    if ($LASTEXITCODE -ne 0) { throw "psql failed: $sql" }
    return $output
}

$envValues = Read-EnvFile (Join-Path $root '.env')

$script:composeCommand = 'docker compose'
# Windows PowerShell 5.1 turns native stderr into errors under 'Stop', hence try/catch.
try { & docker compose version *> $null; if ($LASTEXITCODE -ne 0) { $script:composeCommand = 'docker-compose' } }
catch { $script:composeCommand = 'docker-compose' }

Write-Host '1/4 Starting the local PostgreSQL service (docker compose, 127.0.0.1:5432)...'
Invoke-Compose up -d db
if ($LASTEXITCODE -ne 0) {
    throw 'docker compose could not start the db service. Is Docker Desktop running? Is port 5432 taken by a local PostgreSQL service?'
}
$healthy = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    $id = (Invoke-Compose ps -q db | Select-Object -First 1)
    if ($id) {
        $status = $null
        try { $status = (& docker inspect --format '{{.State.Health.Status}}' $id) } catch { }
        if ($status -eq 'healthy') { $healthy = $true; break }
    }
    Start-Sleep -Seconds 1
}
if (-not $healthy) { throw 'The db service did not become healthy.' }

Write-Host '2/4 Dropping only the lab_visibility schema...'
Invoke-Psql $envValues 'DROP SCHEMA IF EXISTS lab_visibility CASCADE;' | Out-Null

$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder['Host'] = '127.0.0.1'
$builder['Port'] = '5432'
$builder['Database'] = $envValues['POSTGRES_DB']
$builder['Username'] = $envValues['POSTGRES_USER']
$builder['Password'] = $envValues['POSTGRES_PASSWORD']

if (-not $NoBuild) {
    & dotnet build backend/Platform.Api/Platform.Api.csproj --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
}

Write-Host '3/4 Starting the API once: migration + demo seed...'
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$previousConnection = $env:ConnectionStrings__LabVisibility
$log = Join-Path ([System.IO.Path]::GetTempPath()) ("labvisibility-reset-" + [Guid]::NewGuid().ToString('N') + '.log')
$api = $null
try {
    # Only for the child process; never written to disk or to Git.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:ConnectionStrings__LabVisibility = $builder.ConnectionString

    # The built DLL directly (as in CI), so stopping the process really stops the API.
    $dll = Join-Path $root 'backend/Platform.Api/bin/Debug/net10.0/Platform.Api.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw "Missing $dll. Run without -NoBuild." }
    $api = Start-Process -FilePath 'dotnet' -PassThru -NoNewWindow `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" `
        -ArgumentList @($dll, '--contentRoot', (Join-Path $root 'backend/Platform.Api'), '--urls', "http://127.0.0.1:$Port")

    $ready = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if ($api.HasExited) { break }
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/api/health" -TimeoutSec 2
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        Get-Content -LiteralPath $log -ErrorAction SilentlyContinue | Select-Object -Last 30
        throw 'The API did not start; migration and seed were not confirmed.'
    }
}
finally {
    if ($api -and -not $api.HasExited) { Stop-Process -Id $api.Id -Force -ErrorAction SilentlyContinue }
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    $env:ConnectionStrings__LabVisibility = $previousConnection
}

Write-Host '4/4 Checking the seed...'
$labs = [int](Invoke-Psql $envValues 'SELECT count(*) FROM lab_visibility.lab_public_content;')
$tasks = [int](Invoke-Psql $envValues 'SELECT count(*) FROM lab_visibility.lab_public_task;')
$keys = [int](Invoke-Psql $envValues 'SELECT count(*) FROM lab_visibility.lab_evaluator_key;')
$answerTables = @(Invoke-Psql $envValues "SELECT table_name FROM information_schema.columns WHERE table_schema = 'lab_visibility' AND column_name ILIKE '%answer%';")

if ($labs -ne 2 -or $tasks -ne 3 -or $keys -ne 3) {
    throw "Unexpected seed: $labs labs, $tasks tasks, $keys key rows (expected 2, 3, 3)."
}
if ($answerTables.Count -ne 1 -or $answerTables[0] -ne 'lab_evaluator_key') {
    throw 'An answer column exists outside lab_evaluator_key.'
}

Remove-Item -LiteralPath $log, "$log.err" -ErrorAction SilentlyContinue
Write-Host "LabVisibility reset PASS: $labs labs (4444... published, 5555... draft), $tasks tasks, $keys key rows; answers only in lab_evaluator_key."
