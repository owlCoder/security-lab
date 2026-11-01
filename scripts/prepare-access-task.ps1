param(
    [string]$TaskId,
    [switch]$List,
    [switch]$TitleOnly,
    [switch]$NoClipboard
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$data = Get-Content -LiteralPath (Join-Path $root "docs/components/access/backlog.json") -Raw -Encoding UTF8 | ConvertFrom-Json
if ($List) {
    $data.tasks | Select-Object id, phase, implementation, title | Format-Table -AutoSize
    return
}
if (-not $TaskId) { throw "Use -List or -TaskId ACC04-01." }
$task = @($data.tasks | Where-Object { $_.id -eq $TaskId })
if ($task.Count -ne 1) { throw "Unknown task ID. Use -List." }
$task = $task[0]
$title = "$($task.id) - $($task.title)"
$lines = @(
    $title,
    "",
    "Requirements: $($task.requirements -join ', ')",
    "Phase: $($task.phase)",
    "Implementation evidence: $($task.implementation)",
    "Use cases: $($task.useCases -join ', ')",
    "",
    "Prerequisites / order: $($task.prerequisites)",
    "",
    "Acceptance criteria: $($task.acceptance)",
    "",
    "Evidence: $($task.evidence -join '; ')",
    "Branch: feature/acc-04-authorization",
    "PR: https://github.com/owlCoder/security-lab/pull/18",
    "",
    "Record actual work and review dates. This prepared description does not prove prior Tapiz creation or external acceptance."
)
$content = if ($TitleOnly) { $title } else { $lines -join [Environment]::NewLine }
if ($NoClipboard) { Write-Output $content }
else {
    Set-Clipboard -Value $content
    Write-Host "Copied: $title"
    Write-Host "Paste into the real Tapiz card. No remote task was created by this command."
}
