param(
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'WebWritingTool.slnx'
$solution = 'WebWritingTool.slnx'

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file was not found at $solutionPath"
}

$formatArgs = @('format', $solution, '--verbosity', 'minimal')
if (-not $Apply) {
    $formatArgs += '--verify-no-changes'
}

& (Join-Path $PSScriptRoot 'dotnet.ps1') @formatArgs
exit $LASTEXITCODE
