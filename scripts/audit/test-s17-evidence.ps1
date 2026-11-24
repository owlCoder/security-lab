#requires -Version 7.4
$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)
$root = Join-Path $tempRoot "securitylab-s17-guard-$([Guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $root
$writer = Join-Path $PSScriptRoot 'write-s17-evidence.ps1'

function Add-SyntheticTest($document, $id, $fixture, $method, $outcome = 'Passed', $withResult = $true) {
    $namespace = $document.DocumentElement.NamespaceURI
    $test = $document.CreateElement('UnitTest', $namespace)
    $test.SetAttribute('id', $id)
    $test.SetAttribute('name', $method)
    $definition = $document.CreateElement('TestMethod', $namespace)
    $definition.SetAttribute('className', $fixture)
    $definition.SetAttribute('name', $method)
    $null = $test.AppendChild($definition)
    $null = $document.TestRun.TestDefinitions.AppendChild($test)
    if ($withResult) {
        $result = $document.CreateElement('UnitTestResult', $namespace)
        $result.SetAttribute('testId', $id)
        $result.SetAttribute('testName', $method)
        $result.SetAttribute('outcome', $outcome)
        $null = $document.TestRun.Results.AppendChild($result)
    }
}

$count = 0
try {
    foreach ($scenario in 'passed', 'snapshot', 'partial', 'strict-partial', 'skipped', 'missing-fixture',
        'missing-method', 'missing-result', 'failed', 'other-module-failed', 'parameterized', 'parameter-skipped', 'empty') {
        $directory = Join-Path $root $scenario
        $null = New-Item -ItemType Directory -Path $directory
        $fixture = 'Audit.EvidenceTests'
        $methods = @("$fixture.Example")
        $pending = if ($scenario -in 'partial', 'strict-partial') { @('Not integrated.') } else { @() }
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
        $expectedProblem = $null
        switch ($scenario) {
            'skipped' {
                $trx.TestRun.Results.UnitTestResult.outcome = 'NotExecuted'
                $expectedProblem = 'T01: Example is NotExecuted.'
            }
            'failed' {
                $trx.TestRun.Results.UnitTestResult.outcome = 'Failed'
                $expectedProblem = 'Backend test did not pass: Example (Failed).'
            }
            'missing-fixture' {
                $trx.TestRun.TestDefinitions.UnitTest.TestMethod.className = 'Other.Tests'
                $expectedProblem = "T01: missing required method $fixture.Example."
            }
            'missing-method' {
                $methods += "$fixture.DeletedOrFiltered"
                $expectedProblem = "T01: missing required method $fixture.DeletedOrFiltered."
            }
            'missing-result' {
                $methods += "$fixture.MissingResult"
                Add-SyntheticTest $trx 'test-2' $fixture 'MissingResult' 'Passed' $false
                $expectedProblem = 'Defined test has no result: MissingResult.'
            }
            'other-module-failed' {
                Add-SyntheticTest $trx 'test-2' 'Other.Tests' 'Broken' 'Failed'
                $expectedProblem = 'Backend test did not pass: Broken (Failed).'
            }
            'empty' { $expectedProblem = 'No TRX files were produced.' }
        }
        if ($scenario -in 'parameterized', 'parameter-skipped') {
            $trx.TestRun.TestDefinitions.UnitTest.TestMethod.name = 'Example(1)'
            $trx.TestRun.TestDefinitions.UnitTest.name = 'Example(1)'
            $trx.TestRun.Results.UnitTestResult.testName = 'Example(1)'
            $outcome = if ($scenario -eq 'parameter-skipped') { 'NotExecuted' } else { 'Passed' }
            Add-SyntheticTest $trx 'test-2' $fixture 'Example(2)' $outcome
            if ($scenario -eq 'parameter-skipped') { $expectedProblem = 'T01: Example(2) is NotExecuted.' }
        }
        @(@{ id = 'T01'; requirement = 'Synthetic guard check'; methods = $methods; pending = $pending }) |
            ConvertTo-Json -Depth 5 -AsArray | Set-Content -LiteralPath (Join-Path $directory 'matrix.json')
        if ($scenario -eq 'snapshot') {
            @{
                commit = '0000000000000000000000000000000000000000'
                dirtyWorkingTree = $true
                startedAtUtc = '2026-11-24T12:00:00Z'
            } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $directory 'run-context.json')
        }
        if ($scenario -ne 'empty') { $trx.Save((Join-Path $directory 'synthetic.trx')) }
        $shouldFail = $scenario -notin 'passed', 'snapshot', 'partial', 'parameterized'
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
        $report = $json | ConvertFrom-Json
        $expected = if ($shouldFail -and $scenario -ne 'strict-partial') { 'NotVerified' }
            elseif ($scenario -in 'partial', 'strict-partial') { 'Partial' } else { 'Passed' }
        if ($report.status -ne $expected -or $json.Contains('synthetic-sensitive-marker-must-not-be-copied')) {
            throw "Wrong status or captured output leaked: $scenario."
        }
        if ($expectedProblem -and $expectedProblem -notin $report.problems) {
            throw "The intended failure was not detected: $scenario."
        }
        $expectedGroup = if ($scenario -eq 'other-module-failed') { 'Passed' } else { $expected }
        if ($report.groups[0].status -ne $expectedGroup) { throw "Wrong group status: $scenario." }
        if ($scenario -in 'missing-result', 'other-module-failed') {
            if ($report.groups[0].missingMethods.Count -gt 0 -or
                @($report.problems | Where-Object { $_ -match 'missing required method' }).Count -gt 0) {
                throw "A missing method masked the intended failure: $scenario."
            }
            $groupStatus = if ($scenario -eq 'missing-result') { 'NotVerified' } else { 'Passed' }
            if ($report.groups[0].status -ne $groupStatus) { throw "Wrong group status: $scenario." }
            if (@($report.tests | Where-Object { $_.method -eq 'Example' -and $_.outcome -eq 'Passed' }).Count -ne 1) {
                throw "The passing required test was lost: $scenario."
            }
        }
        if ($scenario -eq 'snapshot' -and (
            $report.commit -ne '0000000000000000000000000000000000000000' -or
            -not $report.dirtyWorkingTree -or $report.sourceContext -ne 'CapturedBeforeTestRun' -or
            [DateTimeOffset]$report.startedAtUtc -ne [DateTimeOffset]'2026-11-24T12:00:00Z')) {
            throw 'The pre-test source snapshot was not preserved.'
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
