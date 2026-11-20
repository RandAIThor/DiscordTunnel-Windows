param(
    [Parameter(Mandatory=$true)][string]$ProfilePath,
    [Parameter(Mandatory=$true)][string]$WifiProfile,
    [string]$RestoreWireSockProfile
)
$ErrorActionPreference='Stop'
$repoRoot=$PSScriptRoot
$privateRoot=Join-Path $PSScriptRoot '.build'
$ProfilePath=(Resolve-Path -LiteralPath $ProfilePath).Path
$results=Join-Path $privateRoot ('paired-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $results | Out-Null
Set-Content -LiteralPath (Join-Path $privateRoot 'benchmark-current.txt') -Value $results
$cli='C:/Program Files/WireSock Secure Connect/command-line/wiresock-connect-cli.exe'
function Wifi { ([regex]::Match((netsh wlan show interfaces | Out-String),'(?m)^\s*Profile\s*:\s*(.+)$')).Groups[1].Value.Trim() }
function OldStatus { @(& $cli status 2>&1) | Out-String }
function SwitchWifi([string]$name) {
    if ((Wifi) -eq $name) {return}
    $null=netsh wlan connect "name=$name" 'interface=Wi-Fi'
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do {if ((Wifi) -eq $name) {return}; Start-Sleep -Milliseconds 250} while ($watch.ElapsedMilliseconds -lt 28500)
    throw 'Requested Wi-Fi was not reached within 28.5 seconds'
}
function Sample([int]$round,[string]$state,[string]$load,[string]$flow,[int]$port,[int]$index,[bool]$loadRunning) {
    $args=@('--connect-timeout','2','--max-time','3','--silent','-o','NUL','-w','%{http_code} %{time_total}','https://1.1.1.1/cdn-cgi/trace')
    if ($flow -eq 'wireguard') {$args=@('--noproxy','ignored.invalid','--socks5',"127.0.0.1:$port")+$args}
    else {$args=@('--noproxy','*')+$args}
    $response=& curl.exe @args 2>$null
    $exitCode=$LASTEXITCODE
    $fields=([string]$response).Split(' ')
    $ms=$null
    if ($fields.Count -eq 2) { $ms=1000*[double]::Parse($fields[1],[Globalization.CultureInfo]::InvariantCulture) }
    [pscustomobject]@{round=$round;state=$state;load=$load;flow=$flow;index=$index;upload_running=$loadRunning;success=($exitCode -eq 0 -and $fields[0] -eq '200');curl_exit=$exitCode;ms=$ms} | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $results 'samples.jsonl')
}
$payload=Join-Path $results 'synthetic-upload.bin'
$stream=[IO.File]::Create($payload)
try {$stream.SetLength(12MB)} finally {$stream.Dispose()}
function Measure-TunnelBenchmark([int]$round,[string]$state,[int]$port) {
    foreach ($load in @('idle','upload')) {
        $upload=$null
        try {
            if ($load -eq 'upload') {
                $upOut=Join-Path $results ("upload-$round-$state.txt")
                $upErr=Join-Path $results ("upload-$round-$state.error.txt")
                $upArgs=@('--noproxy','*','--connect-timeout','5','--max-time','35','--silent','-o','NUL','-w','"%{http_code} %{size_upload} %{speed_upload} %{time_total}"','--limit-rate','512k','-X','POST','--data-binary',('"@'+$payload+'"'),'https://speed.cloudflare.com/__up')
                $upload=Start-Process -FilePath (Get-Command curl.exe).Source -ArgumentList $upArgs -RedirectStandardOutput $upOut -RedirectStandardError $upErr -WindowStyle Hidden -PassThru
                Start-Sleep -Milliseconds 1200
            }
            for($i=1;$i -le 20;$i++) {
                if ($load -eq 'upload' -and $upload.HasExited) {throw 'Upload ended before all loaded samples; trial is incomplete'}
                Sample $round $state $load 'direct-control' $port $i ($upload -and !$upload.HasExited)
                if ($state -eq 'on') {
                    if ($load -eq 'upload' -and $upload.HasExited) {throw 'Upload ended before all loaded samples; trial is incomplete'}
                    Sample $round $state $load 'wireguard' $port $i ($upload -and !$upload.HasExited)
                }
            }
            if ($upload) {
                if (!$upload.WaitForExit(35000)) {$upload.Kill(); throw 'Synthetic upload did not stop'}
                $transfer=Get-Content -LiteralPath $upOut -Raw
                [pscustomobject]@{Round=$round;State=$state;UploadExit=$upload.ExitCode;Transfer=$transfer} | ConvertTo-Json -Compress
                if ($upload.ExitCode -ne 0 -or $transfer -notmatch '^200\s+12582912\s+[0-9]+(?:\.[0-9]+)?\s+[0-9]+(?:\.[0-9]+)?\s*$') {
                    throw 'Synthetic upload was not fully received with HTTP 200; loaded trial is incomplete'
                }
            }
            [pscustomobject]@{Round=$round;State=$state;Load=$load;Samples=20;WireguardSamples=$(if($state -eq 'on'){20}else{0})} | ConvertTo-Json -Compress
        } finally {if ($upload -and !$upload.HasExited) {$upload.Kill()}; if($upload){$upload.Dispose()}}
    }
}
$initialWifi=Wifi
if ($RestoreWireSockProfile) {
    $old=OldStatus
    $oldProfile=[regex]::Match($old,'Profil (.+?) kullanılarak').Groups[1].Value.Trim()
    if ($oldProfile -cne $RestoreWireSockProfile) {throw 'The original WireSock profile did not match the requested restoration profile'}
}
$live=$null
$liveDirectory=$null
try {
    SwitchWifi $WifiProfile
    Start-Sleep -Seconds 3
    if ($RestoreWireSockProfile) {
        $null=& $cli disconnect
        if ((OldStatus) -match 'sunucuya bağlanıldı|bağlantı kuruluyor') {throw 'Old tunnel did not stop'}
    }
    for ($round=1;$round -le 3;$round++) {
        $null=& curl.exe --noproxy '*' --connect-timeout 8 --max-time 10 --silent --fail -o NUL 'https://1.1.1.1/cdn-cgi/trace' 2>$null
        if ($LASTEXITCODE -ne 0) {throw 'Direct HTTPS warmup failed; paired trial is incomplete'}
        Measure-TunnelBenchmark $round 'off' 0
        $liveDirectory=Join-Path $results ("engine-$round")
        New-Item -ItemType Directory -Path $liveDirectory | Out-Null
        $live=Start-Process -FilePath (Join-Path $repoRoot 'Windows-x64/LiveAcceptance.exe') -ArgumentList ('"'+$liveDirectory+'" "'+$ProfilePath+'"') -Verb RunAs -WindowStyle Hidden -PassThru
        $watch=[Diagnostics.Stopwatch]::StartNew()
        $report=Join-Path $liveDirectory 'status.txt'
        do {
            if ($live.HasExited) {throw 'Elevated benchmark helper exited'}
            if ((Test-Path -LiteralPath $report) -and ((Get-Content -LiteralPath $report) -match 'ready-for-go')) {break}
            Start-Sleep -Milliseconds 200
        } while ($watch.ElapsedMilliseconds -lt 15000)
        New-Item -ItemType File -Path (Join-Path $liveDirectory 'go') | Out-Null
        $watch.Restart()
        do {
            $state=Get-Content -LiteralPath $report -Raw
            if ($state -match 'tun-ready|tun-start-failed|exception=') {break}
            Start-Sleep -Milliseconds 200
        } while ($watch.ElapsedMilliseconds -lt 28500)
        $match=[regex]::Match($state,'tun-ready probe-port=(\d+)')
        if (!$match.Success) {throw 'New TUN was not ready'}
        $port=[int]$match.Groups[1].Value
        # Warmup must succeed before attributing latency to a working tunnel.
        $null=& curl.exe --noproxy ignored.invalid --socks5 "127.0.0.1:$port" --connect-timeout 8 --max-time 10 --silent --fail -o NUL 'https://1.1.1.1/cdn-cgi/trace' 2>$null
        if ($LASTEXITCODE -ne 0) {throw 'WireGuard HTTPS warmup failed; paired trial is incomplete'}
        Measure-TunnelBenchmark $round 'on' $port
        New-Item -ItemType File -Path (Join-Path $liveDirectory 'stop') -Force | Out-Null
        $watch.Restart()
        while ((Get-Process -Id $live.Id -ErrorAction SilentlyContinue) -and $watch.ElapsedMilliseconds -lt 10000) {Start-Sleep -Milliseconds 200}
        if (Get-Process -Id $live.Id -ErrorAction SilentlyContinue) {throw 'New motor did not stop'}
        $live=$null
        if (@(Get-NetAdapter -Name DiscordTunnel -ErrorAction SilentlyContinue).Count -gt 0) {throw 'TUN remained after stop'}
    }
} finally {
    if ($liveDirectory) {New-Item -ItemType File -Path (Join-Path $liveDirectory 'stop') -Force | Out-Null}
    if ($live) { $watch=[Diagnostics.Stopwatch]::StartNew(); while ((Get-Process -Id $live.Id -ErrorAction SilentlyContinue) -and $watch.ElapsedMilliseconds -lt 10000) {Start-Sleep -Milliseconds 200} }
    try {if ((Wifi) -ne $initialWifi) {SwitchWifi $initialWifi}}
    finally {
        if ($RestoreWireSockProfile) {$null=Start-Process -FilePath $cli -ArgumentList @('connect',$oldProfile,'-exit') -WindowStyle Hidden -PassThru}
    }
    [pscustomobject]@{RestorationAttempted=$true;OriginalWifiRestored=((Wifi) -eq $initialWifi);NewEngineCount=@(Get-Process -Name sing-box -ErrorAction SilentlyContinue).Count;NewTunCount=@(Get-NetAdapter -Name DiscordTunnel -ErrorAction SilentlyContinue).Count;Results=$results} | ConvertTo-Json -Compress
}
