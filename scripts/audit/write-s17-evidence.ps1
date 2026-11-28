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
    @($groups | Where-Object { [string]::IsNullOrWhiteSpace($_.id) -or @($_.methods).Count -eq 0 }).Count -gt 0) {
    throw 'S-17 matrix must contain uniquely named groups with required methods.'
}
foreach ($group in $groups) {
    foreach ($method in $group.methods) {
        if ($method -notmatch '^[A-Za-z_][A-Za-z0-9_.]+\.[A-Za-z_][A-Za-z0-9_]*$') {
            throw "Invalid fully qualified method in group $($group.id)."
        }
    }
}
$requiredFixtures = @($groups.methods | ForEach-Object {
    $_.Substring(0, $_.LastIndexOf('.'))
} | Sort-Object -Unique -CaseSensitive)
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
        # NUnit puts parameter values in TestMethod.name; the matrix identifies the C# method.
        $method = ([string]$test.TestMethod.name -split '\(', 2)[0]
        $definitions[$test.id] = @{
            fixture = $fixture; method = $method; fullMethod = "$fixture.$method"; name = [string]$test.name
        }
    }
    foreach ($result in $document.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns)) {
        if ($result.outcome -in 'Failed', 'Error', 'Timeout', 'Aborted') {
            $problems.Add("Backend test did not pass: $($result.testName) ($($result.outcome)).")
        }
        if (-not $definitions.ContainsKey($result.testId)) { continue }
        $definition = $definitions[$result.testId]
        $seen[$result.testId] = $true
        if ($definition.fixture -cnotin $requiredFixtures) { continue }
        $cases.Add([ordered]@{
            fixture = $definition.fixture
            method = $definition.method
            fullMethod = $definition.fullMethod
            name = [string]$result.testName
            outcome = [string]$result.outcome
            trx = [IO.Path]::GetRelativePath($ResultsDirectory, $file.FullName)
        })
    }
    foreach ($id in $definitions.Keys) {
        if (-not $seen.ContainsKey($id)) {
            $problems.Add("Defined test has no result: $($definitions[$id].name).")
            $definition = $definitions[$id]
            if ($definition.fixture -cin $requiredFixtures) {
                $cases.Add([ordered]@{
                    fixture = $definition.fixture; method = $definition.method; fullMethod = $definition.fullMethod
                    name = $definition.name; outcome = 'MissingResult'
                    trx = [IO.Path]::GetRelativePath($ResultsDirectory, $file.FullName)
                })
            }
        }
    }
}
$summary = @(
    foreach ($group in $groups) {
        $fixtures = @($group.methods | ForEach-Object { $_.Substring(0, $_.LastIndexOf('.')) } | Sort-Object -Unique -CaseSensitive)
        $selected = @($cases | Where-Object { $_.fixture -cin $fixtures })
        $missing = @($group.methods | Where-Object { $_ -cnotin $selected.fullMethod })
        $notPassed = @($selected | Where-Object outcome -NE 'Passed')
        foreach ($method in $missing) { $problems.Add("$($group.id): missing required method $method.") }
        foreach ($test in $notPassed) { $problems.Add("$($group.id): $($test.name) is $($test.outcome).") }
        $status = if ($missing.Count -gt 0 -or $notPassed.Count -gt 0) { 'NotVerified' }
            elseif ($group.pending.Count -gt 0) { 'Partial' } else { 'Passed' }
        [ordered]@{
            id = $group.id; requirement = $group.requirement; status = $status
            passed = @($selected | Where-Object outcome -EQ 'Passed').Count
            notPassed = $notPassed.Count; requiredMethods = @($group.methods); missingMethods = $missing
            pending = @($group.pending)
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
