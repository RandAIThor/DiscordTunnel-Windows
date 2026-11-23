#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Profile,
    [Parameter(Mandatory)][string]$Engine,
    [ValidateRange(1024,65535)][int]$Port = 24739,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$enginePath = (Resolve-Path -LiteralPath $Engine).Path
$fields = @{}
$section = ''
foreach ($line in Get-Content -LiteralPath $Profile) {
    if ($line -match '^\s*\[(Interface|Peer)\]\s*$') {
        $section = $Matches[1]
    } elseif ($line -match '^\s*([A-Za-z]+)\s*=\s*(.+)$') {
        $name = "$section.$($Matches[1])"
        if ($fields.ContainsKey($name)) { throw "Duplicate profile field: $name" }
        $fields[$name] = $Matches[2].Trim()
    }
}
foreach ($name in 'Interface.Address','Interface.PrivateKey','Peer.Endpoint','Peer.PublicKey','Peer.AllowedIPs') {
    if (!$fields[$name]) { throw "Missing profile field: $name" }
}
if ($fields['Peer.Endpoint'] -notmatch '^(\[[^\]]+\]|[^:]+):(\d+)$') {
    throw 'Invalid WireGuard endpoint.'
}
$peer = @{
    address = $Matches[1].Trim('[',']')
    port = [int]$Matches[2]
    public_key = $fields['Peer.PublicKey']
    allowed_ips = @($fields['Peer.AllowedIPs'] -split '\s*,\s*')
}
$endpoint = @{
    type = 'wireguard'
    tag = 'warp'
    system = $false
    address = @($fields['Interface.Address'] -split '\s*,\s*')
    private_key = $fields['Interface.PrivateKey']
    peers = @($peer)
}
if ($fields['Interface.MTU']) { $endpoint.mtu = [int]$fields['Interface.MTU'] }
if ($fields['Peer.Reserved']) {
    $peer.reserved = @($fields['Peer.Reserved'] -split '\s*,\s*' | ForEach-Object { [byte]$_ })
}

$config = @{
    log = @{ level = 'debug' }
    dns = @{
        servers = @(
            @{ type = 'https'; tag = 'tunnel-dns'; server = '1.1.1.1'; detour = 'warp' }
            @{ type = 'local'; tag = 'local-dns' }
        )
        final = 'tunnel-dns'
    }
    inbounds = @(@{ type = 'socks'; tag = 'probe'; listen = '127.0.0.1'; listen_port = $Port })
    endpoints = @($endpoint)
    route = @{
        default_domain_resolver = 'local-dns'
        final = 'warp'
        rules = @(
            @{ inbound = 'warp'; action = 'reject' }
            @{ inbound = 'probe'; action = 'resolve'; server = 'tunnel-dns' }
        )
    }
}
$mode = if ($Check) { 'check' } else { 'run' }
# Pipe the configuration to the engine without writing key material to a second file.
ConvertTo-Json -InputObject $config -Depth 12 -Compress | & $enginePath $mode --disable-color -c stdin
exit $LASTEXITCODE
