param(
    [string] $Configuration = 'Debug',
    [switch] $SkipBrowserInstall
)

$ErrorActionPreference = 'Stop'

# E2E tests run on the host, not in the development .NET SDK container.
# Reason 1: docker-compose.dev.yml sets ArtifactsPath to /tmp, so build output lands
#           outside the bind mount and E2ETestFixture cannot resolve the repository root.
# Reason 2: Dockerfile.dev does not ship Playwright browsers or their system packages.
# This script is the single source of truth for the E2E sequence. The e2e-smoke job in
# .github/workflows/ci.yaml invokes it directly, so do not duplicate these steps in CI.

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'tests/WebWritingTool.E2ETests/WebWritingTool.E2ETests.csproj'

if (-not (Test-Path -LiteralPath $project)) {
    throw "E2E test project was not found at $project"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "E2E tests run on the host and require the .NET SDK. Install the SDK version pinned in global.json and retry."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "E2E tests start PostgreSQL with Testcontainers and require Docker. Start Docker and retry."
}

& dotnet restore $project
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet build $project --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not $SkipBrowserInstall) {
    $playwrightScript = Join-Path $repoRoot "tests/WebWritingTool.E2ETests/bin/$Configuration/net10.0/playwright.ps1"
    if (-not (Test-Path -LiteralPath $playwrightScript)) {
        throw "playwright.ps1 was not found at $playwrightScript"
    }

    # Non-Windows hosts need extra system packages to run the browser.
    # Windows PowerShell 5.1 leaves $IsWindows undefined, so treat that case as Windows.
    if ($null -eq $IsWindows -or $IsWindows) {
        $installArguments = @('install', 'chromium')
    }
    else {
        $installArguments = @('install', '--with-deps', 'chromium')
    }

    & $playwrightScript @installArguments
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

# E2ETestFixture launches the web app with "dotnet run --no-build" and reads this variable
# to pick the configuration. Without it the fixture always falls back to Debug, which would
# run a Release test suite against a Debug (or missing) application build.
$previousConfiguration = $env:E2E_DOTNET_CONFIGURATION
$env:E2E_DOTNET_CONFIGURATION = $Configuration

try {
    # Write the trx log under test-results/e2e so CI can upload traces, videos and the
    # test log as a single artifact directory. test-results/ is git-ignored.
    $trxDirectory = Join-Path $repoRoot 'test-results/e2e/trx'

    # Do not pass --artifacts-path here. Build output outside the repository breaks
    # E2ETestFixture repository root resolution and every E2E test fails during fixture setup.
    & dotnet test $project `
        --configuration $Configuration `
        --no-build `
        --logger "trx;LogFileName=e2e.trx" `
        --results-directory $trxDirectory
    $testExitCode = $LASTEXITCODE
}
finally {
    if ($null -eq $previousConfiguration) {
        if (Test-Path Env:\E2E_DOTNET_CONFIGURATION) {
            Remove-Item Env:\E2E_DOTNET_CONFIGURATION
        }
    }
    else {
        $env:E2E_DOTNET_CONFIGURATION = $previousConfiguration
    }
}

exit $testExitCode
