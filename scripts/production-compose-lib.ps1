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

    # migrate stays outside the manifest, but no longer outside the gate. Its image is a digest
    # written into the Compose file rather than a variable, so there is nothing here to pin: the
    # reference cannot move between the scan and the run, and putting it in the manifest would only
    # make the manifest disagree with the scope checked below. It is gated by its own scan, which
    # the deployment procedure runs before it touches the database:
    #
    #   scan-image.ps1 -ComposeProfile tools -ServiceName migrate
    #
    # A new HIGH in that image therefore stops the next deployment instead of only being reported.
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
