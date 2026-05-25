$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.dev.yml'
$envFile = Join-Path $repoRoot '.env'

if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "docker-compose.dev.yml was not found at $composeFile"
}

$temporaryDockerConfig = $null
if (-not $env:DOCKER_CONFIG) {
    $temporaryDockerConfig = Join-Path ([System.IO.Path]::GetTempPath()) 'web-writing-tool-docker-config'
    New-Item -ItemType Directory -Path $temporaryDockerConfig -Force | Out-Null
    $env:DOCKER_CONFIG = $temporaryDockerConfig
}

$composeExecutable = $null
$composePrefix = @()

if (Get-Command docker -ErrorAction SilentlyContinue) {
    & docker compose version *> $null
    if ($LASTEXITCODE -eq 0) {
        $composeExecutable = 'docker'
        $composePrefix = @('compose')
    }
}

if (-not $composeExecutable -and (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
    $composeExecutable = 'docker-compose'
}

if (-not $composeExecutable) {
    throw "Docker Compose was not found. Install the 'docker compose' plugin or the 'docker-compose' command."
}

$composeOptions = @()
if (Test-Path -LiteralPath $envFile) {
    $composeOptions += @('--env-file', $envFile)
}
elseif (-not $env:POSTGRES_PASSWORD) {
    $env:POSTGRES_PASSWORD = 'local-dev-password'
}

$runOptions = @('run', '--rm', '--build')
if ($args.Count -gt 0 -and $args[0] -eq 'run') {
    $runOptions += '--service-ports'
}

$arguments = $composePrefix + $composeOptions + @(
    '-f',
    $composeFile
) + $runOptions + @(
    'dotnet-sdk'
) + $args

& $composeExecutable @arguments
exit $LASTEXITCODE
