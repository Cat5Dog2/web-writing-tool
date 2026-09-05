# Runs docker compose for a production deployment with every service pinned to the image ID the
# scanner actually inspected.
#
# Two holes make this necessary, and they are not the same hole.
#
# The first is the caller's environment. docker-compose.yml selects app and postgres through
# ${APP_IMAGE} and ${POSTGRES_IMAGE}, and Compose lets the shell environment win over --env-file.
# scan-image.ps1 sets APP_IMAGE inside its own process, so it scans the intended artifact; a later
# "docker compose up" in a shell that exports APP_IMAGE starts a different one, successfully and
# without a word. That is not a failed deployment, it is a deployment of something the gate never
# saw.
#
# The second is re-resolution. Reading the tag again at start time cannot be safe either: between
# the scan and the start, the tag can be repointed at another image.
#
# So the images come from the manifest scan-image.ps1 writes, never from the tags and never from
# the caller's environment; and after an "up" the running containers are read back and compared
# against the same manifest.
#
# The compose command is one string rather than trailing arguments:
#
#   scripts/production-compose.ps1 -ComposeCommand 'up -d --no-build app'
#
# "powershell -File" binds every argument as a parameter, so a bare "--" is rejected as an empty
# parameter name and a loose "-d" would be taken for one. Passing the command as a single value
# removes both, at the cost of not supporting arguments that contain spaces, which compose
# commands do not need here.
#
# See docs/ci-cd-design.md and docs/operation-design.md 14.2.
param(
    [Parameter(Mandatory = $true)]
    [string] $ComposeCommand,
    [string] $ManifestPath = 'artifacts/scanned-images.json',
    [string[]] $ComposeFile = @('docker-compose.yml', 'docker-compose.external-caddy.yml'),
    [string] $EnvFile
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# Service name to the Compose variable that selects its image. A service in scope with no entry
# here cannot be pinned, so the run is refused rather than quietly started from a tag. Adding a
# service to the production Compose files means adding it here too.
$script:ImageVariables = [ordered]@{
    app      = 'APP_IMAGE'
    postgres = 'POSTGRES_IMAGE'
    # The bundled Caddy, which is in scope only when docker-compose.yml is used on its own. In the
    # external-Caddy topology it is behind a profile and never reaches this map. The shared Caddy
    # is a different artifact in a different Compose project and is pinned where it is owned.
    caddy    = 'CADDY_IMAGE'
}

# Refused rather than guessed at. A manifest from a future version may mean something different by
# the same field names.
$script:SupportedSchemaVersion = 1

function Invoke-DockerCapturing {
    param([string[]] $Arguments)

    # Windows PowerShell turns native stderr into an ErrorRecord, which 'Stop' then treats as
    # terminating. Docker writes ordinary notices there, so the preference is relaxed and the exit
    # code is checked instead.
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

function Test-ImageIdFormat {
    param([string] $ImageId)

    # A tag in this position would undo the point of the manifest, and Docker accepts both here.
    #
    # -cmatch, not -match. PowerShell compares case-insensitively by default, which would accept an
    # uppercase digest here and then hand Docker something it does not resolve.
    return ($ImageId -cmatch '^sha256:[0-9a-f]{64}$')
}

function Read-ScanManifest {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "No scan manifest at $Path. Run scripts/scan-image.ps1 with -ProvenanceOutputPath first; the manifest is written only when the vulnerability gate passes, so its absence means no approved artifact exists."
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    try {
        $manifest = $raw | ConvertFrom-Json
    }
    catch {
        throw "The scan manifest at $Path is not valid JSON: $($_.Exception.Message)"
    }

    if ($null -eq $manifest.schemaVersion) {
        throw "The scan manifest at $Path has no schemaVersion."
    }

    if ([int] $manifest.schemaVersion -ne $script:SupportedSchemaVersion) {
        throw "The scan manifest at $Path is schema version $($manifest.schemaVersion); this script understands $($script:SupportedSchemaVersion)."
    }

    if ($manifest.gateResult -ne 'passed') {
        throw "The scan manifest at $Path records gateResult '$($manifest.gateResult)'. Only 'passed' may be deployed."
    }

    if ($null -eq $manifest.services) {
        throw "The scan manifest at $Path lists no services."
    }

    $pinned = [ordered]@{}
    foreach ($property in $manifest.services.PSObject.Properties) {
        $service = $property.Name
        $imageId = $property.Value.imageId

        if ([string]::IsNullOrWhiteSpace($imageId)) {
            throw "Service '$service' in $Path has no imageId."
        }

        if (-not (Test-ImageIdFormat -ImageId $imageId)) {
            throw "Service '$service' in $Path has imageId '$imageId', which is not a sha256 image ID."
        }

        $pinned[$service] = $imageId
    }

    if ($pinned.Count -eq 0) {
        throw "The scan manifest at $Path lists no services."
    }

    return , $pinned
}

$ComposeArguments = @($ComposeCommand -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($ComposeArguments.Count -eq 0) {
    throw "No compose command was given. For example: -ComposeCommand 'up -d --no-build app'"
}

# "powershell -File" passes every argument as a string, so -ComposeFile a,b arrives as one string
# rather than an array.
$ComposeFile = @($ComposeFile | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

if (-not [System.IO.Path]::IsPathRooted($ManifestPath)) {
    $ManifestPath = Join-Path $repoRoot $ManifestPath
}

$composeOptions = @()
if ([string]::IsNullOrWhiteSpace($EnvFile)) {
    # Never substitute the committed example: a production deployment must not run on placeholder
    # values, the same rule scripts/preflight-external-caddy.ps1 follows.
    $defaultEnvFile = Join-Path $repoRoot '.env'
    if (Test-Path -LiteralPath $defaultEnvFile) {
        $EnvFile = $defaultEnvFile
    }
}
else {
    if (-not [System.IO.Path]::IsPathRooted($EnvFile)) {
        $EnvFile = Join-Path $repoRoot $EnvFile
    }

    if (-not (Test-Path -LiteralPath $EnvFile)) {
        throw "Environment file was not found at $EnvFile"
    }
}

if (-not [string]::IsNullOrWhiteSpace($EnvFile)) {
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

$pinned = Read-ScanManifest -Path $ManifestPath

$servicesResult = Invoke-DockerCapturing -Arguments (@('compose') + $composeOptions + @('config', '--services'))
if ($servicesResult.ExitCode -ne 0) {
    throw "docker compose config --services failed with exit code $($servicesResult.ExitCode).$([Environment]::NewLine)$($servicesResult.Output)"
}

$composeServices = @($servicesResult.Output -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

# Both directions are errors. A service in scope but not in the manifest would start from a tag
# the gate never approved; a service in the manifest but not in scope means the manifest was
# produced from a different set of Compose files than the one being deployed.
$missing = @($composeServices | Where-Object { -not $pinned.Contains($_) })
if ($missing.Count -gt 0) {
    throw "These services are in scope but absent from $($ManifestPath): $($missing -join ', '). Re-run the scan against the same Compose files."
}

$unexpected = @($pinned.Keys | Where-Object { $composeServices -notcontains $_ })
if ($unexpected.Count -gt 0) {
    throw "The manifest at $ManifestPath names services that are not in scope: $($unexpected -join ', '). It was produced from a different set of Compose files."
}

$unpinnable = @($composeServices | Where-Object { -not $script:ImageVariables.Contains($_) })
if ($unpinnable.Count -gt 0) {
    throw "No image variable is known for these services: $($unpinnable -join ', '). Add one to the production Compose files and to this script, or they would start from a mutable tag."
}

foreach ($service in $composeServices) {
    $variable = $script:ImageVariables[$service]
    $imageId = $pinned[$service]

    # Deliberately overwrites whatever the caller exported. This assignment is the entire reason
    # the wrapper exists.
    Set-Item -LiteralPath "Env:$variable" -Value $imageId
    Write-Output "Pinned $service to $imageId via $variable"
}

& docker compose @composeOptions @ComposeArguments
$composeExitCode = $LASTEXITCODE

if ($composeExitCode -ne 0) {
    exit $composeExitCode
}

# A container started from a pinned ID cannot be the wrong image, but this reads the result back
# rather than trusting the argument: it also catches a container that some other command left
# running from an earlier, unpinned deployment.
if ($ComposeArguments -notcontains 'up') {
    exit 0
}

$verified = 0
foreach ($service in $composeServices) {
    $idResult = Invoke-DockerCapturing -Arguments (@('compose') + $composeOptions + @('ps', '-q', $service))
    if ($idResult.ExitCode -ne 0) {
        throw "docker compose ps -q $service failed with exit code $($idResult.ExitCode).$([Environment]::NewLine)$($idResult.Output)"
    }

    $containerIds = @($idResult.Output -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($containerId in $containerIds) {
        $imageResult = Invoke-DockerCapturing -Arguments @('inspect', '--format', '{{.Image}}', $containerId)
        if ($imageResult.ExitCode -ne 0) {
            throw "docker inspect failed for $service container $containerId with exit code $($imageResult.ExitCode).$([Environment]::NewLine)$($imageResult.Output)"
        }

        # -cne for the same reason Test-ImageIdFormat uses -cmatch: a case-insensitive comparison
        # would call two different digests equal.
        $runningImageId = $imageResult.Output.Trim()
        if ($runningImageId -cne $pinned[$service]) {
            throw "Service '$service' is running $runningImageId but the manifest approved $($pinned[$service]). Stop it and redeploy from the scanned artifact."
        }

        $verified++
        Write-Output "Verified $service is running $runningImageId"
    }
}

if ($verified -eq 0) {
    throw "The compose command was an 'up' but no container was found to verify. Nothing has been confirmed to run the scanned images."
}

exit 0
