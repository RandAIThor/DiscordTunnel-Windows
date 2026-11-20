$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseGate.ps1')
function Assert-GateTest([bool]$Condition,[string]$Description) {
    if (!$Condition) { throw $Description }
}
function Copy-GateFixture($Fixture) { $Fixture | ConvertTo-Json -Depth 8 | ConvertFrom-Json }
# Synthetic policy fixtures below are NOT application acceptance results.
$fingerprint = 'A' * 64
$fixture = [pscustomobject]@{
    schema_version = 1
    source_sha256 = $fingerprint
    checks = @($script:FinalAcceptanceChecks | ForEach-Object {
        [pscustomobject]@{id=$_;status='passed';evidence=@('synthetic-policy-test-only')}
    })
}
Assert-GateTest (@(Get-FinalAcceptanceErrors $fixture $fingerprint).Count -eq 0) 'Valid synthetic policy fixture was rejected'
foreach ($state in @('failed','partial','not_run','PASSED',$null)) {
    $changed = Copy-GateFixture $fixture
    $changed.checks[0].status = $state
    Assert-GateTest (@(Get-FinalAcceptanceErrors $changed $fingerprint).Count -gt 0) 'Unverified result unblocked packaging'
}
$changed = Copy-GateFixture $fixture
$changed.checks = @($changed.checks | Select-Object -Skip 1)
Assert-GateTest (@(Get-FinalAcceptanceErrors $changed $fingerprint).Count -gt 0) 'Deleting a required result unblocked packaging'
$changed = Copy-GateFixture $fixture
$changed.checks += $changed.checks[0]
Assert-GateTest (@(Get-FinalAcceptanceErrors $changed $fingerprint).Count -gt 0) 'Duplicate result accepted'
$changed = Copy-GateFixture $fixture
$changed.checks[0].evidence = @()
Assert-GateTest (@(Get-FinalAcceptanceErrors $changed $fingerprint).Count -gt 0) 'Result without evidence reference accepted'
Assert-GateTest (@(Get-FinalAcceptanceErrors $fixture ('B' * 64)).Count -gt 0) 'Stale source evidence accepted'
$changed = Copy-GateFixture $fixture
$changed.schema_version = 2
Assert-GateTest (@(Get-FinalAcceptanceErrors $changed $fingerprint).Count -gt 0) 'Unknown schema accepted'
$changed = Copy-GateFixture $fixture
$changed.checks += [pscustomobject]@{id='replacement';status='passed';evidence=@('synthetic-policy-test-only')}
Assert-GateTest (@(Get-FinalAcceptanceErrors $changed $fingerprint).Count -gt 0) 'Unknown result ID accepted'
$report = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RELEASE-ACCEPTANCE.json') -Raw | ConvertFrom-Json
Assert-GateTest (@(Get-FinalAcceptanceErrors $report (Get-FinalSourceFingerprint)).Count -gt 0) 'Current incomplete application acceptance unexpectedly passed'
$fixtureDirectory = Join-Path $PSScriptRoot ('.build/gate-source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDirectory | Out-Null
$fixtureFile = Join-Path $fixtureDirectory 'source.ps1'
try {
    [IO.File]::WriteAllText($fixtureFile, "first`nsecond`n", [Text.UTF8Encoding]::new($false))
    $lf = Get-FinalSourceFingerprint $fixtureDirectory
    [IO.File]::WriteAllText($fixtureFile, "first`r`nsecond`r`n", [Text.UTF8Encoding]::new($true))
    Assert-GateTest ((Get-FinalSourceFingerprint $fixtureDirectory) -ceq $lf) 'Git checkout line endings invalidated equivalent evidence'
    [IO.File]::WriteAllText($fixtureFile, "first`nchanged`n", [Text.UTF8Encoding]::new($false))
    Assert-GateTest ((Get-FinalSourceFingerprint $fixtureDirectory) -cne $lf) 'Changed source content did not invalidate evidence'
} finally {
    Remove-Item -LiteralPath $fixtureFile -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fixtureDirectory -ErrorAction SilentlyContinue
}
'OK - 15 release-policy cases; these are not application acceptance tests'
