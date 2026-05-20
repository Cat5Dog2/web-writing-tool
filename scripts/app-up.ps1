param(
    [switch] $Detached,
    [switch] $SkipMigration,
    [switch] $SkipClean
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.dev.yml'
$envFile = Join-Path $repoRoot '.env'

if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "docker-compose.dev.yml was not found at $composeFile"
}

if (-not (Test-Path -LiteralPath $envFile)) {
    throw ".env was not found. Copy .env.example to .env and set local secret values."
}

$baseComposeArgs = @(
    'compose',
    '--env-file',
    $envFile,
    '-f',
    $composeFile
)

if (-not $SkipClean) {
    $cleanCommand = "dotnet clean src/WebWritingTool.Web/WebWritingTool.Web.csproj"
    & docker @baseComposeArgs run --rm --no-deps --build --entrypoint /bin/sh dotnet-sdk -lc $cleanCommand
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if (-not $SkipMigration) {
    & (Join-Path $PSScriptRoot 'db-migrate.ps1')
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

$composeArgs = $baseComposeArgs + @(
    'up',
    '--build'
)

if ($Detached) {
    $composeArgs += '-d'
}

& docker @composeArgs
exit $LASTEXITCODE
