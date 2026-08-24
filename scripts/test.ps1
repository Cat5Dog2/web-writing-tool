param(
    [string] $Configuration = 'Debug',
    [switch] $IncludeE2E,
    [switch] $IncludePerformance
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'WebWritingTool.slnx'
$solution = 'WebWritingTool.slnx'

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file was not found at $solutionPath"
}

# Always exclude E2E from the container run. E2E needs Playwright browsers and build
# output inside the repository, neither of which the development .NET SDK container
# provides. See scripts/test-e2e.ps1 for details.
# Performance is excluded as well. It seeds thousands of rows and asserts wall-clock
# budgets, so it belongs to the nightly run. See scripts/test-performance.ps1.
$arguments = @(
    'test',
    $solution,
    '--configuration',
    $Configuration,
    '--artifacts-path',
    '/tmp/web-writing-tool-test-artifacts',
    '--filter',
    'Category!=E2E&Category!=Performance'
)

& (Join-Path $PSScriptRoot 'dotnet.ps1') @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($IncludePerformance) {
    & (Join-Path $PSScriptRoot 'test-performance.ps1') -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($IncludeE2E) {
    # E2E runs on the host.
    & (Join-Path $PSScriptRoot 'test-e2e.ps1') -Configuration $Configuration
}

exit $LASTEXITCODE
