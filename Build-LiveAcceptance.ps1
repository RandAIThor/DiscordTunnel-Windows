$ErrorActionPreference = 'Stop'
$temporary = Join-Path $PSScriptRoot '.build/live-harness-sources'
New-Item -ItemType Directory -Path $temporary -Force | Out-Null
$controller = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Engine.cs') -Raw
$marker = 'if (line.StartsWith("FATAL", StringComparison.Ordinal))'
if ([regex]::Matches($controller, [regex]::Escape($marker)).Count -ne 1) {
    throw 'Controller instrumentation point changed; review the harness before compiling.'
}
$controller = $controller.Replace($marker, 'LiveAcceptance.RecordEngine(line);' + [Environment]::NewLine + '                    ' + $marker)
$instrumented = Join-Path $temporary 'Engine-Diagnostic.cs'
Set-Content -LiteralPath $instrumented -Value $controller -Encoding utf8
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$source = @($instrumented, (Join-Path $PSScriptRoot 'TunnelConfig.cs'), (Join-Path $PSScriptRoot 'LiveAcceptance.cs'))
$destination = Join-Path $PSScriptRoot 'Windows-x64/LiveAcceptance.exe'
& $compiler /nologo /codepage:65001 /warnaserror /platform:x64 /target:winexe /r:System.Web.Extensions.dll "/out:$destination" @source
if ($LASTEXITCODE -ne 0) {throw 'Live acceptance harness did not compile'}
'Live acceptance harness compiled. Usage: LiveAcceptance.exe <private-run-directory> <local-profile-path>'
