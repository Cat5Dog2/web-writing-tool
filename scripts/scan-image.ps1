# Mechanises the "no critical Docker image vulnerabilities" release gate in docs/ci-cd-design.md.
#
# Scope is every image the Compose files deploy, not only the application image. The list comes
# from "docker compose config" so it cannot drift from the Compose files. Pass -ComposeFile to
# match the deployment being scanned; with docker-compose.shared-caddy.yml the bundled caddy moves
# behind a profile and correctly drops out of scope.
#
# The tools profile is excluded on purpose: the migrate service is ephemeral, runs only during a
# deploy, and never listens on a socket. docs/ci-cd-design.md records that decision.
#
# Uses Trivy, which downloads its vulnerability database but never uploads the image or its
# metadata to a third party. Docker Scout is deliberately not used for that reason.
#
# Trivy never gets the Docker socket. Each image is exported with `docker save` and read back from
# the tar with --input, so the scanner is handed one file instead of control of the Docker daemon,
# which is root-equivalent on the host. That matters here more than in CI: docs/ci-cd-design.md 9.4
# has this script run on the deployment host, where the daemon also owns the databases and volumes
# of everything else deployed there. For the same reason the scanner image is pinned by digest
# rather than by a tag that can be repointed, and each scan runs with --network none so the scanner
# makes no network calls of its own. That is a claim about the scanner reaching the network, not
# about the image never leaving the host: the scanner's stdout and stderr are the report, and they
# go to this console and to the CI log like any other command output. Refreshing the
# vulnerability database is the one Trivy step that keeps the network, and it has no image tar in
# reach. The network is still used outside Trivy - `docker compose build --pull`, the third-party
# `docker pull`, and Docker's own pull of the scanner image all reach out.
#
# The gate does NOT pass --ignore-unfixed. A finding without an upstream fix still fails until it
# is triaged into security/trivy/<image>.trivyignore.yaml. Trivy treats statement and expired_at
# as optional, so this script enforces them before handing the file over. See security/trivy/README.md.
param(
    [string] $AppImage = 'web-writing-tool-app:local',
    [string[]] $ComposeFile = @('docker-compose.yml'),
    [switch] $Build,
    [switch] $SkipPull,
    [switch] $ValidateOnly,
    [switch] $SelfTest,
    # Needs a daemon, so it is a separate switch from -SelfTest: the checks it runs are about what
    # Trivy and the shell do, not about the arguments this script builds. Its own fixtures are
    # named per run and removed afterwards, but it does create -TrivyCacheVolume if it is missing
    # and refresh the database in it, the same way a scan would. That is deliberate - the Java
    # check is only meaningful against a real database - and it is the one thing this switch leaves
    # behind on the host it runs on.
    [switch] $DockerSelfTest,
    # Trivy 0.74.0, linux/amd64, pinned by digest: a tag can be repointed at a different image, and
    # this one runs on the deployment host. Every run rejects a reference without a digest, not
    # only the self test. Update deliberately, together with the version in this comment.
    [string] $TrivyImage = 'aquasec/trivy@sha256:62b1e65e8869bc4b4c6aa4fa2b21595256c7c2f6018a9d9ad61caf87187c1969',
    # Databases only, and a new name rather than the previous web-writing-tool-trivy-cache. Scans
    # used to share that volume writable, so it still holds the package lists and other output they
    # left there. Keeping the network-facing refresh away from anything a scan derived from a target
    # image means starting from a volume no scan has ever written to. Retiring the old volume is an
    # operational step with a check to do first, so it lives in docs/ci-cd-design.md, not here.
    [string] $TrivyCacheVolume = 'web-writing-tool-trivy-db-cache',
    # Applied to every Trivy container. On the deployment host this process shares a machine with
    # another application, so neither a large or malformed image nor a database download may take
    # the box down. Measured on 2026-09-05: the largest image in scope (a 112 MB export) completes
    # inside 256 MiB, so the memory default leaves roughly four times that.
    #
    # --cpus defaults to one deliberately. The deployment target is a small VPS, where 2 would be
    # the whole machine and the co-located application would feel every scan. CI, which has the
    # runner to itself, can raise it. Set these from the capacity of the host that will run this,
    # not from what is comfortable locally, and raise them here rather than in the builders.
    [string] $ScanMemoryLimit = '1g',
    [string] $ScanCpuLimit = '1',
    [string] $ScanPidsLimit = '512',
    # Where to record what this run actually scanned, so a deployment can start those exact images
    # rather than re-resolving the tags. Optional: without it the script behaves as it did before.
    #
    # Re-resolving is the hole this closes. The scanner resolves each tag to an image ID and
    # exports that ID, so the scan itself cannot be pointed at a different image by a tag that
    # moves mid-run. A deployment that then reads the tag again gets no such guarantee: between the
    # scan and the start, the tag can be repointed at an image the gate never saw.
    #
    # The file is written only after every image in scope passes, and any existing file at the path
    # is removed before scanning begins. Both halves matter. A manifest that outlived a failed run
    # is exactly how an image the gate rejected gets deployed.
    #
    # Relative paths resolve against the repository root. artifacts/ is already ignored by Git.
    [string] $ProvenanceOutputPath
)

$ErrorActionPreference = 'Stop'

# -TrivyImage is a parameter, so a caller can unpin the scanner from the command line. Checking the
# shape here rather than only in the self test is what makes the pin a property of every run: a
# production scan on the deployment host must not be able to fetch whatever a mutable tag points at
# today. The pattern rejects a bare tag and also a malformed digest, which a substring test would
# let through - "host@sha256:x" contains "@sha256:" and pins nothing.
function Test-TrivyImagePinned {
    param([string] $Reference)

    return ($Reference -match '^[^@\s]+@sha256:[0-9a-fA-F]{64}$')
}

if (-not (Test-TrivyImagePinned -Reference $TrivyImage)) {
    throw "The scanner image must be pinned by digest as '<repository>@sha256:<64 hex digits>'. Got '$TrivyImage'."
}

# Docker reads several shapes as "no limit" rather than as an error: 0 for --memory and --cpus, and
# both 0 and -1 for --pids-limit. Passing one of those through would leave the container unbounded
# while the command line still looked like it had been capped, so they are refused here.
function Test-MemoryLimitValue {
    param([string] $Value)

    return ($Value -match '^[1-9][0-9]*[bkmgBKMG]?$')
}

function Test-PidsLimitValue {
    param([string] $Value)

    return ($Value -match '^[1-9][0-9]*$')
}

function Test-CpuLimitValue {
    param([string] $Value)

    $parsed = 0.0
    if (-not [double]::TryParse(
            $Value,
            [System.Globalization.NumberStyles]::Float,
            [cultureinfo]::InvariantCulture,
            [ref] $parsed)) {
        return $false
    }

    return ($parsed -gt 0)
}

if (-not (Test-MemoryLimitValue -Value $ScanMemoryLimit)) {
    throw "-ScanMemoryLimit must be a positive size such as 1g. Docker reads 0 as unlimited. Got '$ScanMemoryLimit'."
}

if (-not (Test-CpuLimitValue -Value $ScanCpuLimit)) {
    throw "-ScanCpuLimit must be greater than zero. Docker reads 0 as unlimited. Got '$ScanCpuLimit'."
}

if (-not (Test-PidsLimitValue -Value $ScanPidsLimit)) {
    throw "-ScanPidsLimit must be a positive process count. Docker reads 0 and -1 as unlimited. Got '$ScanPidsLimit'."
}

