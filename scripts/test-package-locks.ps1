# Regression tests for the package lock policy. The fixtures are temporary Git repositories so
# the tests cover tracked, deleted, modified and untracked files through Git itself.
$ErrorActionPreference = 'Stop'

$checker = Join-Path $PSScriptRoot 'verify-package-locks.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "web-writing-tool-package-lock-tests-$PID-$([guid]::NewGuid().ToString('N'))"

function Write-AsciiFixture {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Content
    )

    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.Encoding]::ASCII)
}

function Invoke-TestGit {
    param(
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    & git -C $Repository @Arguments *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE in $Repository."
    }
}

function New-TestRepository {
    param([Parameter(Mandatory)] [string] $Name)

    $repository = Join-Path $testRoot $Name
    New-Item -ItemType Directory -Path $repository -Force | Out-Null
    Invoke-TestGit -Repository $repository -Arguments @('init', '--quiet')
    Invoke-TestGit -Repository $repository -Arguments @('config', 'user.name', 'Package Lock Test')
    Invoke-TestGit -Repository $repository -Arguments @('config', 'user.email', 'package-lock-test@example.invalid')
    return $repository
}

function Add-TestProject {
    param(
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $ProjectDirectory,
        [switch] $IncludeLock
    )

    $projectName = Split-Path -Leaf $ProjectDirectory
    $directory = Join-Path $Repository $ProjectDirectory
    Write-AsciiFixture -Path (Join-Path $directory "$projectName.csproj") -Content '<Project Sdk="Microsoft.NET.Sdk" />'
    if ($IncludeLock) {
        Write-AsciiFixture -Path (Join-Path $directory 'packages.lock.json') -Content '{"version":1,"dependencies":{}}'
    }
}

function Commit-TestRepository {
    param(
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $Message
    )

    Invoke-TestGit -Repository $Repository -Arguments @('add', '.')
    Invoke-TestGit -Repository $Repository -Arguments @('commit', '--quiet', '-m', $Message)
}

function Assert-PolicyFails {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [ValidateSet('BeforeRestore', 'AfterRestore')] [string] $Phase,
        [Parameter(Mandatory)] [string] $ExpectedMessage
    )

    try {
        & $checker -RepositoryRoot $Repository -Phase $Phase *> $null
    }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") {
            throw "$Name failed for the wrong reason: $($_.Exception.Message)"
        }
        Write-Output "PASS: $Name"
        return
    }

    throw "$Name did not fail."
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    $valid = New-TestRepository -Name 'valid'
    Add-TestProject -Repository $valid -ProjectDirectory 'src/Existing' -IncludeLock
    Commit-TestRepository -Repository $valid -Message 'valid fixture'
    & $checker -RepositoryRoot $valid -Phase BeforeRestore *> $null
    & $checker -RepositoryRoot $valid -Phase AfterRestore *> $null
    Write-Output 'PASS: tracked lock passes both phases'

    $deleted = New-TestRepository -Name 'deleted-lock'
    Add-TestProject -Repository $deleted -ProjectDirectory 'src/Existing' -IncludeLock
    Commit-TestRepository -Repository $deleted -Message 'add tracked lock'
    Invoke-TestGit -Repository $deleted -Arguments @('rm', '--quiet', 'src/Existing/packages.lock.json')
    Commit-TestRepository -Repository $deleted -Message 'remove tracked lock'
    # Simulate an earlier implicit restore recreating the deleted lock as an untracked file. The
    # policy must check Git tracking, not just filesystem existence, or this hides the deletion.
    Write-AsciiFixture -Path (Join-Path $deleted 'src/Existing/packages.lock.json') -Content '{"version":1,"dependencies":{}}'
    Assert-PolicyFails -Name 'regenerated deleted lock is rejected before restore' -Repository $deleted -Phase BeforeRestore -ExpectedMessage 'src/Existing/packages.lock.json is not tracked'

    $newProject = New-TestRepository -Name 'new-project'
    Add-TestProject -Repository $newProject -ProjectDirectory 'src/Existing' -IncludeLock
    Commit-TestRepository -Repository $newProject -Message 'add existing project'
    Add-TestProject -Repository $newProject -ProjectDirectory 'src/NewProject'
    Commit-TestRepository -Repository $newProject -Message 'add project without lock'
    Assert-PolicyFails -Name 'new project without lock is rejected' -Repository $newProject -Phase BeforeRestore -ExpectedMessage 'src/NewProject/packages.lock.json is not tracked'

    $untracked = New-TestRepository -Name 'untracked-lock'
    Add-TestProject -Repository $untracked -ProjectDirectory 'src/Existing' -IncludeLock
    Commit-TestRepository -Repository $untracked -Message 'add valid project'
    Write-AsciiFixture -Path (Join-Path $untracked 'generated/packages.lock.json') -Content '{"version":1,"dependencies":{}}'
    Assert-PolicyFails -Name 'untracked generated lock is rejected after restore' -Repository $untracked -Phase AfterRestore -ExpectedMessage 'working tree differs'

    $modified = New-TestRepository -Name 'modified-lock'
    Add-TestProject -Repository $modified -ProjectDirectory 'src/Existing' -IncludeLock
    Commit-TestRepository -Repository $modified -Message 'add valid project'
    Write-AsciiFixture -Path (Join-Path $modified 'src/Existing/packages.lock.json') -Content '{"version":1,"dependencies":{"net10.0":{}}}'
    Assert-PolicyFails -Name 'modified tracked lock is rejected after restore' -Repository $modified -Phase AfterRestore -ExpectedMessage 'working tree differs'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Output 'Package lock policy tests passed.'
