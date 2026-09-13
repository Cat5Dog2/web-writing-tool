# Runs first in the "notify" job of notify-release-candidate.yaml, before any step that touches
# CD_APP_PRIVATE_KEY or the infra repository. Re-derives the same should-notify decision the
# "gate" job already made, using the live vars.CD_ENABLED and workflow_run event fields at the
# time this job actually executes, and fails the job if the answer is no longer "notify".
#
# This closes a gap that a cached job output cannot: GitHub Actions "Re-run failed jobs" does not
# re-run a job that already succeeded, so once "gate" has produced should_notify=true, that output
# stays cached at "true" even if CD_ENABLED (or another condition) changes afterward. Without this
# re-check, disabling CD_ENABLED after a failed notify attempt would not stop a later "Re-run
# failed jobs" on notify from minting a token and sending the dispatch anyway. See
# docs/ci-cd-design.md 23.2 for the full reasoning.
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-candidate-notify-lib.ps1')

$decision = Test-ShouldNotifyReleaseCandidate `
    -CdEnabled ([string] $env:CD_ENABLED) `
    -Conclusion ([string] $env:WORKFLOW_RUN_CONCLUSION) `
    -TriggerEvent ([string] $env:WORKFLOW_RUN_EVENT) `
    -HeadBranch ([string] $env:WORKFLOW_RUN_HEAD_BRANCH) `
    -HeadRepositoryFullName ([string] $env:WORKFLOW_RUN_HEAD_REPOSITORY) `
    -BaseRepositoryFullName ([string] $env:BASE_REPOSITORY)

Write-Output "Re-check at notify time: should_notify=$($decision.ShouldNotify) reason=$($decision.Reason)"

if (-not $decision.ShouldNotify) {
    throw "Refusing to notify infra: $($decision.Reason) This can happen if CD_ENABLED or another condition changed after the gate job's decision was cached, for example by disabling CD_ENABLED and then using 'Re-run failed jobs' on a previously failed notify job."
}