$resourceLimits = @{
    MemoryLimit = $ScanMemoryLimit
    CpuLimit    = $ScanCpuLimit
    PidsLimit   = $ScanPidsLimit
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$ignoreDirectory = Join-Path $repoRoot 'security/trivy'

# Trivy needs RFC3339. A bare date is accepted by DateTime parsing but rejected by Trivy, so the
# shape is checked before the value, otherwise the file would pass here and fail inside the scanner.
$Rfc3339Pattern = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$'

# PowerShell 7 turns an ISO-8601 JSON string into System.DateTime during ConvertFrom-Json, and the
# resulting object stringifies in the current culture ("01/01/2099 00:00:00"). Checking the shape on
# the parsed object would therefore reject valid files on pwsh while passing on Windows PowerShell.
# -DateKind String would avoid that but only exists from 7.5, and 7.4 is still shipped widely.
# The raw file text is never coerced, so the shape is validated there and the object model is used
# only for the remaining fields.
$ExpiredAtPattern = '"expired_at"\s*:\s*"(?<value>[^"]*)"'

function Test-IgnoreFile {
    param([string] $Path)

    $raw = Get-Content -LiteralPath $Path -Raw
    $document = $raw | ConvertFrom-Json
    $entries = @($document.vulnerabilities)
    if ($entries.Count -eq 0) {
        throw "$Path declares no vulnerabilities. Delete the file instead of leaving it empty."
    }

    foreach ($entry in $entries) {
        $id = $entry.id
        if ([string]::IsNullOrWhiteSpace($id)) {
            throw "$Path has an entry without an id."
        }

        if ([string]::IsNullOrWhiteSpace($entry.statement)) {
            throw "$Path entry $id has no statement. Record the per-CVE reachability assessment."
        }

        $hasPaths = @($entry.paths).Where({ -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0
        $hasPurls = @($entry.purls).Where({ -not [string]::IsNullOrWhiteSpace($_) }).Count -gt 0
        if (-not $hasPaths -and -not $hasPurls) {
            throw "$Path entry $id has neither paths nor purls. Scope the exception to the affected component."
        }

        if ([string]::IsNullOrWhiteSpace($entry.expired_at)) {
            throw "$Path entry $id has no expired_at. Every acceptance needs a re-triage date."
        }
    }

    # Every entry has an expired_at by now, so one raw match per entry is expected, in array order.
    # JSON escapes quotes inside strings, so the pattern only ever matches a real key, never text
    # that happens to sit inside a statement. A mismatch therefore means an expired_at key outside
    # the entries, or a non-string value the pattern cannot see. Pairing by index would be wrong in
    # both cases, so fail loudly instead of guessing.
    $rawExpiries = [regex]::Matches($raw, $ExpiredAtPattern)
    if ($rawExpiries.Count -ne $entries.Count) {
        throw "$Path has $($rawExpiries.Count) expired_at value(s) in text but $($entries.Count) entries. Remove the stray occurrence."
    }

    $now = [DateTimeOffset]::UtcNow
    for ($index = 0; $index -lt $entries.Count; $index++) {
        $id = $entries[$index].id
        $expiredAt = $rawExpiries[$index].Groups['value'].Value

        if ($expiredAt -notmatch $Rfc3339Pattern) {
            throw "$Path entry $id has a non-RFC3339 expired_at '$expiredAt'. Use 2026-11-24T00:00:00Z."
        }

        $expiry = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse(
                $expiredAt,
                [cultureinfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::AssumeUniversal,
                [ref] $expiry)) {
            throw "$Path entry $id has an unparsable expired_at '$expiredAt'."
        }

        if ($expiry -le $now) {
            throw "$Path entry $id expired on $expiredAt. Re-triage it, see security/trivy/README.md."
        }
    }

    Write-Output "Validated $([System.IO.Path]::GetFileName($Path)): $($entries.Count) accepted finding(s)."
}

function Invoke-SelfTest {
    # Proves the validator rejects each way an acceptance can be under-specified. Without this the
    # rules could silently stop firing and every malformed file would sail through the gate.
    $fixtureDirectory = Join-Path $ignoreDirectory 'testdata'
    $expectations = [ordered]@{
        'valid.json'                       = $null
        'purls-only.json'                  = $null
        # Escaped quotes inside a statement must not be mistaken for a key.
        'escaped-expiry-in-statement.json' = $null
        'missing-id.json'        = 'without an id'
        'missing-statement.json' = 'has no statement'
        'missing-scope.json'     = 'neither paths nor purls'
        'missing-expiry.json'    = 'has no expired_at'
        'bare-date-expiry.json'  = 'non-RFC3339'
        'expired.json'           = 'expired on'
        'stray-expiry.json'      = 'expired_at value(s) in text but'
    }

    # Guards against a fixture that no expectation covers, and against an expectation whose entry
    # silently vanished. Windows PowerShell reads a BOM-less script as ANSI, so a non-ASCII comment
    # can swallow the following line and drop a table entry without any error.
    $onDisk = @(Get-ChildItem -LiteralPath $fixtureDirectory -Filter '*.json' | ForEach-Object { $_.Name })
    $expected = @($expectations.Keys)
    $missing = @($onDisk | Where-Object { $expected -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Self test fixtures without an expectation: $($missing -join ', ')"
    }

    $absent = @($expected | Where-Object { $onDisk -notcontains $_ })
    if ($absent.Count -gt 0) {
        throw "Self test expectations without a fixture: $($absent -join ', ')"
    }

    foreach ($fixture in $expectations.Keys) {
        $path = Join-Path $fixtureDirectory $fixture
        if (-not (Test-Path -LiteralPath $path)) {
            throw "Self test fixture is missing: $path"
        }

        $expected = $expectations[$fixture]
        try {
            Test-IgnoreFile -Path $path | Out-Null
            if ($null -ne $expected) {
                throw "Self test failed: $fixture was accepted but should have been rejected with '$expected'."
            }

            Write-Output "Self test ok: $fixture accepted."
        }
        catch {
            $message = $_.Exception.Message
            if ($message -like 'Self test failed:*') {
                throw
            }

            if ($null -eq $expected) {
                throw "Self test failed: $fixture was rejected with '$message' but should have been accepted."
            }

            if ($message -notlike "*$expected*") {
                throw "Self test failed: $fixture was rejected with '$message', expected '$expected'."
            }

            Write-Output "Self test ok: $fixture rejected ($expected)."
        }
    }
}

# $IsLinux only exists from PowerShell 6. Windows PowerShell 5.1 reads it as $null, so the version
# is checked first rather than relying on that.
$script:IsLinuxHost = $false
if ($PSVersionTable.PSVersion.Major -ge 6) {
    $script:IsLinuxHost = [bool] $IsLinux
}

# Bump when the manifest shape changes. Readers must refuse a version they were not written for
# rather than guess, so scripts/production-compose.ps1 checks this exact value.
$script:ProvenanceSchemaVersion = 1

# Rejects anything that is not a full sha256 image ID. A reader that accepts a tag here would
# undo the point of the manifest, and Docker takes both in the same position.
function Test-ImageIdFormat {
    param([string] $ImageId)

    # -cmatch, not -match. PowerShell compares case-insensitively by default, which would accept an
    # uppercase digest here and then hand Docker something it does not resolve.
    return ($ImageId -cmatch '^sha256:[0-9a-f]{64}$')
}

function New-ProvenanceDocument {
    param(
        [Parameter(Mandatory)] $ServiceImages,
        [Parameter(Mandatory)] $Scanned,
        [Parameter(Mandatory)] [string] $ScannerImage
    )

    $idByImage = @{}
    foreach ($entry in $Scanned) {
        $idByImage[$entry.Image] = $entry.ImageId
    }

    # Keyed by service, not by image. Scanning deduplicates by image because scanning the same
    # export twice is waste, but a deployment sets one variable per service, and two services are
    # free to share an image.
    $services = [ordered]@{}
    foreach ($service in $ServiceImages.Keys) {
        $reference = $ServiceImages[$service]

        if (-not $idByImage.ContainsKey($reference)) {
            throw "No scan result for '$reference', used by service '$service'. The manifest has to cover every service in scope."
        }

        $imageId = $idByImage[$reference]
        if (-not (Test-ImageIdFormat -ImageId $imageId)) {
            throw "Service '$service' resolved to '$imageId', which is not a sha256 image ID."
        }

        $services[$service] = [ordered]@{
            reference = $reference
            imageId   = $imageId
        }
    }

    if ($services.Count -eq 0) {
        throw 'No services resolved to an image, so there is nothing to record.'
    }

    return [ordered]@{
        schemaVersion = $script:ProvenanceSchemaVersion
        gateResult    = 'passed'
        generatedAt   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        scannerImage  = $ScannerImage
        services      = $services
    }
}

function Get-ProvenanceDirectory {
    param([Parameter(Mandatory)] [string] $Path)

    $directory = Split-Path -Parent $Path
    if ([string]::IsNullOrWhiteSpace($directory)) {
        return '.'
    }

    return $directory
}

function Clear-ProvenanceOutput {
    param([Parameter(Mandatory)] [string] $Path)

    $directory = Get-ProvenanceDirectory -Path $Path
    if (Test-Path -LiteralPath $directory -PathType Leaf) {
        throw "The directory for -ProvenanceOutputPath is a file: $directory"
    }

    # Created rather than demanded. The documented location is artifacts/, which Git ignores and a
    # fresh checkout therefore does not have, so requiring it would fail every first deployment and
    # every CI run for no protection.
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    if (Test-Path -LiteralPath $Path -PathType Container) {
        throw "-ProvenanceOutputPath points at a directory: $Path"
    }

    # Removed before scanning rather than overwritten after it. A run that fails the gate, or that
    # never reaches the write at all, must leave no manifest behind: a deployment reading the
    # previous run's file would start images this run rejected.
    Remove-Item -LiteralPath $Path -Force
}

function Write-ProvenanceDocument {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] $Document
    )

    $json = $Document | ConvertTo-Json -Depth 6

    # Written under a temporary name in the same directory and then renamed. A reader never sees a
    # partial file, and an interrupted write leaves no manifest rather than a truncated one that
    # would fail to parse at deploy time.
    $directory = Get-ProvenanceDirectory -Path $Path
    $temporary = Join-Path $directory ([System.IO.Path]::GetRandomFileName() + '.provenance.tmp')

    try {
        [System.IO.File]::WriteAllText($temporary, $json, (New-Object System.Text.UTF8Encoding $false))

        # The manifest names what is about to run in production. On the deployment host it sits
        # next to the checkout, so it is kept owner-only for the same reason the export workspace
        # is.
        if ($script:IsLinuxHost) {
            & chmod 600 $temporary
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to restrict permissions on $temporary (chmod exited $LASTEXITCODE)."
            }
        }

        [System.IO.File]::Move($temporary, $Path)
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

# Runs a docker command and returns only its exit code; the command's own output goes straight to
# the host. Injected into Invoke-ImageScan so the self test can substitute a recorder and drive the
# failure paths without a daemon.
$script:RealDockerInvoker = {
    param([string[]] $Arguments)

    & docker @Arguments | Out-Host
    return $LASTEXITCODE
}

# The Docker arguments are the security boundary of this script, so they are built here rather than
# inline: the self test can then assert on exactly what would be passed, with no daemon involved.
# The hardening every container this script starts carries, in one place so the four Trivy steps
# and the fixture preparation cannot drift apart. The self test compares whole option lists, so a
# step that stopped using this would be caught, but sharing it means there is nothing to catch.
function New-HardenedRunOptions {
    param(
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit
    )

    return @(
        '--cap-drop', 'ALL',
        '--security-opt', 'no-new-privileges',
        # --memory-swap equal to --memory turns swap off for this container rather than letting it
        # spill; a host that starts swapping is one the neighbouring application feels too.
        '--memory', $MemoryLimit,
        '--memory-swap', $MemoryLimit,
        '--cpus', $CpuLimit,
        '--pids-limit', $PidsLimit
    )
}

function New-TrivyDbRefreshArguments {
    param(
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit
    )

    # The only Trivy steps allowed to use the network, and the only ones that write to the cache.
    # They mount the cache and nothing else: no image tar, no repository directory. Keep that list
    # closed - the isolation of the scans below is worth nothing if the step holding the network
    # can read what a scan produced. The scans keep their own artifacts out of this volume by
    # mounting it read only and holding them in memory, so what is here is databases and nothing
    # derived from a scanned image.
    # Not driven by a scanned image, but it does download and unpack a database written by a
    # third party, on a host shared with another application, so it is limited like the rest.
    return @('run', '--rm') +
        (New-HardenedRunOptions -MemoryLimit $MemoryLimit -CpuLimit $CpuLimit -PidsLimit $PidsLimit) +
        @(
        '--volume', "$($CacheVolume):/root/.cache/trivy",
        $TrivyImage,
        'image',
        # This step keeps the network so it can fetch the database, which is not a reason to let it
        # talk to anything else. The version notice, the VEX repositories and telemetry are all
        # separate calls upstream, so silencing one does not silence the others.
        '--skip-version-check',
        '--skip-vex-repo-update',
        '--disable-telemetry',
        '--download-db-only'
    )
}

# Checks what is in the cache volume before anything else touches it, and allows only a
# vulnerability database. Two separate things rest on that.
#
# Java archives are refused rather than scanned, and what refuses them is Trivy failing on its first
# run when it meets one with no Java index cached. That failure only happens while the index really
# is absent: --skip-java-db-update stops the index being updated, not used, so a stale one left in
# the volume would be used silently instead.
#
# And the refresh step is the one that keeps the network. A cache that a scan has written to holds
# package lists and other output derived from a scanned image - the reason this moved to a new
# volume name - so handing such a volume to the refresh would undo that separation. -TrivyCacheVolume
# is a parameter and can name any volume, including the retired one, so this runs before the
# refresh rather than after it. Checking afterwards would report the problem only once the
# network-facing container had already been given the data.
function New-CacheContentArguments {
    param(
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit
    )

    return @('run', '--rm', '--network', 'none') +
        (New-HardenedRunOptions -MemoryLimit $MemoryLimit -CpuLimit $CpuLimit -PidsLimit $PidsLimit) +
        @(
        '--volume', "$($CacheVolume):/root/.cache/trivy:ro",
        '--entrypoint', 'sh',
        $TrivyImage,
        '-c',
        # An allowlist, so a cache directory nobody here has thought about also stops the run.
        # An empty volume lists nothing and passes, which is what a first run looks like.
        #
        # The listing is assigned first and its exit code checked, rather than expanded straight
        # into the for. A command substitution that fails expands to nothing, the loop then runs
        # zero times, and the shell exits 0 - so an unreadable or missing cache directory would
        # have read as "nothing unexpected in here" instead of stopping the run.
        #
        # No double quotes anywhere in this command. Windows PowerShell 5.1 does not escape them
        # when it hands an argument to a native program, so a quoted shell word arrives at docker
        # mangled and the test silently stops meaning what it says. An unquoted $entry is safe
        # here and fails closed either way: a cache entry with a space in it makes `[` complain
        # and exit non-zero, which is the answer this check wants for anything unexpected.
        'entries=$(ls -A /root/.cache/trivy) || exit 1; for entry in $entries; do [ $entry = db ] || { echo unexpected cache entry: $entry >&2; exit 1; }; done'
    )
}

# The fixture the Docker self test checks the cache gate against: a volume holding what a scan used
# to leave behind. Built here with the others rather than inline in the test, so it is covered by
# the same whole-option-list comparison - otherwise this would be the one container in the script
# whose hardening could be dropped without the argument self test noticing.
function New-CacheFixturePreparationArguments {
    param(
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit
    )

    return @('run', '--rm', '--network', 'none') +
        (New-HardenedRunOptions -MemoryLimit $MemoryLimit -CpuLimit $CpuLimit -PidsLimit $PidsLimit) +
        @(
        '--volume', "$($CacheVolume):/root/.cache/trivy",
        '--entrypoint', 'sh',
        $TrivyImage,
        '-c',
        'mkdir -p /root/.cache/trivy/db /root/.cache/trivy/fanal'
    )
}

# The provenance line runs Docker too, so it is built here rather than inline: an argument list
# that never passes through a builder is one the self test cannot check, and this one only needs
# to read a version out of the cache.
function New-TrivyVersionArguments {
    param(
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit
    )

    return @('run', '--rm', '--network', 'none') +
        (New-HardenedRunOptions -MemoryLimit $MemoryLimit -CpuLimit $CpuLimit -PidsLimit $PidsLimit) +
        @(
        '--volume', "$($CacheVolume):/root/.cache/trivy:ro",
        $TrivyImage,
        '--version'
    )
}

function New-TrivyScanArguments {
    param(
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [string] $ScanDirectory,
        [Parameter(Mandatory)] [string] $IgnoreDirectory,
        [Parameter(Mandatory)] [string] $TarName,
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit
    )

    # --network none is what makes the exported image unreachable from anywhere but this container.
    # Trivy has update paths besides the vulnerability database - the Java index, rego checks, the
    # version notice, VEX repositories - and telemetry on top of those. Without the network each
    # would fail the run rather than skip quietly, so every one is named here.
    #
    # --skip-java-db-update puts Java out of scope deliberately. Nothing here contains a Java
    # archive (a .NET application, a Go binary and Alpine), and the index is not free: measured at
    # 916 MiB downloaded, 1.4 GB on disk and 55 seconds on 2026-09-04 from mirror.gcr.io, on top of
    # the vulnerability database - a reference figure, not a constant - and CI keeps no cache volume
    # between runs, so it would pay that every build.
    #
    # This fails closed rather than reporting a partial answer. Measured against 0.74.0 on
    # 2026-09-05, with an image built only to carry a JAR and no Java index in the cache:
    #
    #   ERROR [javadb] The first run cannot skip downloading Java DB
    #   FATAL  '--skip-java-db-update' cannot be specified on the first run   (exit 1)
    #
    # So a JAR arriving anywhere in scope stops the gate instead of passing an unexamined archive -
    # including one that turns up inside an image already being scanned, not only a new service.
    # The fix at that point is to add --download-java-db-only to the refresh step. Re-verify this
    # behaviour when the pinned scanner version moves; docs/ci-cd-design.md carries the command.
    #
    # The cache is mounted read only and the scan's own artifacts are held in memory. Trivy's cache
    # holds more than the databases - package lists and other scan output land there too - so
    # sharing it writable would let the refresh step, the one step that keeps the network, read
    # what a scan derived from a target image. Measured against Trivy 0.74.0: a --skip-db-update
    # scan reads the databases fine from a read-only mount. --cache-backend is marked experimental
    # upstream, so re-measure this pair when the pinned scanner version moves.
    return @('run', '--rm', '--network', 'none') +
        (New-HardenedRunOptions -MemoryLimit $MemoryLimit -CpuLimit $CpuLimit -PidsLimit $PidsLimit) +
        @(
        # The one capability handed back, and only to this step. New-ScanWorkspace makes the export
        # directory readable by its owner alone so no other user on the host can read an image, but
        # the scanner runs as root inside the container while that directory belongs to whoever
        # started this script - a different uid. Root without DAC_OVERRIDE cannot cross that, and
        # Trivy fails with "permission denied" on its own input; this was invisible on Docker
        # Desktop, where a bind mount carries no POSIX ownership, and only appeared on Linux CI.
        #
        # It buys nothing else. Everything this container can see is the tar it must read, a
        # read-only cache and a read-only ignore directory. The other steps read Docker volumes,
        # which are root-owned already, so none of them ask for this.
        '--cap-add', 'DAC_OVERRIDE',
        '--volume', "$($CacheVolume):/root/.cache/trivy:ro",
        '--volume', "$($ScanDirectory):/scan:ro",
        '--volume', "$($IgnoreDirectory):/ignore:ro",
        $TrivyImage,
        'image',
        '--input', "/scan/$TarName",
        '--scanners', 'vuln',
        '--cache-backend', 'memory',
        '--skip-db-update',
        '--skip-java-db-update',
        '--skip-check-update',
        '--skip-version-check',
        '--skip-vex-repo-update',
        '--disable-telemetry',
        '--offline-scan'
    )
}

# An export is written outside every container limit, straight onto the filesystem that holds the
# temp directory - on the deployment host, the same one the co-located application's data sits on.
# The tar is roughly the image's uncompressed size, so the room is checked before writing rather
# than discovered as ENOSPC part way through. The cleanup below removes the partial file either
# way, but it cannot give back the minutes during which the disk was full for everything else.
function Test-ExportSpace {
    param([long] $FreeBytes, [long] $ImageBytes)

    # Half as much again, so a run that just fits does not finish by leaving the disk exactly full.
    return ($FreeBytes -ge [long] ($ImageBytes * 1.5))
}

function Get-FreeSpaceBytes {
    param([string] $Path)

    $root = [System.IO.Path]::GetPathRoot((Resolve-Path -LiteralPath $Path).ProviderPath)
    return ([System.IO.DriveInfo]::new($root)).AvailableFreeSpace
}

function New-ScanWorkspace {
    # Under the user's temp directory rather than the repository, so an interrupted run cannot leave
    # an image export inside the build context.
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ('trivy-scan-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null

    # An export is a readable copy of everything the image contains. On Linux /tmp is shared between
    # users, so the directory is narrowed to its owner; Windows temp directories already are.
    #
    # Cleaned up here rather than by the caller: the caller's try/finally only starts once this
    # function has returned a path, so a failure in between would leave the directory behind.
    try {
        if ($script:IsLinuxHost) {
            & chmod 700 $path
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to restrict permissions on $path (chmod exited $LASTEXITCODE)."
            }
        }
    }
    catch {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
        }

        throw
    }

    return $path
}

function Invoke-ImageScan {
    param(
        [Parameter(Mandatory)] [string] $Image,
        [Parameter(Mandatory)] [string] $ImageId,
        [Parameter(Mandatory)] [string] $ScanDirectory,
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [string] $IgnoreDirectory,
        [string] $IgnoreName,
        [string] $IgnorePath,
        [Parameter(Mandatory)] [string] $MemoryLimit,
        [Parameter(Mandatory)] [string] $CpuLimit,
        [Parameter(Mandatory)] [string] $PidsLimit,
        [Parameter(Mandatory)] [scriptblock] $DockerInvoker
    )

    # Exported by image ID, not by tag: a tag can be repointed between the pull or build above and
    # the scan, and the gate would then have inspected something other than what gets started. This
    # closes that window inside this script. It is not a substitute for checking at startup that a
    # running container came from the image that was scanned.
    #
    # One export at a time, deleted as soon as this image has been scanned: an export is a full
    # copy, so holding every image at once would need as much free space as the whole deployment.
    $tarName = 'image-' + [guid]::NewGuid().ToString('N') + '.tar'
    $tarPath = Join-Path $ScanDirectory $tarName

    try {
        $exitCode = & $DockerInvoker @('save', $ImageId, '--output', $tarPath)
        if ($exitCode -ne 0) {
            throw "docker save $Image ($ImageId) failed with exit code $exitCode. Check the free space on $ScanDirectory."
        }

        $scanArgumentParameters = @{
            TrivyImage      = $TrivyImage
            CacheVolume     = $CacheVolume
            ScanDirectory   = $ScanDirectory
            IgnoreDirectory = $IgnoreDirectory
            TarName         = $tarName
            MemoryLimit     = $MemoryLimit
            CpuLimit        = $CpuLimit
            PidsLimit       = $PidsLimit
        }
        $trivyArguments = New-TrivyScanArguments @scanArgumentParameters

        # Write-Host, not Write-Output: these are messages, and the only value this function returns
        # is the pass/fail result. Emitting them into the success stream would leave the caller
        # testing an array of strings, which is always true.
        Write-Host ''
        Write-Host "=== Vulnerability report for $Image ($ImageId) ==="
        $exitCode = & $DockerInvoker ($trivyArguments + @('--severity', 'LOW,MEDIUM,HIGH,CRITICAL', '--format', 'table'))
        if ($exitCode -ne 0) {
            throw "Trivy failed to scan $Image with exit code $exitCode."
        }

        # The same tar the report read, so the gate cannot end up judging a different export.
        $gateArguments = $trivyArguments + @('--severity', 'HIGH,CRITICAL', '--exit-code', '1')
        if (-not [string]::IsNullOrWhiteSpace($IgnorePath) -and (Test-Path -LiteralPath $IgnorePath)) {
            Write-Host "Applying accepted risks from security/trivy/$IgnoreName"
            $gateArguments += @('--ignorefile', "/ignore/$IgnoreName")
        }

        Write-Host "=== Gating HIGH/CRITICAL for $Image ==="
        $exitCode = & $DockerInvoker $gateArguments
        return ($exitCode -eq 0)
    }
    finally {
        # Runs on a passing gate, a failing gate, a Trivy error, a partially written export and
        # Ctrl-C alike. A tar left behind is both space the next run cannot use and a readable copy
        # of the image contents. It cannot cover a killed process, a host shutdown or a power loss:
        # those leave a trivy-scan-* directory in the temp directory for the operator to remove.
        if (Test-Path -LiteralPath $tarPath) {
            Remove-Item -LiteralPath $tarPath -Force
        }
    }
}

function Assert-ScannerSelfTest {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw "Scanner self test failed: $Message"
    }
}

# Everything Docker is told before the image reference, compared against the exact reviewed list
# rather than searched for options known to be dangerous. Enumerating the bad ones cannot hold:
# --use-api-socket mounts the Docker API socket without the string "docker.sock" appearing at all,
# --volumes-from borrows another container's mounts, and the next release can add another. What is
# asserted instead is that this step passes precisely the options it was reviewed as passing, so
# anything new has to be added here before it can run.
function Assert-DockerOptions {
    param([string[]] $Arguments, [string] $ImageReference, [string[]] $Expected, [string] $Step)

    $imageIndex = [array]::IndexOf([array] $Arguments, $ImageReference)
    Assert-ScannerSelfTest ($imageIndex -ge 1) "the $Step does not name the scanner image."

    $actual = @()
    if ($imageIndex -gt 1) {
        $actual = @($Arguments[1..($imageIndex - 1)])
    }

    # -SyncWindow 0 so a reordering counts as a difference too: Docker options are positional
    # enough that "the same set in another order" is not the same command. -CaseSensitive because
    # Compare-Object is not by default, and both Docker option values and container paths are:
    # without it "none" and "NONE", or /scan and /SCAN, compare equal.
    $difference = @(Compare-Object -ReferenceObject @($Expected) -DifferenceObject $actual -SyncWindow 0 -CaseSensitive)
    $rendered = ($difference | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }) -join '; '
    Assert-ScannerSelfTest ($difference.Count -eq 0) "the $Step passes Docker options that are not the reviewed set ('=>' is unreviewed, '<=' is missing): $rendered"
}

