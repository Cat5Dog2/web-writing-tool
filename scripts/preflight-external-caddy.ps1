# Verifies, without changing anything, that the external-Caddy deployment can start.
#
# The one precondition Compose cannot check statically is the external network. "docker compose
# config" succeeds while it is missing, and "docker compose up" then fails with
# "network ... declared as external, but could not be found". On a deployment host that failure
# would land after the image build, the app stop, the backup and the migration, which is exactly
# the ordering problem docs/operation-design.md 14.2 exists to avoid. So this runs first and
# touches nothing: every command here is read-only.
#
# The remaining assertions pin the properties the topology depends on and that a later edit could
# silently drop: app reachable from Caddy under a stable alias, PostgreSQL not reachable from
# Caddy and not published to the host, and the bundled Caddy staying out of scope. See
# docs/environment-setup.md 7.12.
param(
    [string[]] $ComposeFile = @('docker-compose.yml', 'docker-compose.external-caddy.yml'),
    [string] $EnvFile,
    [string] $AppNetworkAlias = 'wwt-app'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-DockerCapturing {
    param([string[]] $Arguments)

    # Windows PowerShell turns native stderr into an ErrorRecord, which 'Stop' then treats as
    # terminating. Docker writes ordinary notices there, so the preference is relaxed and the
    # exit code is checked instead.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & docker @Arguments 2>&1 | ForEach-Object { $_.ToString() }
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = ($output -join [Environment]::NewLine)
        }
    }
    finally {
        $ErrorActionPreference = $previous
    }
}

# Both helpers return with a leading comma. "return @(...)" unrolls the array, so a single element
# comes back as a bare object with no .Count in Windows PowerShell, and a count test against it
# silently reads as $null rather than 1 - which skipped the postgres port check entirely. The comma
# wraps the array in one more array that the return then unrolls, leaving the array itself.
function Get-ServiceNetworkNames {
    param($Service)

    if ($null -eq $Service.networks) {
        return , @()
    }

    return , @($Service.networks.PSObject.Properties.Name)
}

function ConvertTo-EmptyableArray {
    param($Value)

    # @($null) is an array of one null, not an empty array, so a missing "ports" key would
    # otherwise read as one published port and fail the checks below with a blank message.
    if ($null -eq $Value) {
        return , @()
    }

    return , @($Value)
}

# "powershell -File" passes every argument as a string, so -ComposeFile a,b arrives as one string
# rather than an array. Split here so both invocation styles work, the same way scan-image.ps1
# does it.
$ComposeFile = @($ComposeFile | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    $EnvFile = Join-Path $repoRoot '.env'
    if (-not (Test-Path -LiteralPath $EnvFile)) {
        $EnvFile = Join-Path $repoRoot '.env.production.example'
    }
}

$composeOptions = @()
if (Test-Path -LiteralPath $EnvFile) {
    $composeOptions += @('--env-file', $EnvFile)
}

foreach ($file in $ComposeFile) {
    $path = $file
    if (-not [System.IO.Path]::IsPathRooted($path)) {
        $path = Join-Path $repoRoot $file
    }

    if (-not (Test-Path -LiteralPath $path)) {
        throw "Compose file was not found at $path"
    }

    $composeOptions += @('-f', $path)
}

$configResult = Invoke-DockerCapturing -Arguments (@('compose') + $composeOptions + @('config', '--format', 'json'))
if ($configResult.ExitCode -ne 0) {
    throw "docker compose config failed with exit code $($configResult.ExitCode).$([Environment]::NewLine)$($configResult.Output)"
}

$config = $configResult.Output | ConvertFrom-Json

$services = @($config.services.PSObject.Properties.Name)

# The bundled Caddy would bind 80 and 443 on the host and fight the shared Caddy for them.
if ($services -contains 'caddy') {
    throw "The bundled caddy service is in scope. docker-compose.external-caddy.yml must keep it behind the bundled-caddy profile."
}

if ($services -notcontains 'app') {
    throw "The app service is not in scope. Check the compose file list."
}

if ($services -notcontains 'postgres') {
    throw "The postgres service is not in scope. Check the compose file list."
}

