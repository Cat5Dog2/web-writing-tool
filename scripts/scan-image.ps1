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
    [string] $TrivyImage = 'aquasec/trivy:0.74.0',
    [string] $TrivyCacheVolume = 'web-writing-tool-trivy-cache'
)

$ErrorActionPreference = 'Stop'

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

if ($SelfTest) {
    Invoke-SelfTest
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

# "powershell -File" passes every argument as a string, so -ComposeFile a,b arrives as one string
# rather than an array. Split here so the documented -File invocation and a direct pwsh call with a
# real array both work.
$ComposeFile = @($ComposeFile | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

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
foreach ($service in $config.services.PSObject.Properties) {
    $image = $service.Value.image
    if ([string]::IsNullOrWhiteSpace($image)) {
        continue
    }

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

$failed = New-Object System.Collections.Generic.List[string]
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

    # A locally built image that was never built yields an opaque Trivy failure, so say what to do.
    & docker image inspect $image *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "$image is not present locally. Run this script with -Build, or build it first."
    }

    $repository = $image.Split(':')[0]
    $ignoreName = ($repository.Split('/') | Select-Object -Last 1) + '.trivyignore.yaml'
    $ignorePath = Join-Path $ignoreDirectory $ignoreName

    $trivyArguments = @(
        'run',
        '--rm',
        '-v',
        '/var/run/docker.sock:/var/run/docker.sock',
        '-v',
        "$($TrivyCacheVolume):/root/.cache/trivy",
        '-v',
        "$($ignoreDirectory):/ignore:ro",
        $TrivyImage,
        'image',
        '--scanners',
        'vuln'
    )

    Write-Output ''
    Write-Output "=== Vulnerability report for $image ==="
    & docker @trivyArguments '--severity' 'LOW,MEDIUM,HIGH,CRITICAL' '--format' 'table' $image
    if ($LASTEXITCODE -ne 0) {
        throw "Trivy failed to scan $image with exit code $LASTEXITCODE."
    }

    $gateArguments = $trivyArguments + @('--severity', 'HIGH,CRITICAL', '--exit-code', '1')
    if (Test-Path -LiteralPath $ignorePath) {
        Write-Output "Applying accepted risks from security/trivy/$ignoreName"
        $gateArguments += @('--ignorefile', "/ignore/$ignoreName")
    }

    Write-Output "=== Gating HIGH/CRITICAL for $image ==="
    & docker @gateArguments $image
    if ($LASTEXITCODE -ne 0) {
        $failed.Add($image)
    }
}

Write-Output ''
if ($failed.Count -gt 0) {
    Write-Output "Image vulnerability gate failed for: $($failed -join ', ')"
    Write-Output "Fix by rebuilding or upgrading the image, or triage into security/trivy/<image>.trivyignore.yaml."
    exit 1
}

Write-Output 'Image vulnerability gate passed for all images in scope.'
exit 0
