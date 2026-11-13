param([switch]$Test, [switch]$Diagnose, [switch]$PrototypeZip)
$ErrorActionPreference = 'Stop'

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

$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = @('TunnelConfig.cs','Engine.cs','Tray.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$refs = @('/r:System.Net.Http.dll','/r:System.Web.Extensions.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll')
$common = @('/nologo','/codepage:65001','/warnaserror','/platform:x64') + $refs
$exe = Join-Path $stage 'DiscordTunnel-Prototype.exe'
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

$documents = @('KURULUM.md','TASARIM-VE-TEST.md','Motoru-Hazirla.ps1')
Copy-Item -LiteralPath ($documents | ForEach-Object { Join-Path $PSScriptRoot $_ }) -Destination $stage
if ($PrototypeZip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'DiscordTunnel-Prototype-Windows-x64.zip'
    $allowed = @('DiscordTunnel-Prototype.exe') + $documents
    $package = [System.IO.Compression.ZipFile]::Open($destination, 'Create')
    try {
        foreach ($item in $allowed) {
            $null = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($package, (Join-Path $stage $item), $item)
        }
    } finally { $package.Dispose() }
    Get-FileHash -LiteralPath $destination | Format-List Hash,Path
}
