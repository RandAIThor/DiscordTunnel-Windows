$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$stage = Join-Path $PSScriptRoot 'Windows-x64'
if (!(Test-Path -LiteralPath (Join-Path $stage 'engine/sing-box.exe'))) {
    throw 'Run Build.ps1 first. This reproduction requires the pinned engine.'
}
$exe = Join-Path $stage 'AttributionRepro.exe'
$source = @('Engine.cs','TunnelConfig.cs','AttributionRepro.cs') | ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler /nologo /codepage:65001 /warnaserror /platform:x64 /target:exe /r:System.Web.Extensions.dll "/out:$exe" @source
if ($LASTEXITCODE -ne 0) {throw 'Adversarial regression probe did not compile'}
$fixture = Join-Path $PSScriptRoot ('.build/attribution-' + [guid]::NewGuid().ToString('N'))
try {
    & $exe $fixture
    $result = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $exe -ErrorAction SilentlyContinue
}
if ($result -eq 1) {throw 'Protection failed: process-attribution loss caused direct delivery. Final acceptance must remain blocked.'}
if ($result -ne 0) {throw 'Reproduction could not be completed; do not report protection as passed.'}
