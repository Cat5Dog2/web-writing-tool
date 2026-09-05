# Regression tests for the production image-pinning boundary. These tests need no Docker daemon.

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'production-compose-lib.ps1')

function Assert-Test {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw "Production compose test failed: $Message"
    }
}

function Assert-Rejected {
    param([string[]] $Arguments, [string] $ExpectedMessage)

    $message = $null
    try {
        Get-ProductionComposeCommandInfo -Arguments $Arguments | Out-Null
    }
    catch {
        $message = $_.Exception.Message
    }

    Assert-Test (-not [string]::IsNullOrWhiteSpace($message)) "the unsafe command '$($Arguments -join ' ')' was accepted."
    Assert-Test ($message -like "*$ExpectedMessage*") "the rejection for '$($Arguments -join ' ')' did not explain the cause: $message"
}

function Assert-UpSelectionRejected {
    param([string[]] $Arguments, [string] $ExpectedMessage)

    $message = $null
    try {
        Get-ExpectedUpServices -Arguments $Arguments -ComposeServices @('postgres', 'app', 'caddy') | Out-Null
    }
    catch {
        $message = $_.Exception.Message
    }

    Assert-Test (-not [string]::IsNullOrWhiteSpace($message)) "the ambiguous up command '$($Arguments -join ' ')' was accepted."
    Assert-Test ($message -like "*$ExpectedMessage*") "the up rejection for '$($Arguments -join ' ')' did not explain the cause: $message"
}

$up = Get-ProductionComposeCommandInfo -Arguments @('up', '-d', '--no-build', 'app')
Assert-Test ($up.Command -ceq 'up') 'a normal up command was not recognized.'
Assert-Test (-not $up.IsToolsMigration) 'a normal up command was classified as the migration exception.'

$migration = Get-ProductionComposeCommandInfo -Arguments @('--profile', 'tools', 'run', '--rm', 'migrate')
Assert-Test ($migration.Command -ceq 'run' -and $migration.IsToolsMigration) 'the one documented migration command was rejected.'

Assert-Rejected -Arguments @('--profile', 'bundled-caddy', 'up', '-d', 'caddy') -ExpectedMessage 'only supported profile command'
Assert-Rejected -Arguments @('-f', 'other.yml', 'up', '-d', 'app') -ExpectedMessage 'may not contain the global option'
Assert-Rejected -Arguments @('--project-name=other', 'up', '-d', 'app') -ExpectedMessage 'may not contain the global option'
Assert-Rejected -Arguments @('up', '-d', '--build', 'app') -ExpectedMessage 'can change image contents'
Assert-Rejected -Arguments @('up', '-d', '--build=false', 'app') -ExpectedMessage 'can change image contents'
Assert-Rejected -Arguments @('pull') -ExpectedMessage 'can change image contents'
Assert-UpSelectionRejected -Arguments @('up', '--attach', 'app') -ExpectedMessage 'not in the reviewed production set'
Assert-UpSelectionRejected -Arguments @('up', '-d', 'app') -ExpectedMessage 'without --no-build'

$composeServices = @('postgres', 'app', 'caddy')
$selected = @(Get-ExpectedUpServices -Arguments @('up', '-d', '--no-build', 'app') -ComposeServices $composeServices)
Assert-Test ($selected.Count -eq 1 -and $selected[0] -ceq 'app') 'an explicit app update did not require app itself to be verified.'

$selected = @(Get-ExpectedUpServices -Arguments @('up', '-d', '--no-build') -ComposeServices $composeServices)
Assert-Test ($selected.Count -eq 3) 'an up without service names did not require every active service.'

$appImage = 'sha256:' + ('a' * 64)
$postgresImage = 'sha256:' + ('b' * 64)
$pinned = [ordered]@{ app = $appImage; postgres = $postgresImage }

$missingAppCapture = {
    param([string[]] $Arguments)

    if ($Arguments[0] -ceq 'compose' -and $Arguments[-1] -ceq 'app') {
        return [pscustomobject]@{ ExitCode = 0; Output = '' }
    }

    if ($Arguments[0] -ceq 'compose' -and $Arguments[-1] -ceq 'postgres') {
        return [pscustomobject]@{ ExitCode = 0; Output = 'postgres-container' }
    }

    return [pscustomobject]@{ ExitCode = 0; Output = $postgresImage }
}.GetNewClosure()

$missingRejected = $false
try {
    # postgres is available and approved, but it must not satisfy an up that selected app.
    Assert-ExpectedContainerImages -Services @('app') -Pinned $pinned -ComposeOptions @('-f', 'compose.yml') -DockerCapturer $missingAppCapture
}
catch {
    $missingRejected = $_.Exception.Message -like "*Service 'app'*no running container*"
}
Assert-Test $missingRejected 'a running unrelated service satisfied verification for a missing app container.'

$matchingCapture = {
    param([string[]] $Arguments)

    if ($Arguments[0] -ceq 'compose') {
        return [pscustomobject]@{ ExitCode = 0; Output = "$($Arguments[-1])-container" }
    }

    if ($Arguments[-1] -ceq 'app-container') {
        return [pscustomobject]@{ ExitCode = 0; Output = $appImage }
    }

    return [pscustomobject]@{ ExitCode = 0; Output = $postgresImage }
}.GetNewClosure()
Assert-ExpectedContainerImages -Services @('app', 'postgres') -Pinned $pinned -ComposeOptions @('-f', 'compose.yml') -DockerCapturer $matchingCapture | Out-Null

# Exercise the top-level scanner ordering rather than only Clear-ProvenanceOutput. The child fails
# during resource validation, before Docker is needed, and must still invalidate the stale file.
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("production-compose-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace | Out-Null
try {
    $staleManifest = Join-Path $workspace 'scanned-images.json'
    Set-Content -LiteralPath $staleManifest -Value 'stale approval'

    if ($PSVersionTable.PSEdition -eq 'Desktop') {
        $powerShellExecutable = Join-Path $PSHOME 'powershell.exe'
    }
    else {
        $powerShellExecutable = Join-Path $PSHOME 'pwsh'
    }

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $powerShellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'scan-image.ps1') -ProvenanceOutputPath $staleManifest -ScanMemoryLimit 0 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    Assert-Test ($exitCode -ne 0) 'the deliberately invalid scanner invocation unexpectedly succeeded.'
    Assert-Test (-not (Test-Path -LiteralPath $staleManifest)) "an early scanner failure left the stale manifest behind. Output: $($output -join ' ')"
}
finally {
    if (Test-Path -LiteralPath $workspace) {
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }
}

Write-Output 'Production compose regression tests passed.'
