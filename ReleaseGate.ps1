$script:FinalAcceptanceChecks = @(
    'windows-10-x64', 'windows-11-x64', 'discord-update-and-module-traffic',
    'non-discord-isolation', 'unknown-process-no-bypass', 'system-dns-isolation',
    'dns-failure-diagnosis-30s', 'discord-client-doh', 'wifi-transition-with-voice',
    'discord-login', 'discord-two-way-voice', 'live-ipv4-ipv6',
    'paired-idle-upload-latency', 'tun-crash-recovery', 'clean-removal'
)

function Get-FinalSourceFingerprint {
    param([string]$Root = $PSScriptRoot)
    $names = [string[]]@(Get-ChildItem -LiteralPath $Root -File | Where-Object Extension -in '.cs','.ps1' | ForEach-Object Name)
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $entries = @(foreach ($name in $names) {
            # Match Git's text checkout semantics across LF/CRLF and UTF-8 BOM changes.
            $content = [IO.File]::ReadAllText((Join-Path $Root $name)).Replace("`r`n", "`n")
            $hash = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($content)))).Replace('-','')
            $name + ':' + $hash
        })
        ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))))).Replace('-','')
    }
    finally { $sha.Dispose() }
}

function Get-FinalAcceptanceErrors {
    param($Report, [string]$SourceFingerprint)
    # This validates a review record, not the truth of a human-authored test claim.
    # Required test IDs live in code, so deleting a failed row cannot unblock packaging.
    if ($Report.schema_version -ne 1) { 'Unsupported acceptance record schema' }
    if ($Report.source_sha256 -cne $SourceFingerprint) { 'Acceptance evidence does not match the current source' }
    $checks = @($Report.checks)
    foreach ($required in $script:FinalAcceptanceChecks) {
        $matching = @($checks | Where-Object id -CEQ $required)
        if ($matching.Count -ne 1) { "${required}: expected exactly one result"; continue }
        $check = $matching[0]
        if ($check.status -cne 'passed') { "${required}: $($check.status)" }
        if (@($check.evidence).Count -eq 0 -or @($check.evidence | Where-Object {$_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_)}).Count -gt 0) {
            "${required}: evidence reference missing"
        }
    }
    foreach ($check in $checks) {
        if ($check.id -cnotin $script:FinalAcceptanceChecks) { 'Unknown acceptance check' }
        if ($check.status -cnotin @('passed','failed','partial','not_run')) { 'Invalid acceptance status' }
    }
}

function Assert-FinalAcceptance {
    $path = Join-Path $PSScriptRoot 'RELEASE-ACCEPTANCE.json'
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Final paket engellendi: kabul kaydı yok.' }
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $failures = @(Get-FinalAcceptanceErrors $report (Get-FinalSourceFingerprint))
    if ($failures.Count -gt 0) {
        throw ("Final paket engellendi. Doğrulanmamış veya başarısız kabul ölçütleri:`n" + ($failures -join "`n"))
    }
}
