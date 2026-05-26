param(
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'WebWritingTool.slnx'
$solution = 'WebWritingTool.slnx'

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file was not found at $solutionPath"
}

$arguments = @(
    'build',
    $solution,
    '--configuration',
    $Configuration,
    '--artifacts-path',
    '/tmp/web-writing-tool-build-artifacts'
)

& (Join-Path $PSScriptRoot 'dotnet.ps1') @arguments
exit $LASTEXITCODE
