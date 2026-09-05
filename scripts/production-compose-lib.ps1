# Shared, side-effect-free policy and verification helpers for production-compose.ps1.

function Test-ExactArguments {
    param([string[]] $Actual, [string[]] $Expected)

    $difference = @(Compare-Object -ReferenceObject $Expected -DifferenceObject $Actual -SyncWindow 0 -CaseSensitive)
    return ($difference.Count -eq 0)
}

function Get-ProductionComposeCommandInfo {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    if ($Arguments.Count -eq 0) {
        throw "No compose command was given. For example: -ComposeCommand 'up -d --no-build app'"
    }

    # migrate stays outside the long-running-service manifest, but no longer outside the enforced
    # gate. A digest pin prevents substitution; a separate, short-lived receipt proves that this
    # exact digest and local image ID passed the vulnerability gate immediately before use:
    #
    #   scan-image.ps1 -ComposeProfile tools -ServiceName migrate \
    #     -ScanReceiptOutputPath artifacts/scanned-migrate.json
    #
    # production-compose.ps1 validates and atomically consumes the receipt before it runs this
    # command, so a successful scan cannot be skipped or silently reused for a later deployment.
    # Keep the exception to one exact command; never let a caller turn either profile into an `up`
    # that adds an unapproved service after scope checking.
    $migrationArguments = @('--profile', 'tools', 'run', '--rm', 'migrate')
    if (Test-ExactArguments -Actual $Arguments -Expected $migrationArguments) {
        return [pscustomobject]@{ Command = 'run'; IsToolsMigration = $true }
    }

    # Compose global options change the project or active services. They cannot live in
    # ComposeCommand because manifest scope is established from the dedicated parameters.
    $scopeOptions = @('--profile', '-f', '--file', '-p', '--project-name', '--project-directory', '--env-file')
    foreach ($argument in $Arguments) {
        foreach ($option in $scopeOptions) {
            $joinedShortOption = ($option -ceq '-f' -or $option -ceq '-p') -and
                $argument.StartsWith($option, [System.StringComparison]::Ordinal) -and $argument.Length -gt 2
            if ($argument -ceq $option -or $argument.StartsWith("$option=", [System.StringComparison]::Ordinal) -or $joinedShortOption) {
                throw "ComposeCommand may not contain the global option '$argument'. Use -ComposeFile or -EnvFile so validation and execution use the same scope. The only supported profile command is '--profile tools run --rm migrate'."
            }
        }
    }

    if ($Arguments[0].StartsWith('-', [System.StringComparison]::Ordinal)) {
        throw "ComposeCommand must begin with a Compose command, not the global option '$($Arguments[0])'. Use the wrapper parameters instead."
    }

    $command = $Arguments[0]
    if ($command -cin @('build', 'pull')) {
        throw "ComposeCommand '$command' can change image contents after the vulnerability gate. Build and pull before scanning instead."
    }

    foreach ($argument in $Arguments) {
        if ($argument -ceq '--build' -or $argument.StartsWith('--build=', [System.StringComparison]::Ordinal) -or
            $argument -ceq '--pull' -or $argument.StartsWith('--pull=', [System.StringComparison]::Ordinal)) {
            throw "ComposeCommand may not use '$argument' because it can change image contents after the vulnerability gate."
        }
    }

    return [pscustomobject]@{ Command = $command; IsToolsMigration = $false }
}

function Test-ImageIdFormat {
    param([string] $ImageId)

    return ($ImageId -cmatch '^sha256:[0-9a-f]{64}$')
}

function Test-DigestPinnedImageReference {
    param([string] $Reference)

    return ($Reference -cmatch '^[^@\s]+@sha256:[0-9a-f]{64}$')
}

