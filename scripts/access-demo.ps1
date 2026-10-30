param([string]$BaseUrl = "http://localhost:5080")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.CookieContainer = New-Object System.Net.CookieContainer
$client = New-Object System.Net.Http.HttpClient($handler)
$client.BaseAddress = [Uri]$BaseUrl
$results = New-Object 'System.Collections.Generic.List[object]'

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

function Test-Read([string]$Name, [int]$Expected, [string]$Suffix, [string]$ExpectedAnswer = "") {
    $response = $client.GetAsync("/api/access-demo/attempts/30000000-0000-0000-0000-$Suffix").GetAwaiter().GetResult()
    try {
        $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $status = [int]$response.StatusCode
        if ($status -ne $Expected) { throw "$Name : expected $Expected, got $status. $body" }
        if ($ExpectedAnswer -and (($body | ConvertFrom-Json).answer -ne $ExpectedAnswer)) { throw "$Name : wrong answer." }
        if ($status -eq 404 -and $body -match 'answer|ReasonCode|CONTEXT_EXPIRED|RESOURCE_NOT_FOUND') { throw "$Name : response leaks internal data." }
        $results.Add([pscustomobject]@{ scenario = $Name; status = $status; body = $body; passed = $true })
        Write-Host "PASS $Name : HTTP $status"
        return $body
    } finally { $response.Dispose() }
}

try {
    $null = Test-Read "Anonymous" 401 "000000000001"
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
    $outDir = Join-Path (Split-Path $PSScriptRoot -Parent) "TestResults"
    $null = New-Item -ItemType Directory -Force -Path $outDir
    $results | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $outDir "access-http-demo.json")
    Write-Host "DEMO PASS: $($results.Count)/12. Evidence: TestResults/access-http-demo.json"
} finally { $client.Dispose(); $handler.Dispose() }
