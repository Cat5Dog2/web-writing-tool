param(
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

# Runs the NFT-PERF cases in docs/test-design.md. They are excluded from scripts/test.ps1
# because they seed thousands of rows and assert wall-clock budgets, which only makes sense
# on the nightly schedule. Per docs/ci-cd-design.md a budget miss is a degradation signal,
# not a release stop condition, so the nightly job runs this with continue-on-error.
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
    '/tmp/web-writing-tool-test-artifacts',
    '--filter',
    'Category=Performance',
    '--logger',
    'console;verbosity=detailed'
)

& (Join-Path $PSScriptRoot 'dotnet.ps1') @arguments
exit $LASTEXITCODE
