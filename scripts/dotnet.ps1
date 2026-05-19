$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repoRoot 'docker-compose.dev.yml'

if (-not (Test-Path -LiteralPath $composeFile)) {
    throw "docker-compose.dev.yml was not found at $composeFile"
}

$runOptions = @('run', '--rm', '--build')
if ($args.Count -gt 0 -and $args[0] -eq 'run') {
    $runOptions += '--service-ports'
}

$arguments = @(
    'compose',
    '-f',
    $composeFile
) + $runOptions + @(
    'dotnet-sdk'
) + $args

& docker @arguments
exit $LASTEXITCODE
