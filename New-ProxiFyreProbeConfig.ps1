#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProbeDirectory,
    [ValidateRange(1024,65535)][int]$Port = 24739
)

$ErrorActionPreference = 'Stop'

function Test-PlainItem([System.IO.FileSystemInfo]$Item) {
    return ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0
}

$discordRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Discord'
$rootItem = Get-Item -LiteralPath $discordRoot
if (!$rootItem.PSIsContainer -or !(Test-PlainItem $rootItem)) {
    throw 'Standart Discord kurulum klasörü bulunamadı veya bir bağlantı.'
}

$versionDirs = Get-ChildItem -LiteralPath $rootItem.FullName -Directory -Force | Where-Object {
    $_.Name -match '^app-[0-9]+\.[0-9]+(?:\.[0-9]+){0,2}$' -and (Test-PlainItem $_)
}
$discordPrograms = foreach ($versionDir in $versionDirs) {
    $main = Get-Item -LiteralPath (Join-Path $versionDir.FullName 'Discord.exe') -ErrorAction SilentlyContinue
    if ($main -and !$main.PSIsContainer -and (Test-PlainItem $main)) {
        $main
        Get-Item -LiteralPath (Join-Path $versionDir.FullName 'updater_launcher.exe') -ErrorAction SilentlyContinue |
            Where-Object { !$_.PSIsContainer -and (Test-PlainItem $_) }
    }
}
if (!$discordPrograms) {
    throw 'Standart Discord sürüm klasöründe Discord.exe bulunamadı.'
}
$rootPrograms = foreach ($name in 'Discord.exe','Update.exe') {
    Get-Item -LiteralPath (Join-Path $rootItem.FullName $name) -ErrorAction SilentlyContinue |
        Where-Object { !$_.PSIsContainer -and (Test-PlainItem $_) }
}
$appNames = @($rootPrograms) + @($discordPrograms) |
    Where-Object { $_ } | Select-Object -ExpandProperty FullName -Unique

# v2.7.0 treats paths as substrings. Reject currently visible collisions during
# a lab run. A file created later can still match, so this is not a boundary.
foreach ($exe in $appNames) {
    $parent = Split-Path -LiteralPath $exe
    $prefix = (Split-Path -Path $exe -Leaf) + '*'
    $shadow = Get-ChildItem -LiteralPath $parent -Force -Filter $prefix |
        Where-Object { $_.FullName -ne $exe } | Select-Object -First 1
    if ($shadow) {
        throw "ProxiFyre alt dize kuralıyla eşleşebilecek başka bir yol var: $($shadow.FullName)"
    }
}

$probeItem = Get-Item -LiteralPath $ProbeDirectory
$engine = Join-Path $probeItem.FullName 'ProxiFyre.exe'
$engineItem = Get-Item -LiteralPath $engine -ErrorAction SilentlyContinue
if (!$probeItem.PSIsContainer -or !(Test-PlainItem $probeItem) -or
    !$engineItem -or $engineItem.PSIsContainer -or !(Test-PlainItem $engineItem) -or
    $engineItem.VersionInfo.FileVersion -notmatch '^2\.7\.0(?:\.0)?$') {
    throw 'ProxiFyre v2.7.0 x64 ZIP dosyasını ayrı bir deneme klasörüne çıkar.'
}

$path = Join-Path $probeItem.FullName 'app-config.json'
$config = @{
    logLevel = 'Info'
    bypassLan = $true
    proxies = @(@{
        id = 'discord-probe'
        appNames = @($appNames)
        socks5ProxyEndpoint = "127.0.0.1:$Port"
        socks5Transport = 'TCP'
        supportedProtocols = @('TCP','UDP')
        supportedAddressFamilies = @('IPv4','IPv6')
    })
    excludes = @()
    dns = @{ mode = 'Direct' }
}
$temporaryPath = Join-Path $probeItem.FullName ("app-config.{0}.tmp" -f [Guid]::NewGuid())
try {
    $json = $config | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($temporaryPath, $json)
    [IO.File]::Move($temporaryPath, $path)
} finally {
    [IO.File]::Delete($temporaryPath)
}
Get-Item -LiteralPath $path
