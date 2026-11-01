[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$TapizProjectUrl,
    [string]$TaskId,
    [string]$TaskUrl,
    [string]$ContractReviewer,
    [string]$ContractReviewUrl,
    [switch]$ReviewAccepted,
    [string]$CiRunUrl,
    [string]$CiCommit,
    [switch]$CiSucceeded
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root "docs/components/access/EVIDENCE.json"
$data = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json

function Confirm-Reference([string]$Value, [string]$Label) {
    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne "https" -or $uri.UserInfo -or
        $uri.Query -match '(?i)(password|secret|access_token|refresh_token)=') {
        throw "$Label must be an HTTPS evidence URL without credentials/tokens."
    }
}

$changed = $false
if ($TapizProjectUrl) {
    Confirm-Reference $TapizProjectUrl "Tapiz project"
    $data.tapiz.projectUrl = $TapizProjectUrl
    $changed = $true
}
if ($TaskId -or $TaskUrl) {
    if (-not $TaskId -or -not $TaskUrl) { throw "Supply both -TaskId and -TaskUrl." }
    if ($data.tapiz.taskUrls.PSObject.Properties.Name -notcontains $TaskId) { throw "Unknown task ID. Use prepare-access-task.ps1 -List." }
    Confirm-Reference $TaskUrl "Task reference"
    $data.tapiz.taskUrls.$TaskId = $TaskUrl
    $changed = $true
}
if ($ContractReviewer) {
    if ([string]::IsNullOrWhiteSpace($ContractReviewer)) { throw "Reviewer must identify the real reviewing person/team." }
    $data.contractReview.reviewer = $ContractReviewer
    $changed = $true
}
if ($ContractReviewUrl) {
    Confirm-Reference $ContractReviewUrl "Contract review"
    $data.contractReview.reviewUrl = $ContractReviewUrl
    $data.contractReview.status = "REVIEW_LINK_RECORDED"
    $changed = $true
}
if ($ReviewAccepted) {
    if (-not $data.contractReview.reviewer -or -not $data.contractReview.reviewUrl) {
        throw "Record a real reviewer and actual confirmation URL before -ReviewAccepted."
    }
    $data.contractReview.status = "ACCEPTED_AS_RECORDED"
    $changed = $true
}
if ($CiRunUrl -or $CiCommit -or $CiSucceeded) {
    if (-not $CiRunUrl -or -not $CiCommit) { throw "Supply both -CiRunUrl and -CiCommit for the prepared update." }
    Confirm-Reference $CiRunUrl "CI run"
    if ($CiRunUrl -notmatch '^https://github\.com/owlCoder/security-lab/actions/runs/[0-9]+/?$' -or
        $CiCommit -notmatch '^[0-9a-fA-F]{7,40}$') { throw "Use the actual Security Lab Actions run URL and its commit SHA." }
    $data.preparedUpdate.ciRunUrl = $CiRunUrl
    $data.preparedUpdate.ciCommit = $CiCommit
    $data.preparedUpdate.ciStatus = if ($CiSucceeded) { "SUCCESS_AS_RECORDED" } else { "PENDING_VERIFICATION" }
    $changed = $true
}
if (-not $changed) { throw "No evidence supplied. This command records real references; it does not create tasks, obtain approval or run CI." }
if ($PSCmdlet.ShouldProcess($path, "Record supplied evidence references")) {
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($path, (($data | ConvertTo-Json -Depth 12) + [Environment]::NewLine), $utf8)
    Write-Host "Evidence references recorded. Check their actual content before claiming KT1 completion."
}