# Adjacency matters for Docker: "--network" and "none" are one option, and a check that only asks
# whether both strings appear somewhere would pass on arguments Docker reads differently.
function Test-ArgumentPair {
    param([string[]] $Arguments, [string] $Name, [string] $Value)

    for ($index = 0; $index -lt $Arguments.Count - 1; $index++) {
        if ($Arguments[$index] -eq $Name -and $Arguments[$index + 1] -eq $Value) {
            return $true
        }
    }

    return $false
}

function Invoke-ScannerSelfTest {
    # What this covers is the part of the gate that has no findings to show: that the scanner never
    # receives the Docker socket, that the scan cannot reach the network, that the step which can
    # reach the network cannot reach an exported image, and that an export never outlives the scan
    # that needed it. None of it requires a daemon, so it runs in CI on both PowerShell editions
    # alongside the ignore-file checks.
    $trivyImage = 'aquasec/trivy@sha256:1111111111111111111111111111111111111111111111111111111111111111'
    $cacheVolume = 'self-test-cache'
    $ignoreDirectory = 'self-test-ignore'
    $tarName = 'image-selftest.tar'
    $workspace = New-ScanWorkspace

    try {
        $memoryLimit = '512m'
        $cpuLimit = '1'
        $pidsLimit = '128'
        $limits = @{ MemoryLimit = $memoryLimit; CpuLimit = $cpuLimit; PidsLimit = $pidsLimit }
        $refresh = New-TrivyDbRefreshArguments -TrivyImage $trivyImage -CacheVolume $cacheVolume @limits
        $cacheCheck = New-CacheContentArguments -TrivyImage $trivyImage -CacheVolume $cacheVolume @limits
        $scanArgumentParameters = @{
            TrivyImage      = $trivyImage
            CacheVolume     = $cacheVolume
            ScanDirectory   = $workspace
            IgnoreDirectory = $ignoreDirectory
            TarName         = $tarName
            MemoryLimit     = $memoryLimit
            CpuLimit        = $cpuLimit
            PidsLimit       = $pidsLimit
        }
        $scan = New-TrivyScanArguments @scanArgumentParameters
        $version = New-TrivyVersionArguments -TrivyImage $trivyImage -CacheVolume $cacheVolume @limits
        $fixturePrep = New-CacheFixturePreparationArguments -TrivyImage $trivyImage -CacheVolume $cacheVolume @limits

        foreach ($argument in ($refresh + $cacheCheck + $scan + $version + $fixturePrep)) {
            Assert-ScannerSelfTest (-not ($argument -like '*docker.sock*')) "a Trivy argument mounts the Docker socket: $argument"
        }

        # The pin is enforced on every run, not only here, so what this covers is the validator
        # itself: a substring test would accept "host@sha256:x", which pins nothing.
        Assert-ScannerSelfTest (Test-TrivyImagePinned -Reference ('aquasec/trivy@sha256:' + ('a' * 64))) 'a correctly pinned scanner reference was rejected.'
        $unpinnedReferences = @(
            'aquasec/trivy:0.74.0',
            'aquasec/trivy',
            '',
            'evil@example.com@sha256:x',
            ('aquasec/trivy@sha256:' + ('a' * 63)),
            ('aquasec/trivy@sha256:' + ('a' * 65)),
            ('aquasec/trivy@sha256:' + ('g' * 64)),
            ('aquasec/trivy@sha1:' + ('a' * 64))
        )
        foreach ($reference in $unpinnedReferences) {
            Assert-ScannerSelfTest (-not (Test-TrivyImagePinned -Reference $reference)) "an unpinned scanner reference was accepted: '$reference'"
        }

        Assert-ScannerSelfTest ($refresh -contains '--download-db-only') 'the database refresh does not pass --download-db-only.'

        # Exact allowlists rather than a search for known-bad mounts: what a step is allowed to see
        # is a short list, so anything added later has to be justified here before it can run.
        $hardening = @(
            '--cap-drop', 'ALL',
            '--security-opt', 'no-new-privileges',
            '--memory', $memoryLimit,
            '--memory-swap', $memoryLimit,
            '--cpus', $cpuLimit,
            '--pids-limit', $pidsLimit
        )
        $cacheReadOnly = @('--volume', "$($cacheVolume):/root/.cache/trivy:ro")

        Assert-DockerOptions -Arguments $refresh -ImageReference $trivyImage -Step 'database refresh' -Expected (
            @('--rm') + $hardening + @('--volume', "$($cacheVolume):/root/.cache/trivy"))
        Assert-DockerOptions -Arguments $cacheCheck -ImageReference $trivyImage -Step 'cache content check' -Expected (
            @('--rm', '--network', 'none') + $hardening + $cacheReadOnly + @('--entrypoint', 'sh'))
        Assert-DockerOptions -Arguments $version -ImageReference $trivyImage -Step 'version check' -Expected (
            @('--rm', '--network', 'none') + $hardening + $cacheReadOnly)
        # Only the scan gets DAC_OVERRIDE back, because it is the only step that reads a host
        # directory this script deliberately closed to other users.
        Assert-DockerOptions -Arguments $scan -ImageReference $trivyImage -Step 'scan' -Expected (
            @('--rm', '--network', 'none') + $hardening + @('--cap-add', 'DAC_OVERRIDE') + $cacheReadOnly + @(
                '--volume', "$($workspace):/scan:ro",
                '--volume', "$($ignoreDirectory):/ignore:ro"))
        Assert-DockerOptions -Arguments $fixturePrep -ImageReference $trivyImage -Step 'cache fixture preparation' -Expected (
            @('--rm', '--network', 'none') + $hardening + @(
                '--volume', "$($cacheVolume):/root/.cache/trivy",
                '--entrypoint', 'sh'))

        foreach ($other in @(
                @{ Arguments = $refresh; Step = 'database refresh' },
                @{ Arguments = $cacheCheck; Step = 'cache content check' },
                @{ Arguments = $version; Step = 'version check' },
                @{ Arguments = $fixturePrep; Step = 'cache fixture preparation' })) {
            Assert-ScannerSelfTest (-not ($other.Arguments -contains 'DAC_OVERRIDE')) "the $($other.Step) takes DAC_OVERRIDE back; only the scan reads a host path that needs it."
        }

        Assert-ScannerSelfTest (-not ($refresh -contains '--input')) 'the database refresh reads an image tar.'

        # Both the refusal of Java archives and the separation of the network-facing refresh from
        # scan output rest on what is in that volume, so the run has to establish it rather than
        # assume it - and read only, with no network, before the refresh is handed the same volume.
        Assert-ScannerSelfTest ((($cacheCheck -join ' ') -like '*ls -A /root/.cache/trivy*')) 'the cache content check does not list the cache.'
        Assert-ScannerSelfTest ((($cacheCheck -join ' ') -like '*= db *')) 'the cache content check does not restrict the cache to a vulnerability database.'
        # A failed listing expands to nothing, so a for loop reading it directly would run zero
        # times and report success on a cache it could not read at all.
        Assert-ScannerSelfTest ((($cacheCheck -join ' ') -like '*|| exit 1*')) 'the cache content check does not stop when the listing itself fails.'

        # Cleanup is part of what the Docker self test guarantees, not an afterthought: the
        # workspace holds an image export, so one step failing must not stop the rest, and no
        # failure may be swallowed. A List rather than += because a scriptblock assigning to a
        # variable from an enclosing scope writes a copy of its own.
        $ran = New-Object System.Collections.Generic.List[string]
        $cleanupFailures = @(Invoke-CleanupSteps -Steps @(
                { $ran.Add('first'); throw 'first failed' },
                { $ran.Add('second') },
                { $ran.Add('third'); throw 'third failed' }
            ))
        Assert-ScannerSelfTest ($ran.Count -eq 3) "a failing cleanup step stopped the ones after it; only '$($ran -join ', ')' ran."
        Assert-ScannerSelfTest ($cleanupFailures.Count -eq 2) "cleanup failures were lost: two steps threw, $($cleanupFailures.Count) were reported."
        Assert-ScannerSelfTest ((($cleanupFailures -join ' ') -like '*first failed*') -and (($cleanupFailures -join ' ') -like '*third failed*')) 'the reported cleanup failures do not say what went wrong.'

        # The export lands outside every container limit, so the room for it is arithmetic this
        # script does itself.
        Assert-ScannerSelfTest (Test-ExportSpace -FreeBytes 3000 -ImageBytes 2000) 'an export with room to spare was refused.'
        Assert-ScannerSelfTest (Test-ExportSpace -FreeBytes 3001 -ImageBytes 2000) 'an export with room to spare was refused.'
        Assert-ScannerSelfTest (-not (Test-ExportSpace -FreeBytes 2999 -ImageBytes 2000)) 'an export was allowed with less than the margin free.'
        Assert-ScannerSelfTest (-not (Test-ExportSpace -FreeBytes 2000 -ImageBytes 2000)) 'an export was allowed with only its own size free, leaving the disk exactly full.'
        Assert-ScannerSelfTest (-not (Test-ExportSpace -FreeBytes 0 -ImageBytes 1)) 'an export was allowed onto a full disk.'

        # Docker treats several values as "no limit", so the values are checked, not just the flags.
        Assert-ScannerSelfTest (Test-MemoryLimitValue -Value '1g') 'a valid memory limit was rejected.'
        Assert-ScannerSelfTest (Test-CpuLimitValue -Value '1.5') 'a valid CPU limit was rejected.'
        Assert-ScannerSelfTest (Test-PidsLimitValue -Value '512') 'a valid process limit was rejected.'
        foreach ($rejected in @('0', '', '-1', 'abc', '0m')) {
            Assert-ScannerSelfTest (-not (Test-MemoryLimitValue -Value $rejected)) "an unbounded memory limit was accepted: '$rejected'"
        }
        foreach ($rejected in @('0', '', '-1', 'abc', '0.0')) {
            Assert-ScannerSelfTest (-not (Test-CpuLimitValue -Value $rejected)) "an unbounded CPU limit was accepted: '$rejected'"
        }
        foreach ($rejected in @('0', '', '-1', 'abc', '1.5')) {
            Assert-ScannerSelfTest (-not (Test-PidsLimitValue -Value $rejected)) "an unbounded process limit was accepted: '$rejected'"
        }

        # Keeping the network to fetch a database is not a reason to let this step talk to anything
        # else, and the scan-side assertions below would not notice if these were dropped here.
        foreach ($flag in @('--skip-version-check', '--skip-vex-repo-update', '--disable-telemetry')) {
            Assert-ScannerSelfTest ($refresh -contains $flag) "the database refresh does not pass $flag, so it reaches out for more than the database."
        }

        # What follows the image reference is Trivy's own arguments, which the Docker option
        # comparison above does not reach.
        Assert-ScannerSelfTest (-not ($version -contains '--input')) 'the version check reads an image tar.'
        Assert-ScannerSelfTest (Test-ArgumentPair -Arguments $scan -Name '--input' -Value "/scan/$tarName") 'the scan does not read the exported tar.'
        Assert-ScannerSelfTest (Test-ArgumentPair -Arguments $scan -Name '--cache-backend' -Value 'memory') 'the scan keeps its artifacts in the shared cache instead of in memory.'

        foreach ($flag in @('--skip-db-update', '--skip-java-db-update', '--skip-check-update', '--skip-version-check', '--skip-vex-repo-update', '--disable-telemetry', '--offline-scan')) {
            Assert-ScannerSelfTest ($scan -contains $flag) "the scan does not pass $flag, so Trivy would try to reach the network and fail the run instead of skipping."
        }

        Write-Output 'Scanner self test ok: no Docker socket, scan sealed with --network none, refresh has no tar in reach.'

        # The manifest decides which images a deployment starts, so its failure modes are checked
        # here rather than left to a real run. All of these are pure or filesystem-only, which is
        # why they belong in the argument-level self test and not the Docker one.
        Assert-ScannerSelfTest (Test-ImageIdFormat -ImageId ('sha256:' + ('a' * 64))) 'a valid image ID was rejected.'
        Assert-ScannerSelfTest (-not (Test-ImageIdFormat -ImageId 'postgres:16-alpine')) 'a tag was accepted as an image ID, which is the substitution the manifest exists to prevent.'
        Assert-ScannerSelfTest (-not (Test-ImageIdFormat -ImageId ('sha256:' + ('a' * 63)))) 'a short digest was accepted as an image ID.'
        Assert-ScannerSelfTest (-not (Test-ImageIdFormat -ImageId ('SHA256:' + ('A' * 64)))) 'an uppercase digest was accepted; Docker reports lowercase and a case difference would break comparison.'

        $selfTestServiceImages = [ordered]@{
            app      = 'web-writing-tool-app:local'
            postgres = 'postgres:16-alpine'
            migrate  = 'postgres:16-alpine'
        }
        $selfTestScanned = @(
            [pscustomobject]@{ Image = 'web-writing-tool-app:local'; ImageId = 'sha256:' + ('b' * 64) },
            [pscustomobject]@{ Image = 'postgres:16-alpine'; ImageId = 'sha256:' + ('c' * 64) }
        )

        $selfTestDocument = New-ProvenanceDocument -ServiceImages $selfTestServiceImages -Scanned $selfTestScanned -ScannerImage 'aquasec/trivy@sha256:'
        Assert-ScannerSelfTest ($selfTestDocument.gateResult -eq 'passed') 'the manifest does not record gateResult passed.'
        Assert-ScannerSelfTest ($selfTestDocument.schemaVersion -eq $script:ProvenanceSchemaVersion) 'the manifest does not record the schema version.'
        Assert-ScannerSelfTest ($selfTestDocument.services.Count -eq 3) 'the manifest dropped a service; scanning deduplicates by image but a deployment needs one entry per service.'
        Assert-ScannerSelfTest ($selfTestDocument.services['postgres'].imageId -eq $selfTestDocument.services['migrate'].imageId) 'two services sharing an image did not resolve to the same ID.'
        Assert-ScannerSelfTest ($selfTestDocument.services['app'].imageId -eq ('sha256:' + ('b' * 64))) 'a service resolved to the wrong image ID.'

        $unscannedServices = [ordered]@{ app = 'never-scanned:local' }
        $unscannedRejected = $false
        try {
            New-ProvenanceDocument -ServiceImages $unscannedServices -Scanned $selfTestScanned -ScannerImage 'x' | Out-Null
        }
        catch {
            $unscannedRejected = $true
        }
        Assert-ScannerSelfTest $unscannedRejected 'a service with no scan result was recorded, so a deployment could start an image the gate never saw.'

        $provenanceWorkspace = New-ScanWorkspace
        try {
            $provenancePath = Join-Path $provenanceWorkspace 'manifest.json'

            Set-Content -LiteralPath $provenancePath -Value 'stale manifest from an earlier run'
            Clear-ProvenanceOutput -Path $provenancePath
            Assert-ScannerSelfTest (-not (Test-Path -LiteralPath $provenancePath)) 'a previous manifest survived the pre-scan clear, so a failed run would leave an approved-looking file behind.'

            Write-ProvenanceDocument -Path $provenancePath -Document $selfTestDocument
            Assert-ScannerSelfTest (Test-Path -LiteralPath $provenancePath) 'the manifest was not written.'

            $roundTrip = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
            Assert-ScannerSelfTest ($roundTrip.services.app.imageId -eq ('sha256:' + ('b' * 64))) 'the written manifest does not round-trip through JSON.'

            $leftovers = @(Get-ChildItem -LiteralPath $provenanceWorkspace -Filter '*.provenance.tmp' -File)
            Assert-ScannerSelfTest ($leftovers.Count -eq 0) 'the atomic write left its temporary file behind.'

            $createdDirectory = Join-Path $provenanceWorkspace 'not-yet-there'
            Clear-ProvenanceOutput -Path (Join-Path $createdDirectory 'manifest.json')
            Assert-ScannerSelfTest (Test-Path -LiteralPath $createdDirectory -PathType Container) 'the output directory was not created, so a first deployment would fail on the missing artifacts directory.'

            $fileAsDirectory = Join-Path $provenanceWorkspace 'a-file'
            Set-Content -LiteralPath $fileAsDirectory -Value 'not a directory'
            $fileAsDirectoryRejected = $false
            try {
                Clear-ProvenanceOutput -Path (Join-Path $fileAsDirectory 'manifest.json')
            }
            catch {
                $fileAsDirectoryRejected = $true
            }
            Assert-ScannerSelfTest $fileAsDirectoryRejected 'a manifest path under a regular file was accepted, so the failure would land after the build instead of before it.'
        }
        finally {
            if (Test-Path -LiteralPath $provenanceWorkspace) {
                Remove-Item -LiteralPath $provenanceWorkspace -Recurse -Force
            }
        }

        Write-Output 'Scanner self test ok: the manifest covers every service, records only a passed gate, and clears a stale file before scanning.'

        # The flow is driven through a recorder rather than Docker, so the failure paths - which a
        # real run only reaches when something is already wrong - are exercised every build.
        $recorded = New-Object System.Collections.Generic.List[object]
        $failAt = ''
        $fakeDocker = {
            param([string[]] $Arguments)

            $recorded.Add([string[]] $Arguments)

            if ($Arguments[0] -eq 'save') {
                # Written even when the save is told to fail, so the partial-export case leaves
                # something behind for the cleanup assertion to be about.
                Set-Content -LiteralPath $Arguments[3] -Value 'fake image export'
                if ($failAt -eq 'save') {
                    return 3
                }

                return 0
            }

            if ($Arguments -contains '--exit-code') {
                if ($failAt -eq 'gate') {
                    return 1
                }

                return 0
            }

            if ($failAt -eq 'report') {
                return 2
            }

            return 0
        }

        $flowParameters = @{
            Image           = 'self-test-image:local'
            ImageId         = 'sha256:1234567890abcdef'
            ScanDirectory   = $workspace
            TrivyImage      = $trivyImage
            CacheVolume     = $cacheVolume
            IgnoreDirectory = $ignoreDirectory
            MemoryLimit     = $memoryLimit
            CpuLimit        = $cpuLimit
            PidsLimit       = $pidsLimit
            DockerInvoker   = $fakeDocker
        }

        $recorded.Clear()
        $failAt = ''
        $passed = Invoke-ImageScan @flowParameters
        Assert-ScannerSelfTest ($passed -is [bool]) 'Invoke-ImageScan returned something other than a boolean; a caller testing it would always see true.'
        Assert-ScannerSelfTest $passed 'a passing gate was reported as a failure.'
        Assert-ScannerSelfTest ($recorded.Count -eq 3) "expected a save, a report and a gate, got $($recorded.Count) docker calls."
        Assert-ScannerSelfTest ($recorded[0][0] -eq 'save') 'the image was not exported before scanning.'
        Assert-ScannerSelfTest ($recorded[0][1] -eq $flowParameters.ImageId) 'the export used something other than the resolved image ID, so a moved tag could still be scanned.'

        $reportInput = [array]::IndexOf([array] $recorded[1], '--input')
        $gateInput = [array]::IndexOf([array] $recorded[2], '--input')
        Assert-ScannerSelfTest ($reportInput -ge 0 -and $gateInput -ge 0) 'a scan ran without --input.'
        Assert-ScannerSelfTest ($recorded[1][$reportInput + 1] -eq $recorded[2][$gateInput + 1]) 'the report and the gate read different tars.'
        Assert-ScannerSelfTest (-not (Test-Path -LiteralPath $recorded[0][3])) 'the export was left behind after a passing gate.'

        $recorded.Clear()
        $failAt = 'gate'
        $passed = Invoke-ImageScan @flowParameters
        Assert-ScannerSelfTest ($passed -is [bool] -and -not $passed) 'a failing gate was reported as a pass.'
        Assert-ScannerSelfTest (-not (Test-Path -LiteralPath $recorded[0][3])) 'the export was left behind after a failing gate.'

        foreach ($stage in @('save', 'report')) {
            $recorded.Clear()
            $failAt = $stage
            $threw = $false
            try {
                Invoke-ImageScan @flowParameters | Out-Null
            }
            catch {
                $threw = $true
            }

            Assert-ScannerSelfTest $threw "a failing $stage did not stop the run, so a scan could be reported against an image that was never exported."
            Assert-ScannerSelfTest (-not (Test-Path -LiteralPath $recorded[0][3])) "the export was left behind after a failing $stage."
        }

        Write-Output 'Scanner self test ok: exports use the image ID, the report and gate share one tar, and every failure path deletes it.'
    }
    finally {
        if (Test-Path -LiteralPath $workspace) {
            Remove-Item -LiteralPath $workspace -Recurse -Force
        }
    }
}

