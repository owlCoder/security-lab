$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$envPath = Join-Path $repositoryRoot '.env'
if (-not (Test-Path -LiteralPath $envPath)) {
    throw 'Create the local .env and start the PostgreSQL service before bootstrapping Audit.'
}

$envContent = Get-Content -LiteralPath $envPath -Raw
$passwordLines = [regex]::Matches($envContent, '(?m)^AUDIT_RUNTIME_PASSWORD=([^\r\n]*)')
if ($passwordLines.Count -gt 1) {
    throw 'The local .env must contain at most one AUDIT_RUNTIME_PASSWORD entry.'
}

$password = if ($passwordLines.Count -eq 1) { $passwordLines[0].Groups[1].Value.Trim() } else { '' }
$generatePassword = [string]::IsNullOrWhiteSpace($password) -or $password.StartsWith('CHANGE_ME_')
if ($generatePassword) {
    $randomBytes = [byte[]]::new(32)
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($randomBytes) } finally { $generator.Dispose() }
    $password = [BitConverter]::ToString($randomBytes).Replace('-', '').ToLowerInvariant()
}

# Keep this local setting literal; Compose interpolation must not change the password.
if ($password -notmatch '^[A-Za-z0-9_-]{32,}$') {
    throw 'Use an unquoted AUDIT_RUNTIME_PASSWORD of at least 32 letters, digits, underscores or hyphens.'
}

$previousPassword = $env:AUDIT_RUNTIME_PASSWORD
Push-Location $repositoryRoot
try {
    $env:AUDIT_RUNTIME_PASSWORD = $password
    Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bootstrap-runtime.sql') -Raw |
        docker compose --env-file .env exec -T -e AUDIT_RUNTIME_PASSWORD db sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"'
    if ($LASTEXITCODE -ne 0) {
        throw 'Audit runtime bootstrap failed. The local password file was not changed.'
    }

    if ($generatePassword) {
        $newSetting = "AUDIT_RUNTIME_PASSWORD=$password"
        if ($passwordLines.Count -eq 1) {
            $envContent = $envContent.Remove($passwordLines[0].Index, $passwordLines[0].Length)
                .Insert($passwordLines[0].Index, $newSetting)
        } else {
            $envContent = $envContent.TrimEnd() + [Environment]::NewLine + $newSetting + [Environment]::NewLine
        }
        [System.IO.File]::WriteAllText($envPath, $envContent, [System.Text.UTF8Encoding]::new($false))
    }
    Write-Output 'Audit runtime role configured; credentials remain in the ignored local .env.'
} finally {
    $env:AUDIT_RUNTIME_PASSWORD = $previousPassword
    Pop-Location
}
