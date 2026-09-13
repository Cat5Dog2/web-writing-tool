# Shared, side-effect-free policy and payload helpers for the release-candidate notification
# workflow (.github/workflows/notify-release-candidate.yaml). Kept separate from the orchestration
# scripts so scripts/test-release-candidate-notify.ps1 can exercise the exact same decision and
# payload logic the workflow runs, without a network call or a GitHub Actions runner.
#
# Contract with the infra repository (must not change without updating the receiver too):
#   destination: repository_dispatch on the repository named by the INFRA_REPOSITORY variable
#   event_type: app-release-candidate-v1
#   client_payload: { component: "wwt", source_sha, source_run_id, source_run_attempt }
# See docs/ci-cd-design.md for the full design and docs/operation-design.md for the runbook.

function Test-ShouldNotifyReleaseCandidate {
    param(
        # AllowEmptyString on every field: an unset repository variable or a workflow_run field
        # GitHub happens not to populate arrives here as an empty string, and that must be handled
        # as "does not qualify", not as a parameter-binding error.
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $CdEnabled,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Conclusion,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $TriggerEvent,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $HeadBranch,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $HeadRepositoryFullName,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $BaseRepositoryFullName
    )

    # Case-sensitive on purpose: only the literal string "true" opts in. This is the one check that
    # must pass before any step touches CD_APP_PRIVATE_KEY or sends anything to the infra
    # repository.
    if ($CdEnabled -cne 'true') {
        return [pscustomobject]@{ ShouldNotify = $false; Reason = "CD_ENABLED is not the string 'true'." }
    }

    # A cancelled, failed, or still-running source CI never reaches conclusion 'success', so this
    # also covers cancellation without a separate check.
    if ($Conclusion -ne 'success') {
        return [pscustomobject]@{ ShouldNotify = $false; Reason = "workflow_run.conclusion is '$Conclusion', not 'success'." }
    }

    # Excludes pull_request, schedule and workflow_dispatch runs of CI in one check.
    if ($TriggerEvent -ne 'push') {
        return [pscustomobject]@{ ShouldNotify = $false; Reason = "workflow_run.event is '$TriggerEvent', not 'push'." }
    }

    if ($HeadBranch -ne 'main') {
        return [pscustomobject]@{ ShouldNotify = $false; Reason = "workflow_run.head_branch is '$HeadBranch', not 'main'." }
    }

    # workflow_run fires in this repository's context, with this repository's GITHUB_TOKEN and
    # secrets available to the responding job, even for a fork's pull_request run of CI. Excluding
    # pull_request above already rules out the common fork path; this comparison is the explicit,
    # auditable guard against any head_repository that is not this repository.
    if ($HeadRepositoryFullName -ne $BaseRepositoryFullName) {
        return [pscustomobject]@{ ShouldNotify = $false; Reason = "workflow_run.head_repository ('$HeadRepositoryFullName') is not this repository ('$BaseRepositoryFullName')." }
    }

    return [pscustomobject]@{ ShouldNotify = $true; Reason = 'main push CI succeeded in this repository.' }
}

function Get-InfraRepositoryParts {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $InfraRepository)

    if ($InfraRepository -notmatch '^(?<owner>[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)/(?<repo>[A-Za-z0-9._-]+)$') {
        throw "INFRA_REPOSITORY '$InfraRepository' is not in 'owner/repository' format."
    }

    return [pscustomobject]@{
        Owner = $Matches['owner']
        Repo  = $Matches['repo']
    }
}

function ConvertTo-ReleaseCandidatePayload {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $HeadSha,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $RunId,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $RunAttempt
    )

    # HeadSha must come from the source CI's workflow_run event, never from this workflow's own
    # github.sha: a workflow_run-triggered run's github.sha resolves to the default branch tip, not
    # the commit that produced the event, and would silently report the wrong candidate. Callers
    # must pass workflow_run.head_sha here; this function has no way to read github.sha itself.
    if ($HeadSha -cnotmatch '^[0-9a-f]{40}$') {
        throw "source_sha '$HeadSha' must be a 40-character lowercase hex commit SHA."
    }

    if ($RunId -cnotmatch '^[0-9]+$') {
        throw "source_run_id '$RunId' must be a decimal string."
    }

    if ($RunAttempt -cnotmatch '^[1-9][0-9]*$') {
        throw "source_run_attempt '$RunAttempt' must be a positive integer."
    }

    # source_run_id stays a string and source_run_attempt becomes an int so ConvertTo-Json renders
    # them exactly as the infra-side contract expects: a quoted decimal string and a bare number.
    return [ordered]@{
        component          = 'wwt'
        source_sha         = $HeadSha
        source_run_id      = $RunId
        source_run_attempt = [int] $RunAttempt
    }
}

function Send-ReleaseCandidateDispatch {
    param(
        [Parameter(Mandatory)] [string] $Owner,
        [Parameter(Mandatory)] [string] $Repo,
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [System.Collections.Specialized.OrderedDictionary] $Payload,
        [Parameter(Mandatory)] [scriptblock] $Invoker,
        [string] $ApiBaseUrl = 'https://api.github.com'
    )

    $uri = '{0}/repos/{1}/{2}/dispatches' -f $ApiBaseUrl, $Owner, $Repo
    $requestBody = [ordered]@{
        event_type     = 'app-release-candidate-v1'
        client_payload = $Payload
    }
    $bodyJson = $requestBody | ConvertTo-Json -Depth 6 -Compress

    # $Invoker performs the actual HTTP call. Production passes one backed by Invoke-WebRequest;
    # tests pass a fake that never touches the network, so both a successful send and an HTTP error
    # path can be exercised without a live token or a real repository_dispatch.
    & $Invoker -Uri $uri -Token $Token -Body $bodyJson
}