# Two properties the argument assertions cannot reach, because they are about what Trivy and the
# shell actually do rather than about what this script types. Both were established by hand once
# during review; this is what keeps them true when the pinned scanner version moves. Needs a
# daemon, so it runs from the Docker job rather than alongside the ignore-file checks.
# Removing something that is not there is not an error worth ending a run over, but `docker rm`
# says so on stderr, and Windows PowerShell turns a native command's stderr into an ErrorRecord that
# $ErrorActionPreference = 'Stop' then treats as terminating. So presence is established first with
# a command that stays quiet when it finds nothing.
function Remove-DockerObjectIfPresent {
    param([ValidateSet('volume', 'image')] [string] $Kind, [string] $Name)

    if ($Kind -eq 'volume') {
        $found = @(& docker volume ls --quiet --filter "name=^$Name$")
    }
    else {
        $found = @(& docker images --quiet $Name)
    }

    # Every exit code checked. A native command that fails does not raise here, so without this a
    # removal that Docker refused would look exactly like one that succeeded, and the caller would
    # report a clean run over a fixture it had not actually removed.
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list the $Kind '$Name' to remove it (exit $LASTEXITCODE)."
    }

    if ($found.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($found[0])) {
        & docker $Kind rm $Name | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Could not remove the $Kind '$Name' (exit $LASTEXITCODE)."
        }
    }
}