$appNetworks = Get-ServiceNetworkNames -Service $config.services.app

# Writing networks at all replaces the default attachment, so losing 'default' here means app can
# no longer resolve postgres.
if ($appNetworks -notcontains 'default') {
    throw "app is not attached to the default network, so it cannot reach postgres. Networks: $($appNetworks -join ', ')."
}

if ($appNetworks -notcontains 'caddy') {
    throw "app is not attached to the caddy network, so the shared Caddy cannot reach it. Networks: $($appNetworks -join ', ')."
}

$appAliases = ConvertTo-EmptyableArray -Value $config.services.app.networks.caddy.aliases
if ($appAliases -notcontains $AppNetworkAlias) {
    throw "app has no '$AppNetworkAlias' alias on the caddy network. The shared Caddyfile resolves that name. Aliases: $($appAliases -join ', ')."
}

$postgresNetworks = Get-ServiceNetworkNames -Service $config.services.postgres
if ($postgresNetworks -contains 'caddy') {
    throw "postgres is attached to the caddy network. Only app may be reachable from the shared Caddy."
}

$postgresPorts = ConvertTo-EmptyableArray -Value $config.services.postgres.ports
if ($postgresPorts.Count -gt 0) {
    $published = ($postgresPorts | ForEach-Object { "$($_.host_ip):$($_.published)" }) -join ', '
    throw "postgres publishes $published to the host. The shared Caddy reaches app over the Docker network, so nothing needs this, and any local process could connect. Use 'docker compose exec postgres' instead."
}

# Kept deliberately: the deployment procedure checks /health/ready from inside the VPS, because the
# shared Caddy answers 404 for it from outside.
$appPorts = ConvertTo-EmptyableArray -Value $config.services.app.ports
$loopbackPorts = @($appPorts | Where-Object { $_.host_ip -eq '127.0.0.1' })
if ($loopbackPorts.Count -eq 0) {
    $published = ($appPorts | ForEach-Object { "$($_.host_ip):$($_.published)" }) -join ', '
    throw "app publishes no loopback port, so /health/ready cannot be checked from the VPS. Published: $published."
}

$nonLoopback = @($appPorts | Where-Object { $_.host_ip -ne '127.0.0.1' })
if ($nonLoopback.Count -gt 0) {
    $published = ($nonLoopback | ForEach-Object { "$($_.host_ip):$($_.published)" }) -join ', '
    throw "app publishes $published beyond loopback. Only the shared Caddy may face the network."
}

$caddyNetwork = $config.networks.caddy
if ($null -eq $caddyNetwork) {
    throw "The compose configuration declares no caddy network."
}

if (-not $caddyNetwork.external) {
    throw "The caddy network is not declared external. It must be created and owned outside this project."
}

$caddyNetworkName = $caddyNetwork.name
if ([string]::IsNullOrWhiteSpace($caddyNetworkName)) {
    throw "The caddy network has no resolved name. Set CADDY_NETWORK."
}

# "network ls" is used rather than "network inspect" because inspect writes to stderr and exits
# non-zero for a missing network, and the name is compared exactly: "ls --filter name=" matches
# substrings, so a different network could satisfy it.
$networkResult = Invoke-DockerCapturing -Arguments @('network', 'ls', '--format', '{{.Name}}')
if ($networkResult.ExitCode -ne 0) {
    throw "docker network ls failed with exit code $($networkResult.ExitCode).$([Environment]::NewLine)$($networkResult.Output)"
}

$networkNames = @($networkResult.Output -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($networkNames -notcontains $caddyNetworkName) {
    throw "The external network '$caddyNetworkName' does not exist. Create it before deploying: docker network create $caddyNetworkName"
}

Write-Output "Preflight passed for the external-Caddy deployment."
Write-Output "  services in scope : $($services -join ', ')"
Write-Output "  app networks      : $($appNetworks -join ', ')"
Write-Output "  app caddy alias   : $($appAliases -join ', ')"
Write-Output "  app published     : $(($appPorts | ForEach-Object { "$($_.host_ip):$($_.published)" }) -join ', ')"
Write-Output "  postgres published: none"
Write-Output "  external network  : $caddyNetworkName exists"