function ConvertFrom-ReceiptTimestamp {
    param(
        $Value,
        [Parameter(Mandatory)] [string] $FieldName
    )

    # Windows PowerShell 5.1 leaves JSON timestamps as strings. Current PowerShell 7 converts the
    # same JSON token to DateTime. Accept both representations, but normalize both to UTC before the
    # age checks so the security decision is identical on local Windows and the Linux deployment.
    if ($Value -is [datetimeoffset]) {
        return $Value.UtcDateTime
    }
    if ($Value -is [datetime]) {
        return $Value.ToUniversalTime()
    }

    $text = [string] $Value
    if ($text -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') {
        throw "The migration scan receipt has invalid $FieldName '$text'; expected an RFC3339 UTC timestamp."
    }

    try {
        return [datetimeoffset]::ParseExact($text, 'yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal).UtcDateTime
    }
    catch {
        throw "The migration scan receipt has invalid $FieldName '$text'; expected an RFC3339 UTC timestamp."
    }
}

function Get-ValidatedMigrationReceipt {
    param(
        [Parameter(Mandatory)] $Receipt,
        [Parameter(Mandatory)] [string] $ExpectedReference,
        [datetime] $Now = [datetime]::UtcNow,
        [timespan] $MaximumAge = ([timespan]::FromHours(24)),
        [timespan] $MaximumDatabaseAge = ([timespan]::FromHours(48))
    )

    if ([int] $Receipt.schemaVersion -ne 1) {
        throw "The migration scan receipt has unsupported schemaVersion '$($Receipt.schemaVersion)'."
    }
    if ($Receipt.gateResult -cne 'passed') {
        throw "The migration scan receipt records gateResult '$($Receipt.gateResult)'; only 'passed' may run."
    }
    if (-not (Test-DigestPinnedImageReference -Reference $Receipt.scannerImage)) {
        throw "The migration scan receipt scannerImage '$($Receipt.scannerImage)' is not pinned by digest."
    }
    if ([string]::IsNullOrWhiteSpace($Receipt.scannerVersion)) {
        throw 'The migration scan receipt has no scannerVersion.'
    }

    $utcNow = $Now.ToUniversalTime()
    $generatedAt = ConvertFrom-ReceiptTimestamp -Value $Receipt.generatedAt -FieldName generatedAt
    if ($generatedAt -gt $utcNow.AddMinutes(5)) {
        throw 'The migration scan receipt generatedAt is in the future.'
    }
    if (($utcNow - $generatedAt) -gt $MaximumAge) {
        throw "The migration scan receipt is older than $([int] $MaximumAge.TotalHours) hours. Re-scan immediately before migration."
    }

    $databaseUpdatedAt = ConvertFrom-ReceiptTimestamp -Value $Receipt.vulnerabilityDatabaseUpdatedAt -FieldName vulnerabilityDatabaseUpdatedAt
    if ($databaseUpdatedAt -gt $utcNow.AddMinutes(5)) {
        throw 'The migration scan receipt vulnerabilityDatabaseUpdatedAt is in the future.'
    }
    if (($utcNow - $databaseUpdatedAt) -gt $MaximumDatabaseAge) {
        throw "The migration scan receipt uses a vulnerability database older than $([int] $MaximumDatabaseAge.TotalHours) hours. Refresh and re-scan."
    }

    if (-not (Test-DigestPinnedImageReference -Reference $ExpectedReference)) {
        throw "The migrate service image '$ExpectedReference' is not pinned by digest."
    }
    if ($null -eq $Receipt.services) {
        throw 'The migration scan receipt must name exactly the migrate service.'
    }

    $serviceProperties = @($Receipt.services.PSObject.Properties)
    if ($serviceProperties.Count -ne 1 -or $serviceProperties[0].Name -cne 'migrate') {
        throw 'The migration scan receipt must name exactly the migrate service.'
    }

    $entry = $serviceProperties[0].Value
    if ($entry.reference -cne $ExpectedReference) {
        throw "The migration scan receipt reference '$($entry.reference)' does not match the active Compose reference '$ExpectedReference'."
    }
    if (-not (Test-ImageIdFormat -ImageId $entry.imageId)) {
        throw "The migration scan receipt imageId '$($entry.imageId)' is not a sha256 image ID."
    }

    return [pscustomobject]@{
        Reference = $entry.reference
        ImageId   = $entry.imageId
    }
}

function Get-ExpectedUpServices {
    param(
        [Parameter(Mandatory)] [string[]] $Arguments,
        [Parameter(Mandatory)] [string[]] $ComposeServices,
        [string[]] $BuildableServices = @('app', 'caddy')
    )

    # Parse only the small set used by production procedures. Treating every token equal to a
    # service as a target is unsafe because options can use service names as values without
    # narrowing what up starts.
    $booleanOptions = @(
        '-d',
        '--detach',
        '--no-build',
        '--force-recreate',
        '--no-recreate',
        '--remove-orphans',
        '--renew-anon-volumes',
        '--always-recreate-deps',
        '--no-deps',
        '--quiet-pull',
        '--wait'
    )
    $requested = @()
    foreach ($argument in @($Arguments | Select-Object -Skip 1)) {
        if ($booleanOptions -ccontains $argument) {
            continue
        }

        if ($argument.StartsWith('-', [System.StringComparison]::Ordinal)) {
            throw "The up option '$argument' is not in the reviewed production set."
        }

        if ($ComposeServices -cnotcontains $argument) {
            throw "The up command names '$argument', which is not an active service in the validated Compose scope."
        }

        $requested += $argument
    }

    if ($requested.Count -eq 0) {
        $requested = @($ComposeServices)
    }

    $wouldBuild = @($requested | Where-Object { $BuildableServices -ccontains $_ })
    if ($wouldBuild.Count -gt 0 -and $Arguments -cnotcontains '--no-build') {
        throw "The up command selects buildable service(s) $($wouldBuild -join ', ') without --no-build."
    }

    return $requested
}

function Get-CapturedDockerDetails {
    param([Parameter(Mandatory)] $Result)

    return (@($Result.Output, $Result.ErrorOutput) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
}

function Assert-ExpectedContainerImages {
    param(
        [Parameter(Mandatory)] [string[]] $Services,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Pinned,
        [Parameter(Mandatory)] [string[]] $ComposeOptions,
        [Parameter(Mandatory)] [scriptblock] $DockerCapturer
    )

    foreach ($service in $Services) {
        $idResult = & $DockerCapturer -Arguments (@('compose') + $ComposeOptions + @('ps', '-q', $service))
        if ($idResult.ExitCode -ne 0) {
            throw "docker compose ps -q $service failed with exit code $($idResult.ExitCode).$([Environment]::NewLine)$(Get-CapturedDockerDetails -Result $idResult)"
        }

        $containerIds = @($idResult.Output -split '\r?\n' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        if ($containerIds.Count -eq 0) {
            throw "Service '$service' was selected by the up command but has no running container to verify."
        }

        foreach ($containerId in $containerIds) {
            $imageResult = & $DockerCapturer -Arguments @('inspect', '--format', '{{.Image}}', $containerId)
            if ($imageResult.ExitCode -ne 0) {
                throw "docker inspect failed for $service container $containerId with exit code $($imageResult.ExitCode).$([Environment]::NewLine)$(Get-CapturedDockerDetails -Result $imageResult)"
            }

            # Docker reports lowercase image IDs. A case-insensitive comparison would conceal a
            # malformed result.
            $runningImageId = $imageResult.Output.Trim()
            if ($runningImageId -cne $Pinned[$service]) {
                throw "Service '$service' is running $runningImageId but the manifest approved $($Pinned[$service]). Stop it and redeploy from the scanned artifact."
            }

            Write-Output "Verified $service is running $runningImageId"
        }
    }
}
