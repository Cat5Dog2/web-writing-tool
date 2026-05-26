param(
    [string] $Configuration = 'Debug',
    [switch] $IncludeE2E
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'WebWritingTool.slnx'
$solution = 'WebWritingTool.slnx'

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file was not found at $solutionPath"
}

$arguments = @(
    'test',
    $solution,
    '--configuration',
    $Configuration,
    '--artifacts-path',
    '/tmp/web-writing-tool-test-artifacts'
)

if (-not $IncludeE2E) {
    $arguments += @('--filter', 'Category!=E2E')
}

& (Join-Path $PSScriptRoot 'dotnet.ps1') @arguments
exit $LASTEXITCODE