# Runs every step even when an earlier one fails, and hands back what went wrong. Cleanup is not a
# best-effort afterthought here: the workspace holds an image export, so a caller that cannot
# remove it has to say so rather than report a clean run.
function Invoke-CleanupSteps {
    param([scriptblock[]] $Steps)

    $failures = @()
    foreach ($step in $Steps) {
        try {
            & $step
        }
        catch {
            $failures += $_.Exception.Message
        }
    }

    return $failures
}

# Hands back both the exit code and everything Docker wrote, so an assertion can say which failure
# it saw rather than accepting any of them. $ErrorActionPreference is relaxed for the call:
# Windows PowerShell turns a native command's stderr into an ErrorRecord, and 'Stop' would end the
# run on the very output being read.
function Invoke-DockerCapturing {
    param([string[]] $Arguments)

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

function Invoke-ScannerDockerSelfTest {
    param(
        [Parameter(Mandatory)] [string] $TrivyImage,
        [Parameter(Mandatory)] [string] $CacheVolume,
        [Parameter(Mandatory)] [hashtable] $ResourceLimits
    )

    # Named per run. A fixed name would make `docker volume rm` in the cleanup below destroy a
    # volume of the same name that belonged to somebody else, and two runs on one host would delete
    # each other's fixtures half way through.
    $runId = [guid]::NewGuid().ToString('N')
    $primaryError = $null
    $cleanupErrors = @()
    $workspace = New-ScanWorkspace
    $fixtureTag = "scan-image-selftest-java-$($runId):local"
    $fixtureVolume = "scan-image-selftest-cache-$runId"

    try {
        # Before the refresh, for the same reason the real run checks first: the refresh is the
        # container that keeps the network, and handing it a volume a scan has written to is what
        # the check exists to prevent. Doing this after the refresh would reproduce inside the self
        # test the exact ordering bug the self test is here to guard against.
        & docker volume create $CacheVolume | Out-Null
        $exitCode = & $script:RealDockerInvoker (New-CacheContentArguments -TrivyImage $TrivyImage -CacheVolume $CacheVolume @ResourceLimits)
        if ($exitCode -ne 0) {
            throw "The Docker self test will not use $($CacheVolume) - it holds something other than a vulnerability database, or the check could not run (exit $exitCode)."
        }

        # The database has to be in the cache next. Without it the scan below would fail for the
        # wrong reason and the Java assertion would pass without proving anything.
        $exitCode = & $script:RealDockerInvoker (New-TrivyDbRefreshArguments -TrivyImage $TrivyImage -CacheVolume $CacheVolume @ResourceLimits)
        if ($exitCode -ne 0) {
            throw "The Docker self test could not refresh the vulnerability database (exit $exitCode)."
        }

        # Java archives are out of scope only because Trivy stops rather than half-identifying one.
        # A JAR is a zip that Trivy picks by extension, so the smallest possible one will do.
        $manifestDirectory = Join-Path (Join-Path $workspace 'jar') 'META-INF'
        New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $manifestDirectory 'MANIFEST.MF') -Value 'Manifest-Version: 1.0'
        $archivePath = Join-Path $workspace 'test.zip'
        Compress-Archive -Path (Join-Path (Join-Path $workspace 'jar') '*') -DestinationPath $archivePath -Force
        Move-Item -LiteralPath $archivePath -Destination (Join-Path $workspace 'test.jar') -Force
        Set-Content -LiteralPath (Join-Path $workspace 'Dockerfile') -Value @('FROM scratch', 'COPY test.jar /opt/test.jar')

        & docker build --quiet --tag $fixtureTag $workspace | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "The Docker self test could not build the Java fixture image (exit $LASTEXITCODE)."
        }

        $tarName = 'java-fixture.tar'
        & docker save $fixtureTag --output (Join-Path $workspace $tarName) | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "The Docker self test could not export the Java fixture image (exit $LASTEXITCODE)."
        }

        # No --exit-code here: findings would then be indistinguishable from the refusal, and this
        # fixture has no packages to find.
        #
        # The message is checked, not only the exit code. Docker answers 125, 126 and 127 for its
        # own errors, a failed exec and a missing command, a mount problem or an OOM kill is also
        # non-zero, and so is any other Trivy fatal - so "non-zero" alone would let a thoroughly
        # broken scan stand in as proof that Java stops the gate.
        $scanParameters = @{
            TrivyImage      = $TrivyImage
            CacheVolume     = $CacheVolume
            ScanDirectory   = $workspace
            IgnoreDirectory = $ignoreDirectory
            TarName         = $tarName
        } + $ResourceLimits
        $result = Invoke-DockerCapturing -Arguments (New-TrivyScanArguments @scanParameters)
        Write-Output $result.Output
        Assert-ScannerSelfTest ($result.ExitCode -ne 0) 'Trivy scanned an image holding a Java archive with no Java index cached instead of stopping. Java is out of scope only because that stops the run - see docs/ci-cd-design.md.'
        Assert-ScannerSelfTest ($result.Output -match 'cannot be specified on the first run') "the scan of the Java fixture failed for some reason other than the missing Java index, so it proves nothing about Java being out of scope. Trivy said: $($result.Output)"
        Write-Output 'Docker self test ok: a Java archive with no index cached stops the scan.'

        # And the cache check has to fail on a volume a scan has written to, which is what the
        # shell command in it is for. The fixture is prepared under the same limits as everything
        # else this script starts.
        $prepareParameters = @{ TrivyImage = $TrivyImage; CacheVolume = $fixtureVolume } + $ResourceLimits
        $exitCode = & $script:RealDockerInvoker (New-CacheFixturePreparationArguments @prepareParameters)
        if ($exitCode -ne 0) {
            throw "The Docker self test could not prepare the cache fixture volume (exit $exitCode)."
        }

        $exitCode = & $script:RealDockerInvoker (New-CacheContentArguments -TrivyImage $TrivyImage -CacheVolume $fixtureVolume @ResourceLimits)
        Assert-ScannerSelfTest ($exitCode -ne 0) 'the cache content check passed a volume holding a scan cache, so the network-facing refresh could be handed data derived from a scanned image.'

        $exitCode = & $script:RealDockerInvoker (New-CacheContentArguments -TrivyImage $TrivyImage -CacheVolume $CacheVolume @ResourceLimits)
        Assert-ScannerSelfTest ($exitCode -eq 0) "the cache content check rejected $CacheVolume, which holds only a database; the check is refusing what it should accept."
        Write-Output 'Docker self test ok: the cache check refuses a volume a scan has written to and accepts a database-only one.'
    }
    catch {
        # Held rather than allowed to propagate, so the cleanup below still runs and its own
        # failures can be reported alongside this one instead of replacing it.
        $primaryError = $_
    }
    finally {
        # Each step attempted on its own. An earlier version ran them in sequence, one `docker
        # volume rm` for a volume that was not there ended the block, and the image export stayed
        # on disk - which is the one thing here that must not survive. The workspace goes first for
        # the same reason.
        $cleanupErrors = Invoke-CleanupSteps -Steps @(
            { if (Test-Path -LiteralPath $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force } },
            { Remove-DockerObjectIfPresent -Kind image -Name $fixtureTag },
            { Remove-DockerObjectIfPresent -Kind volume -Name $fixtureVolume }
        )
    }

    if ($null -ne $primaryError) {
        throw $primaryError
    }

    # A failed cleanup fails the test. Warning about it and exiting 0 would let CI pass over an
    # image export still sitting in the temp directory, which is the state this whole change exists
    # to avoid.
    if (@($cleanupErrors).Count -gt 0) {
        throw "The Docker self test could not finish cleaning up, so something it created may still be on this host: $(@($cleanupErrors) -join '; ')"
    }
}

