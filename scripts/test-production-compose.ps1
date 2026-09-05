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

function Copy-TestObject {
    param([Parameter(Mandatory)] $Value)

    return (($Value | ConvertTo-Json -Depth 8) | ConvertFrom-Json)
}

function Assert-MigrationReceiptRejected {
    param(
        [Parameter(Mandatory)] $Receipt,
        [Parameter(Mandatory)] [string] $ExpectedReference,
        [Parameter(Mandatory)] [datetime] $Now,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    $message = $null
    try {
        Get-ValidatedMigrationReceipt -Receipt $Receipt -ExpectedReference $ExpectedReference -Now $Now | Out-Null
    }
    catch {
        $message = $_.Exception.Message
    }

    Assert-Test (-not [string]::IsNullOrWhiteSpace($message)) 'an invalid migration scan receipt was accepted.'
    Assert-Test ($message -like "*$ExpectedMessage*") "the migration receipt rejection did not explain the cause: $message"
}

$up = Get-ProductionComposeCommandInfo -Arguments @('up', '-d', '--no-build', 'app')
Assert-Test ($up.Command -ceq 'up') 'a normal up command was not recognized.'
Assert-Test (-not $up.IsToolsMigration) 'a normal up command was classified as the migration exception.'

$migration = Get-ProductionComposeCommandInfo -Arguments @('--profile', 'tools', 'run', '--rm', 'migrate')
Assert-Test ($migration.Command -ceq 'run' -and $migration.IsToolsMigration) 'the one documented migration command was rejected.'

$receiptNow = [datetime]::SpecifyKind([datetime]::Parse('2026-09-05T12:00:00Z'), [DateTimeKind]::Utc)
$migrationReference = 'registry.example:5000/team/sdk@sha256:' + ('d' * 64)
$migrationImageId = 'sha256:' + ('e' * 64)
$validMigrationReceipt = [pscustomobject]@{
    schemaVersion                  = 1
    gateResult                    = 'passed'
    generatedAt                   = '2026-09-05T11:55:00Z'
    scannerImage                  = 'aquasec/trivy@sha256:' + ('f' * 64)
    scannerVersion                = '0.74.0'
    vulnerabilityDatabaseUpdatedAt = '2026-09-05T10:00:00Z'
    services                      = [pscustomobject]@{
        migrate = [pscustomobject]@{
            reference = $migrationReference
            imageId   = $migrationImageId
        }
    }
}

$validatedReceipt = Get-ValidatedMigrationReceipt -Receipt $validMigrationReceipt -ExpectedReference $migrationReference -Now $receiptNow
Assert-Test ($validatedReceipt.ImageId -ceq $migrationImageId) 'a valid migration scan receipt returned the wrong image ID.'

$dateObjectReceipt = Copy-TestObject -Value $validMigrationReceipt
$dateObjectReceipt.generatedAt = [datetime]::SpecifyKind([datetime]::Parse('2026-09-05T11:55:00Z'), [DateTimeKind]::Utc)
$dateObjectReceipt.vulnerabilityDatabaseUpdatedAt = [datetimeoffset]::Parse('2026-09-05T10:00:00Z')
$validatedDateObjectReceipt = Get-ValidatedMigrationReceipt -Receipt $dateObjectReceipt -ExpectedReference $migrationReference -Now $receiptNow
Assert-Test ($validatedDateObjectReceipt.ImageId -ceq $migrationImageId) 'PowerShell 7-style DateTime receipt fields were rejected.'

$expiredReceipt = Copy-TestObject -Value $validMigrationReceipt
$expiredReceipt.generatedAt = '2026-09-04T11:59:59Z'
Assert-MigrationReceiptRejected -Receipt $expiredReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'older than'

$wrongReferenceReceipt = Copy-TestObject -Value $validMigrationReceipt
$wrongReferenceReceipt.services.migrate.reference = 'registry.example:5000/team/sdk@sha256:' + ('a' * 64)
Assert-MigrationReceiptRejected -Receipt $wrongReferenceReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'does not match'

$missingMetadataReceipt = Copy-TestObject -Value $validMigrationReceipt
$missingMetadataReceipt.scannerVersion = ''
Assert-MigrationReceiptRejected -Receipt $missingMetadataReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'scannerVersion'

$unpinnedScannerReceipt = Copy-TestObject -Value $validMigrationReceipt
$unpinnedScannerReceipt.scannerImage = 'aquasec/trivy:0.74.0'
Assert-MigrationReceiptRejected -Receipt $unpinnedScannerReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'not pinned by digest'

$staleDatabaseReceipt = Copy-TestObject -Value $validMigrationReceipt
$staleDatabaseReceipt.vulnerabilityDatabaseUpdatedAt = '2026-09-03T11:59:59Z'
Assert-MigrationReceiptRejected -Receipt $staleDatabaseReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'database older than'

$extraServiceReceipt = Copy-TestObject -Value $validMigrationReceipt
$extraServiceReceipt.services | Add-Member -NotePropertyName app -NotePropertyValue ([pscustomobject]@{ reference = 'app:local'; imageId = 'sha256:' + ('b' * 64) })
Assert-MigrationReceiptRejected -Receipt $extraServiceReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'exactly the migrate service'

$badIdReceipt = Copy-TestObject -Value $validMigrationReceipt
$badIdReceipt.services.migrate.imageId = 'registry.example:5000/team/sdk:latest'
Assert-MigrationReceiptRejected -Receipt $badIdReceipt -ExpectedReference $migrationReference -Now $receiptNow -ExpectedMessage 'sha256 image ID'

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

    $staleReceipt = Join-Path $workspace 'scanned-migrate.json'
    Set-Content -LiteralPath $staleReceipt -Value 'stale migration approval'

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $powerShellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'scan-image.ps1') -ScanReceiptOutputPath $staleReceipt -ServiceName migrate -ScanMemoryLimit 0 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    Assert-Test ($exitCode -ne 0) 'the deliberately invalid migration scanner invocation unexpectedly succeeded.'
    Assert-Test (-not (Test-Path -LiteralPath $staleReceipt)) "an early scanner failure left the stale migration receipt behind. Output: $($output -join ' ')"
}
finally {
    if (Test-Path -LiteralPath $workspace) {
        Remove-Item -LiteralPath $workspace -Recurse -Force
    }
}

Write-Output 'Production compose regression tests passed.'
