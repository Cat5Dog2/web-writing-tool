# Runs in the "notify" job of notify-release-candidate.yaml, before the GitHub App token is
# minted. Splits the INFRA_REPOSITORY repository variable into owner and repo outputs so the
# token-minting step can scope the installation token to that one repository only.
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-candidate-notify-lib.ps1')

$infraRepository = [string] $env:INFRA_REPOSITORY
if ([string]::IsNullOrWhiteSpace($infraRepository)) {
    throw 'INFRA_REPOSITORY is not set. Configure the INFRA_REPOSITORY repository variable before enabling CD_ENABLED.'
}

$parts = Get-InfraRepositoryParts -InfraRepository $infraRepository

if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    throw 'GITHUB_OUTPUT is not set; run this script as a GitHub Actions step.'
}

Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "owner=$($parts.Owner)"
Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "repo=$($parts.Repo)"
