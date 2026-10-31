param([switch]$Force)
$ErrorActionPreference = "Stop"

$schema = "identity"
$identityProject = "backend/Platform.Identity"

function Read-EnvFile([string]$Path) {
    if (-not (Test-Path $Path)) { throw "Missing .env. Copy .env.example to .env and set the credentials." }
    $values = @{}
    Get-Content $Path | Where-Object { $_ -match '^\s*\w+\s*=' } | ForEach-Object {
        $key, $value = $_ -split '=', 2
        $values[$key.Trim()] = $value.Trim().Trim('"', "'")
    }
    return $values
}

function Invoke-Checked([string]$FailureMessage, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $envValues = Read-EnvFile ".env"
    $user = $envValues["POSTGRES_USER"]
    $database = $envValues["POSTGRES_DB"]
    if (-not $user -or -not $database) { throw "POSTGRES_USER and POSTGRES_DB must be set in .env." }

    if (-not $Force) {
        Write-Host "This deletes all data in schema '$schema' of the local database '$database'. Other schemas are not changed."
        if ((Read-Host "Type 'reset' to continue") -cne "reset") {
            Write-Host "Cancelled. Nothing was changed."
            return
        }
    }

    Invoke-Checked "Database is not running. Start it with: docker compose up -d db" {
        docker compose exec -T db pg_isready -q -U $user -d $database
    }

    # Everything that can fail on its own runs first, so a broken build never leaves the schema dropped.
    Write-Host "Building the identity module..."
    Invoke-Checked "dotnet tool restore failed. Nothing was changed." { dotnet tool restore }
    Invoke-Checked "Build failed. Nothing was changed." { dotnet build $identityProject --nologo --verbosity quiet }

    Write-Host "Dropping schema '$schema'..."
    Invoke-Checked "Could not drop schema '$schema'." {
        docker compose exec -T db psql -q -v ON_ERROR_STOP=1 -U $user -d $database -c "DROP SCHEMA IF EXISTS $schema CASCADE"
    }

    Write-Host "Applying migrations..."
    Invoke-Checked "Migrations failed. Schema '$schema' was dropped and is not fully recreated." {
        dotnet ef database update --project $identityProject --startup-project $identityProject --no-build
    }

    Write-Host "Done. Schema '$schema' was recreated from migrations."
} finally {
    Pop-Location
}
