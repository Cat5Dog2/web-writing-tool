param(
    [string] $Configuration = 'Debug'
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

$composeArgs = @('compose', '--env-file', $envFile, '-f', $composeFile)

& docker @composeArgs up -d postgres
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$migrationCommand = @"
set -eu
dotnet restore WebWritingTool.slnx
dotnet build WebWritingTool.slnx --configuration $Configuration --no-restore
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/WebWritingTool.Infrastructure/WebWritingTool.Infrastructure.csproj --startup-project src/WebWritingTool.Web/WebWritingTool.Web.csproj --context ApplicationDbContext
"@
$migrationCommand = $migrationCommand -replace "`r", ''

& docker @composeArgs run --rm --build --entrypoint /bin/sh dotnet-sdk -lc $migrationCommand
exit $LASTEXITCODE
