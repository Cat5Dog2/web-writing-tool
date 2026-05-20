$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.dev.yml'
$envFile = Join-Path $repoRoot '.env'

if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "docker-compose.dev.yml was not found at $composeFile"
}

$composeOptions = @('compose')
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

$arguments = $composeOptions + @(
    '-f',
    $composeFile
) + $runOptions + @(
    'dotnet-sdk'
) + $args

& docker @arguments
exit $LASTEXITCODE
