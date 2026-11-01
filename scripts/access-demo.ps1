param([string]$BaseUrl = "http://localhost:5080")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.CookieContainer = New-Object System.Net.CookieContainer
$client = New-Object System.Net.Http.HttpClient($handler)
$client.BaseAddress = [Uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromSeconds(20)
$results = New-Object 'System.Collections.Generic.List[object]'
$expectedCount = 25

function Set-DemoIdentity([string]$Persona) {
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "/api/access-demo/session/$Persona")
    $request.Headers.Add("X-Access-Demo", "1")
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne 200) { throw "Demo session failed. Enable Development and AccessDemo__Enabled=true; HTTP $([int]$response.StatusCode)." }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Confirm-Response($Response, [string]$Name, [int]$Expected, [string]$ExpectedAnswer = "") {
    $body = $Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $status = [int]$Response.StatusCode
    if ($status -ne $Expected) { throw "$Name : expected $Expected, got $status. $body" }
    if ($ExpectedAnswer -and (($body | ConvertFrom-Json).answer -ne $ExpectedAnswer)) { throw "$Name : wrong answer." }
    if ($status -eq 404 -and $body -match 'answer|ReasonCode|CONTEXT_EXPIRED|RESOURCE_NOT_FOUND') { throw "$Name : response leaks internal data." }
    $results.Add([pscustomobject]@{ scenario = $Name; status = $status; body = $body; passed = $true })
    Write-Host "PASS $Name : HTTP $status"
    return $body
}

function Test-Read([string]$Name, [int]$Expected, [string]$Suffix, [string]$ExpectedAnswer = "") {
    $response = $client.GetAsync("/api/access-demo/attempts/30000000-0000-0000-0000-$Suffix").GetAwaiter().GetResult()
    try { return Confirm-Response $response $Name $Expected $ExpectedAnswer }
    finally { $response.Dispose() }
}

function Test-Update([string]$Name, [int]$Expected, [string]$Suffix,
    [string]$Answer, [int]$Version, [bool]$WithHeader = $true) {
    $path = "/api/access-demo/attempts/30000000-0000-0000-0000-$Suffix/answer"
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Put, $path)
    if ($WithHeader) { $request.Headers.Add("X-Access-Demo", "1") }
    $json = @{ answer = $Answer; expectedVersion = $Version } | ConvertTo-Json -Compress
    $request.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, "application/json")
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try { return Confirm-Response $response $Name $Expected }
        finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

try {
    $null = Test-Read "Anonymous" 401 "000000000001"
    $null = Test-Update "Anonymous update" 401 "000000000004" "Anoniman upis." 1
    Set-DemoIdentity "ana"
    $null = Test-Read "ACC-04 owner" 200 "000000000001" "Sinteticki Anin odgovor."
    $foreign = Test-Read "ACC-04 foreign owner" 404 "000000000002"
    $missing = Test-Read "Missing resource" 404 "000000000099"
    if ($foreign -ne $missing) { throw "Foreign and missing public responses differ." }
    Set-DemoIdentity "marko"
    $null = Test-Read "IDOR by changed ID" 404 "000000000001"
    Set-DemoIdentity "ana"
    $null = Test-Read "ACC-08 expired context denies owner" 404 "000000000003"
    Set-DemoIdentity "teacher"
    $null = Test-Read "Course teacher" 200 "000000000001" "Sinteticki Anin odgovor."
    $null = Test-Read "Unsubmitted answer remains private" 404 "000000000004"
    Set-DemoIdentity "foreign-teacher"
    $null = Test-Read "Other course teacher" 404 "000000000001"
    Set-DemoIdentity "blocked"
    $null = Test-Read "Blocked account" 404 "000000000001"
    Set-DemoIdentity "assistant"
    $null = Test-Read "Assistant without delegation" 404 "000000000001"
    Set-DemoIdentity "admin"
    $null = Test-Read "No global admin bypass" 404 "000000000001"

    Set-DemoIdentity "ana"
    $before = (Test-Read "Owner reads draft before update" 200 "000000000004") | ConvertFrom-Json
    $version = [int]$before.version
    $answer = "KT1: sacuvan sopstveni nacrt."
    $updated = (Test-Update "Owner updates own draft" 200 "000000000004" $answer $version) | ConvertFrom-Json
    if ($updated.answer -ne $answer -or [int]$updated.version -ne $version + 1) { throw "Update did not return the next authorized version." }
    $after = (Test-Read "Updated answer is stored" 200 "000000000004" $answer) | ConvertFrom-Json
    if ([int]$after.version -ne $version + 1) { throw "Read did not return the stored version." }
    $null = Test-Update "Stale update rejected" 409 "000000000004" "Zastareo upis." $version
    Set-DemoIdentity "marko"
    $foreignWrite = Test-Update "Foreign draft update denied" 404 "000000000004" "Tudj upis." 999
    $missingWrite = Test-Update "Missing draft update denied" 404 "000000000099" "Tudj upis." 999
    if ($foreignWrite -ne $missingWrite) { throw "Foreign and missing write responses differ." }
    Set-DemoIdentity "teacher"
    $null = Test-Update "Teacher cannot edit student draft" 404 "000000000004" "Nastavnicki upis." 1
    Set-DemoIdentity "ana"
    $null = Test-Update "Expired context blocks draft update" 404 "000000000006" "Kasni upis." 1
    $null = Test-Update "Submitted answer cannot be overwritten" 404 "000000000001" "Prepisivanje." 1
    $null = Test-Update "Invalid draft input rejected" 400 "000000000004" "   " 1
    $null = Test-Update "Missing write header rejected" 400 "000000000004" "Upis." 1 $false
    $null = Test-Update "Completed answer cannot be overwritten" 404 "000000000005" "Prepisivanje." 1

    if ($results.Count -ne $expectedCount) { throw "Unexpected scenario count: $($results.Count)/$expectedCount." }
    $outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "TestResults"
    $null = New-Item -ItemType Directory -Force -Path $outDir
    $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $outDir "access-http-demo.json")
    Write-Host "DEMO PASS: $($results.Count)/$expectedCount. Evidence: TestResults/access-http-demo.json"
} finally { $client.Dispose(); $handler.Dispose() }
