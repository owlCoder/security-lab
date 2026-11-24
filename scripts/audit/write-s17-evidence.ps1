#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResultsDirectory,
    [string]$MatrixPath = (Join-Path $PSScriptRoot 's17-matrix.json'),
    [switch]$RequireComplete
)

$ErrorActionPreference = 'Stop'
$groups = @(Get-Content -Raw -LiteralPath $MatrixPath | ConvertFrom-Json)
if ($groups.Count -eq 0 -or @($groups.id | Sort-Object -Unique).Count -ne $groups.Count -or
    @($groups | Where-Object { [string]::IsNullOrWhiteSpace($_.id) -or @($_.fixtures).Count -eq 0 }).Count -gt 0) {
    throw 'S-17 matrix must contain uniquely named groups with required fixtures.'
}
$requiredFixtures = @($groups.fixtures | Sort-Object -Unique)
$cases = [Collections.Generic.List[object]]::new()
$problems = [Collections.Generic.List[string]]::new()
$files = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -Recurse)
if ($files.Count -eq 0) { $problems.Add('No TRX files were produced.') }
foreach ($file in $files) {
    # Read only definitions/results, never captured output that could contain credentials.
    $reader = [Xml.XmlReader]::Create($file.FullName, [Xml.XmlReaderSettings]@{
        DtdProcessing = [Xml.DtdProcessing]::Prohibit
        XmlResolver = $null
    })
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
    } finally { $reader.Dispose() }
    $ns = [Xml.XmlNamespaceManager]::new($document.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $definitions = @{}
    $seen = @{}
    foreach ($test in $document.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns)) {
        $fixture = ($test.TestMethod.className -split ',', 2)[0]
        if ($fixture -in $requiredFixtures) {
            $definitions[$test.id] = $test
        }
    }
    foreach ($result in $document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns)) {
        if ($result.outcome -in 'Failed', 'Error', 'Timeout', 'Aborted') {
            $problems.Add("Backend test did not pass: $($result.testName) ($($result.outcome)).")
        }
        if (-not $definitions.ContainsKey($result.testId)) { continue }
        $definition = $definitions[$result.testId]
        $seen[$result.testId] = $true
        $cases.Add([ordered]@{
            fixture = ($definition.TestMethod.className -split ',', 2)[0]
            method = [string]$definition.TestMethod.name
            name = [string]$result.testName
            outcome = [string]$result.outcome
            trx = [IO.Path]::GetRelativePath($ResultsDirectory, $file.FullName)
        })
    }
    foreach ($id in $definitions.Keys) {
        if (-not $seen.ContainsKey($id)) {
            $problems.Add("Defined test has no result: $($definitions[$id].name).")
        }
    }
}
$summary = @(
    foreach ($group in $groups) {
        $selected = @($cases | Where-Object { $_.fixture -in $group.fixtures })
        $missing = @($group.fixtures | Where-Object { $_ -notin $selected.fixture })
        $notPassed = @($selected | Where-Object outcome -NE 'Passed')
        foreach ($fixture in $missing) { $problems.Add("$($group.id): missing fixture $fixture.") }
        foreach ($test in $notPassed) { $problems.Add("$($group.id): $($test.name) is $($test.outcome).") }
        $status = if ($missing.Count -gt 0 -or $notPassed.Count -gt 0) { 'NotVerified' }
            elseif ($group.pending.Count -gt 0) { 'Partial' } else { 'Passed' }
        [ordered]@{
            id = $group.id; requirement = $group.requirement; status = $status
            passed = @($selected | Where-Object outcome -EQ 'Passed').Count
            notPassed = $notPassed.Count; missingFixtures = $missing; pending = @($group.pending)
        }
    }
)
$repositoryRoot = Join-Path $PSScriptRoot '../..'
$sha = git -C $repositoryRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the tested commit.' }
$changes = @(git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Cannot determine working tree state.' }
$contextPath = Join-Path $ResultsDirectory 'run-context.json'
$context = if (Test-Path -LiteralPath $contextPath) { Get-Content -Raw -LiteralPath $contextPath | ConvertFrom-Json }
    else { $null }
$report = [ordered]@{
    story = 'S-17'
    commit = if ($context) { $context.commit } else { $sha }
    dirtyWorkingTree = if ($context) { $context.dirtyWorkingTree } else { $changes.Count -gt 0 }
    sourceContext = if ($context) { 'CapturedBeforeTestRun' } else { 'CurrentCheckoutAtReportTime' }
    startedAtUtc = if ($context) { ([DateTimeOffset]$context.startedAtUtc).ToUniversalTime().ToString('O') } else { $null }
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    status = if ($problems.Count -gt 0) { 'NotVerified' }
        elseif (@($summary | Where-Object status -NE 'Passed').Count -gt 0) { 'Partial' } else { 'Passed' }
    groups = $summary; problems = @($problems); tests = @($cases.ToArray())
}
$outputPath = Join-Path $ResultsDirectory 's17-evidence.json'
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outputPath -Encoding utf8
$summary | ForEach-Object { Write-Output "$($_.id): $($_.status) ($($_.passed) passed cases)" }
Write-Output "S-17 evidence: $outputPath; overall $($report.status)"
if ($problems.Count -gt 0) { throw 'Required S-17 tests failed, were skipped, or are missing. See s17-evidence.json.' }
if ($RequireComplete -and $report.status -ne 'Passed') { throw 'S-17 has pending integration/operational evidence.' }