if ($SelfTest) {
    Invoke-SelfTest
    Invoke-ScannerSelfTest
}

foreach ($file in Get-ChildItem -LiteralPath $ignoreDirectory -Filter '*.trivyignore.yaml' -ErrorAction SilentlyContinue) {
    Test-IgnoreFile -Path $file.FullName
}

if ($ValidateOnly) {
    Write-Output 'Validation only: skipping image scan.'
    exit 0
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker was not found. Start Docker and retry."
}

if ($DockerSelfTest) {
    Invoke-ScannerDockerSelfTest -TrivyImage $TrivyImage -CacheVolume $TrivyCacheVolume -ResourceLimits $resourceLimits
    Write-Output 'Docker self test passed.'
    exit 0
}

# "powershell -File" passes every argument as a string, so -ComposeFile a,b arrives as one string
# rather than an array. Split here so the documented -File invocation and a direct pwsh call with a
# real array both work.
$ComposeFile = @($ComposeFile | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

# Resolved and cleared before the build and the scans, not after them. A bad path should fail here,
# in seconds, rather than after a twenty minute build; and clearing first is what makes the
# manifest's presence mean "this run passed" instead of "some run passed at some point".
if (-not [string]::IsNullOrWhiteSpace($ProvenanceOutputPath)) {
    if (-not [System.IO.Path]::IsPathRooted($ProvenanceOutputPath)) {
        $ProvenanceOutputPath = Join-Path $repoRoot $ProvenanceOutputPath
    }

    Clear-ProvenanceOutput -Path $ProvenanceOutputPath
}

$composeOptions = @()
$envFile = Join-Path $repoRoot '.env'
if (-not (Test-Path -LiteralPath $envFile)) {
    $envFile = Join-Path $repoRoot '.env.production.example'
}
if (Test-Path -LiteralPath $envFile) {
    $composeOptions += @('--env-file', $envFile)
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

# The shell environment wins over --env-file in Compose, so APP_IMAGE resolves to the image that
# was actually built here even when the env file carries the placeholder value.
$env:APP_IMAGE = $AppImage

$configJson = & docker compose @composeOptions config --format json
if ($LASTEXITCODE -ne 0) {
    throw "docker compose config failed with exit code $LASTEXITCODE."
}

$config = ($configJson | Out-String) | ConvertFrom-Json
$targets = New-Object System.Collections.Generic.List[object]
# Kept alongside $targets rather than derived from it: $targets is deduplicated by image, which is
# right for scanning and wrong for the manifest, where each service needs its own entry even when
# two of them share an image.
$serviceImages = [ordered]@{}
foreach ($service in $config.services.PSObject.Properties) {
    $image = $service.Value.image
    if ([string]::IsNullOrWhiteSpace($image)) {
        continue
    }

    $serviceImages[$service.Name] = $image

    $targets.Add([pscustomobject]@{
            Image   = $image
            # Images Compose knows how to build are ours. Pulling them would fail or fetch a
            # different artifact, so they are built locally instead.
            IsLocal = $null -ne $service.Value.build
        })
}

$targets = @($targets | Sort-Object -Property Image -Unique)
if ($targets.Count -eq 0) {
    throw "docker compose config returned no images."
}

Write-Output "Images in scope: $(($targets | ForEach-Object { $_.Image }) -join ', ')"

if ($Build) {
    # --pull is required. A stale local base image keeps shipping patches that were already fixed
    # upstream, and the scan would then fail on vulnerabilities a rebuild fixes.
    & docker compose @composeOptions build --pull
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# One shared cache volume, otherwise every scan downloads the vulnerability database again.
& docker volume create $TrivyCacheVolume | Out-Null

# Refreshed once for the whole run by the only Trivy step that keeps the network, so every scan
# below can be sealed off. It mounts the cache and nothing else.
# Before the refresh, because the refresh is the container that keeps the network and this decides
# whether the volume is one it may be given at all.
Write-Output "Checking the contents of $TrivyCacheVolume..."
$cacheArguments = New-CacheContentArguments -TrivyImage $TrivyImage -CacheVolume $TrivyCacheVolume @resourceLimits
$cacheExitCode = & $script:RealDockerInvoker $cacheArguments
if ($cacheExitCode -ne 0) {
    throw "$TrivyCacheVolume holds something other than a vulnerability database, or the check itself could not run (exit $cacheExitCode). A Java index there would be used instead of stopping a Java archive at the gate, and a scan cache there would hand data derived from a scanned image to the step that keeps the network. Point -TrivyCacheVolume at a volume no scan has written to, remove the extra entries, or take the new content into scope deliberately and re-verify. See docs/ci-cd-design.md."
}

Write-Output 'Refreshing the Trivy vulnerability database...'
$refreshArguments = New-TrivyDbRefreshArguments -TrivyImage $TrivyImage -CacheVolume $TrivyCacheVolume @resourceLimits
$refreshExitCode = & $script:RealDockerInvoker $refreshArguments
if ($refreshExitCode -ne 0) {
    throw "Refreshing the Trivy database failed with exit code $refreshExitCode."
}

# A finding only means something alongside what produced it. The version output carries the
# database timestamps, so this one block dates the findings and identifies the scanner that made
# them; the image IDs it was run against are printed at the end of the run.
Write-Output ''
Write-Output '=== Scan provenance ==='
Write-Output "Scanner image: $TrivyImage"
$versionArguments = New-TrivyVersionArguments -TrivyImage $TrivyImage -CacheVolume $TrivyCacheVolume @resourceLimits
$scannerVersion = @(& docker @versionArguments)
if ($LASTEXITCODE -ne 0) {
    throw "Reading the scanner version failed with exit code $LASTEXITCODE."
}

$scannerVersion | ForEach-Object { Write-Output "  $_" }

$scanDirectory = New-ScanWorkspace
$scanned = New-Object System.Collections.Generic.List[object]

$failed = New-Object System.Collections.Generic.List[string]
try {
    foreach ($target in $targets) {
        $image = $target.Image

        # Third party images are pulled so the newest published tag is evaluated. A cached tag hides
        # fixes that upstream already shipped.
        if (-not $SkipPull -and -not $target.IsLocal) {
            & docker pull $image
            if ($LASTEXITCODE -ne 0) {
                throw "docker pull $image failed with exit code $LASTEXITCODE."
            }
        }

        # Resolved after the pull or the build, and everything below refers to this ID rather than
        # to the tag. A locally built image that was never built yields an opaque Trivy failure
        # otherwise, so the missing case says what to do.
        $inspected = @(& docker image inspect $image --format '{{.Id}} {{.Size}}')
        if ($LASTEXITCODE -ne 0 -or $inspected.Count -eq 0 -or [string]::IsNullOrWhiteSpace($inspected[0])) {
            throw "$image is not present locally. Run this script with -Build, or build it first."
        }

        $inspectedFields = $inspected[0].Trim() -split '\s+'
        $resolvedImageId = $inspectedFields[0]
        $imageBytes = [long] $inspectedFields[1]

        # Per image rather than once for all of them: exports are written and deleted one at a
        # time, so what has to fit is the largest single image, not their total.
        $freeBytes = Get-FreeSpaceBytes -Path $scanDirectory
        if (-not (Test-ExportSpace -FreeBytes $freeBytes -ImageBytes $imageBytes)) {
            throw "Not enough room to export $image. The image is $([math]::Round($imageBytes / 1GB, 2)) GB and $scanDirectory has $([math]::Round($freeBytes / 1GB, 2)) GB free; an export needs half as much again. Free space on that filesystem or point the temp directory at one with room - on a shared host, filling this disk stops more than this scan."
        }
        $scanned.Add([pscustomobject]@{ Image = $image; ImageId = $resolvedImageId })

        $repository = $image.Split(':')[0]
        $ignoreName = ($repository.Split('/') | Select-Object -Last 1) + '.trivyignore.yaml'

        $scanParameters = @{
            Image           = $image
            ImageId         = $resolvedImageId
            ScanDirectory   = $scanDirectory
            TrivyImage      = $TrivyImage
            CacheVolume     = $TrivyCacheVolume
            IgnoreDirectory = $ignoreDirectory
            IgnoreName      = $ignoreName
            IgnorePath      = (Join-Path $ignoreDirectory $ignoreName)
            MemoryLimit     = $ScanMemoryLimit
            CpuLimit        = $ScanCpuLimit
            PidsLimit       = $ScanPidsLimit
            DockerInvoker   = $script:RealDockerInvoker
        }

        if (-not (Invoke-ImageScan @scanParameters)) {
            $failed.Add($image)
        }
    }
}
finally {
    if (Test-Path -LiteralPath $scanDirectory) {
        Remove-Item -LiteralPath $scanDirectory -Recurse -Force
    }
}

Write-Output ''
Write-Output '=== Images scanned ==='
foreach ($entry in $scanned) {
    Write-Output "  $($entry.Image) $($entry.ImageId)"
}

Write-Output ''
if ($failed.Count -gt 0) {
    Write-Output "Image vulnerability gate failed for: $($failed -join ', ')"
    Write-Output "Fix by rebuilding or upgrading the image, or triage into security/trivy/<image>.trivyignore.yaml."
    exit 1
}

Write-Output 'Image vulnerability gate passed for all images in scope.'

# Only here. Every earlier exit path leaves no manifest, so a reader cannot mistake a rejected or
# abandoned run for an approved one.
if (-not [string]::IsNullOrWhiteSpace($ProvenanceOutputPath)) {
    $document = New-ProvenanceDocument -ServiceImages $serviceImages -Scanned $scanned -ScannerImage $TrivyImage
    Write-ProvenanceDocument -Path $ProvenanceOutputPath -Document $document
    Write-Output "Recorded the scanned image IDs at $ProvenanceOutputPath."
}

exit 0
