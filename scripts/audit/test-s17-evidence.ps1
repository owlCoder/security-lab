#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = Join-Path $tempRoot "securitylab-s17-guard-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $root
$writer = Join-Path $PSScriptRoot 'write-s17-evidence.ps1'
$count = 0
try {
foreach ($scenario in 'passed', 'snapshot', 'partial', 'strict-partial', 'skipped', 'missing-fixture', 'missing-result',
    'failed', 'other-module-failed', 'empty') {
    $directory = Join-Path $root $scenario
    $null = New-Item -ItemType Directory -Path $directory
    $fixture = 'Audit.EvidenceTests'
    $pending = if ($scenario -in 'partial', 'strict-partial') { @('Not integrated.') } else { @() }
    @(@{ id = 'T01'; requirement = 'Synthetic guard check'; fixtures = @($fixture); pending = $pending }) |
        ConvertTo-Json -Depth 5 -AsArray | Set-Content -LiteralPath (Join-Path $directory 'matrix.json')
    if ($scenario -eq 'snapshot') {
        @{
            commit = '0000000000000000000000000000000000000000'
            dirtyWorkingTree = $true
            startedAtUtc = '2026-11-24T12:00:00Z'
        } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'run-context.json')
    }
    [xml]$trx = @'
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <TestDefinitions><UnitTest id="test-1" name="Example">
    <TestMethod className="Audit.EvidenceTests" name="Example" />
  </UnitTest></TestDefinitions>
  <Results><UnitTestResult testId="test-1" testName="Example" outcome="Passed">
    <Output><StdOut>synthetic-sensitive-marker-must-not-be-copied</StdOut></Output>
  </UnitTestResult></Results>
</TestRun>
'@
    switch ($scenario) {
        'skipped' { $trx.TestRun.Results.UnitTestResult.outcome = 'NotExecuted' }
        'failed' { $trx.TestRun.Results.UnitTestResult.outcome = 'Failed' }
        'missing-fixture' { $trx.TestRun.TestDefinitions.UnitTest.TestMethod.className = 'Other.Tests' }
        'missing-result' { $null = $trx.TestRun.Results.RemoveChild($trx.TestRun.Results.FirstChild) }
        'other-module-failed' {
            $trx.TestRun.TestDefinitions.UnitTest.TestMethod.className = 'Other.Tests'
            $trx.TestRun.Results.UnitTestResult.outcome = 'Failed'
        }
    }
    if ($scenario -ne 'empty') { $trx.Save((Join-Path $directory 'synthetic.trx')) }
    $shouldFail = $scenario -notin 'passed', 'snapshot', 'partial'
    $failed = $false
    try {
        & $writer -ResultsDirectory $directory -MatrixPath (Join-Path $directory 'matrix.json') `
            -RequireComplete:($scenario -eq 'strict-partial') | Out-Null
    } catch {
        if ($_.Exception.Message -notmatch '^Required S-17 tests|^S-17 has pending') { throw }
        $failed = $true
    }
    if ($failed -ne $shouldFail) { throw "Unexpected evidence guard outcome: $scenario." }
    $json = Get-Content -Raw -LiteralPath (Join-Path $directory 's17-evidence.json')
    $expected = if ($shouldFail -and $scenario -ne 'strict-partial') { 'NotVerified' }
        elseif ($scenario -in 'partial', 'strict-partial') { 'Partial' } else { 'Passed' }
    if (($json | ConvertFrom-Json).status -ne $expected -or
        $json.Contains('synthetic-sensitive-marker-must-not-be-copied')) {
        throw "Wrong status or captured output leaked: $scenario."
    }
    if ($scenario -eq 'snapshot') {
        $report = $json | ConvertFrom-Json
        if ($report.commit -ne '0000000000000000000000000000000000000000' -or
            -not $report.dirtyWorkingTree -or $report.sourceContext -ne 'CapturedBeforeTestRun' -or
            [DateTimeOffset]$report.startedAtUtc -ne [DateTimeOffset]'2026-11-24T12:00:00Z') {
            throw 'The pre-test source snapshot was not preserved.'
        }
    }
    $count++
}
Write-Output "S-17 evidence guard: $count scenarios passed."
} finally {
    $resolved = [IO.Path]::GetFullPath($root)
    if ([IO.Path]::GetDirectoryName($resolved) -ne $tempRoot -or
        -not [IO.Path]::GetFileName($resolved).StartsWith('securitylab-s17-guard-')) {
        throw 'Refusing cleanup outside the temporary evidence-test directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
