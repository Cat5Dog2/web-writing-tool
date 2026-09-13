# Runs in the "gate" job of notify-release-candidate.yaml, before any step that touches
# CD_APP_PRIVATE_KEY or sends anything to the infra repository. Reads the workflow_run event fields
# and the CD_ENABLED repository variable from the environment and writes should_notify to
# GITHUB_OUTPUT. The decision itself lives in release-candidate-notify-lib.ps1 so
# scripts/test-release-candidate-notify.ps1 exercises the exact function this script calls.
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-candidate-notify-lib.ps1')

$decision = Test-ShouldNotifyReleaseCandidate `
    -CdEnabled ([string] $env:CD_ENABLED) `
    -Conclusion ([string] $env:WORKFLOW_RUN_CONCLUSION) `
    -TriggerEvent ([string] $env:WORKFLOW_RUN_EVENT) `
    -HeadBranch ([string] $env:WORKFLOW_RUN_HEAD_BRANCH) `
    -HeadRepositoryFullName ([string] $env:WORKFLOW_RUN_HEAD_REPOSITORY) `
    -BaseRepositoryFullName ([string] $env:BASE_REPOSITORY)

Write-Output "should_notify=$($decision.ShouldNotify) reason=$($decision.Reason)"

if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    throw 'GITHUB_OUTPUT is not set; run this script as a GitHub Actions step.'
}

$value = if ($decision.ShouldNotify) { 'true' } else { 'false' }
Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "should_notify=$value"
