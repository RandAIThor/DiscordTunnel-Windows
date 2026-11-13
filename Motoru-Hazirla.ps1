$ErrorActionPreference = 'Stop'
$asset = 'sing-box-1.14.4-windows-amd64'
$digest = '99185D75D314A07DBBA4A130C8AD986CBEB629A07927975636B341CA61F991DF'
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ('DiscordTunnel-' + [Guid]::NewGuid())
$archive = Join-Path $temporary "$asset.zip"
$engine = Join-Path $PSScriptRoot 'engine'

New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    Invoke-WebRequest -Uri "https://github.com/SagerNet/sing-box/releases/download/v1.14.4/$asset.zip" -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive).Hash -ne $digest) {
        throw 'Downloaded sing-box archive did not match its SHA-256 checksum.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $temporary
    New-Item -ItemType Directory -Path $engine -Force | Out-Null
    foreach ($name in 'sing-box.exe','libcronet.dll','LICENSE') {
        Copy-Item -LiteralPath (Join-Path $temporary "$asset/$name") -Destination $engine
    }
    'sing-box 1.14.4 is ready.'
}
finally {
    $resolved = (Resolve-Path -LiteralPath $temporary -ErrorAction SilentlyContinue).Path
    if ($resolved -and $resolved -eq $temporary -and $resolved.StartsWith(
        ([System.IO.Path]::GetTempPath()), [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
