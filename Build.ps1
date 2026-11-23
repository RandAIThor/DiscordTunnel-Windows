param(
    [switch]$Test, [switch]$Diagnose,
    [Alias('PrototypeZip')][switch]$Package, [switch]$PreviewPackage
)
$ErrorActionPreference = 'Stop'

if ($Package -and $PreviewPackage) { throw 'Tek bir paket türü seç.' }
if ($Package) {
    . (Join-Path $PSScriptRoot 'ReleaseGate.ps1')
    Assert-FinalAcceptance
}
if ($Package -or $PreviewPackage) {
    $Test = $true
}

$version = '1.14.4'
$asset = "sing-box-$version-windows-amd64"
$digest = '99185D75D314A07DBBA4A130C8AD986CBEB629A07927975636B341CA61F991DF'
$build = Join-Path $PSScriptRoot '.build'
$stage = Join-Path $PSScriptRoot 'Windows-x64'
$engine = Join-Path $stage 'engine'
$archive = Join-Path $build "$asset.zip"
New-Item -ItemType Directory -Path $build,$engine -Force | Out-Null
if (!(Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest "https://github.com/SagerNet/sing-box/releases/download/v$version/$asset.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive).Hash -ne $digest) {
    throw 'sing-box arşivi beklenen SHA-256 değeriyle eşleşmiyor.'
}
Expand-Archive -LiteralPath $archive -DestinationPath $build -Force
$upstream = Join-Path $build $asset
foreach ($name in 'sing-box.exe','libcronet.dll','LICENSE') {
    Copy-Item -LiteralPath (Join-Path $upstream $name) -Destination $engine
}
$copying = Join-Path $build 'COPYING'
if (!(Test-Path -LiteralPath $copying)) {
    Invoke-WebRequest 'https://www.gnu.org/licenses/gpl-3.0.txt' -OutFile $copying -TimeoutSec 15
}
if ((Get-FileHash -LiteralPath $copying).Hash -ne '3972DC9744F6499F0F9B2DBF76696F2AE7AD8AF9B23DDE66D6AF86C9DFB36986') {
    throw 'GPL license text did not match the expected SHA-256.'
}
Copy-Item -LiteralPath $copying -Destination $engine

$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = @('TunnelConfig.cs','Engine.cs','Tray.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$refs = @('/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
$common = @('/nologo','/codepage:65001','/warnaserror','/platform:x64') + $refs
$exe = Join-Path $stage 'DiscordTunnel.exe'
& $compiler @common /target:winexe "/out:$exe" @source
if ($LASTEXITCODE -ne 0) { throw 'Uygulama derlenemedi.' }

if ($Test -or $Diagnose) {
    $testExe = Join-Path $stage 'SmokeTest.exe'
    try {
        $testSources = @('SmokeTest.cs','WireGuardLoopback.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
        & $compiler @common /target:exe /main:DiscordTunnelPrototype.SmokeTest "/out:$testExe" @source @testSources
        if ($LASTEXITCODE -ne 0) { throw 'Testler derlenemedi.' }
        $testArgs = @()
        if ($Diagnose) { $testArgs += '--dns' }
        & $testExe @testArgs
        if ($LASTEXITCODE -ne 0) { throw 'Prototip kontrolleri başarısız.' }

        $info = New-Object System.Diagnostics.ProcessStartInfo($testExe, '--orphan-test')
        $info.UseShellExecute = $false
        $info.RedirectStandardOutput = $true
        $info.CreateNoWindow = $true
        $parent = [System.Diagnostics.Process]::Start($info)
        try {
            $ready = $parent.StandardOutput.ReadLineAsync()
            if (!$ready.Wait(10000) -or $ready.Result -ne 'READY') { throw 'Çökme testi motoru hazır olmadı.' }
            $child = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($parent.Id) AND Name = 'sing-box.exe'")
            if ($child.Count -ne 1) { throw 'Çökme testi motoru bulunamadı.' }
            $engineProcess = [System.Diagnostics.Process]::GetProcessById($child[0].ProcessId)
            try {
                $null = $engineProcess.Handle
                $parent.Kill()
                if (!$engineProcess.WaitForExit(5000)) { throw 'Denetleyici kapandığında motor çalışır kaldı.' }
                'OK - child terminated after controller crash without TUN'
            } finally { $engineProcess.Dispose() }
        } finally {
            if (!$parent.HasExited) { $parent.Kill() }
            $parent.WaitForExit()
            $parent.Dispose()
        }
    } finally {
        if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
    }
}

$documents = @('KURULUM.md','ILK-BAGLANTI.md','TASARIM-VE-TEST.md','FINAL-KABUL.md','THIRD-PARTY.md')
Copy-Item -LiteralPath ($documents | ForEach-Object { Join-Path $PSScriptRoot $_ }) -Destination $stage
if ($Package -or $PreviewPackage) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $distribution = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Path $distribution -Force | Out-Null
    $packageVersion = if ($PreviewPackage) { '0.2.1-preview.1' } else { '0.2.1' }
    $destination = Join-Path $distribution "DiscordTunnel-$packageVersion-Windows-x64.zip"
    $allowed = @('DiscordTunnel.exe') + $documents + @('engine/sing-box.exe','engine/libcronet.dll','engine/LICENSE','engine/COPYING')
    if (Test-Path -LiteralPath $destination) { throw 'Package already exists. Choose a new release version or move the earlier artifact.' }
    $zipArchive = [System.IO.Compression.ZipFile]::Open($destination, 'Create')
    try {
        foreach ($item in $allowed) {
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zipArchive, (Join-Path $stage $item), $item)
        }
    } finally { $zipArchive.Dispose() }
    Get-FileHash -LiteralPath $destination | Format-List Hash,Path
}
