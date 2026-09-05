# Enforces the repository policy that every tracked .NET project has a tracked packages.lock.json.
# Run BeforeRestore before any command that can restore, then run AfterRestore after the locked
# restore to catch both tracked changes and newly generated untracked lock files.
param(
    [ValidateSet('BeforeRestore', 'AfterRestore')]
    [string] $Phase = 'BeforeRestore',
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$resolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

function Invoke-GitLines {
    param([Parameter(Mandatory)] [string[]] $Arguments)

    $stderrPath = [System.IO.Path]::GetTempFileName()
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = @(& git -C $resolvedRepositoryRoot @Arguments 2> $stderrPath |
            ForEach-Object { $_.ToString() })
        $exitCode = $LASTEXITCODE
        $stderr = @(Get-Content -LiteralPath $stderrPath -ErrorAction SilentlyContinue |
            ForEach-Object { $_.ToString() })
    }
    finally {
        $ErrorActionPreference = $previousPreference
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }

    if ($exitCode -ne 0) {
        $details = @($output + $stderr | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine
        throw "git $($Arguments -join ' ') failed with exit code $exitCode.$([Environment]::NewLine)$details"
    }

    $output
}

$insideWorkTree = @(Invoke-GitLines -Arguments @('rev-parse', '--is-inside-work-tree'))
if ($insideWorkTree.Count -ne 1 -or $insideWorkTree[0] -cne 'true') {
    throw "$resolvedRepositoryRoot is not a Git work tree."
}

$trackedProjects = @(Invoke-GitLines -Arguments @('ls-files', '--', ':(glob)**/*.csproj'))
if ($trackedProjects.Count -eq 0) {
    throw 'No tracked .csproj files were found.'
}

$trackedLocks = @(Invoke-GitLines -Arguments @('ls-files', '--', ':(glob)**/packages.lock.json'))
$trackedLockSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($trackedLock in $trackedLocks) {
    [void] $trackedLockSet.Add($trackedLock)
}

$failures = New-Object System.Collections.Generic.List[string]
foreach ($project in $trackedProjects) {
    $separator = $project.LastIndexOf('/')
    $expectedLock = if ($separator -ge 0) {
        $project.Substring(0, $separator + 1) + 'packages.lock.json'
    }
    else {
        'packages.lock.json'
    }

    if (-not $trackedLockSet.Contains($expectedLock)) {
        $failures.Add("$expectedLock is not tracked for $project.")
        continue
    }

    $nativePath = $expectedLock.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedRepositoryRoot $nativePath) -PathType Leaf)) {
        $failures.Add("$expectedLock is tracked but missing from the working tree.")
    }
}

if ($Phase -ceq 'AfterRestore') {
    $lockStatus = @(Invoke-GitLines -Arguments @(
            'status',
            '--porcelain=v1',
            '--untracked-files=all',
            '--',
            ':(glob)**/packages.lock.json'
        ))
    if ($lockStatus.Count -gt 0) {
        $failures.Add("The package lock working tree differs after restore: $($lockStatus -join '; ')")
    }
}

foreach ($failure in $failures) {
    Write-Output $failure
}
if ($failures.Count -gt 0) {
    throw "Package lock policy failed for $($failures.Count) reason(s): $($failures -join ' ')"
}

Write-Output "Package lock policy $Phase check passed for $($trackedProjects.Count) tracked project(s)."
