# Regression tests for the release-candidate notification decision, payload and dispatch logic.
# These tests need no Docker daemon and send nothing to GitHub or any other network endpoint.
# Run in CI by .github/workflows/ci.yaml's build-test (pwsh) and script-compat (Windows PowerShell
# 5.1) jobs, the same way scripts/test-production-compose.ps1 is.

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-candidate-notify-lib.ps1')

function Assert-Test {
    param([bool] $Condition, [string] $Message)

    if (-not $Condition) {
        throw "Release candidate notify test failed: $Message"
    }
}

function Assert-Throws {
    param([Parameter(Mandatory)] [scriptblock] $ScriptBlock, [Parameter(Mandatory)] [string] $ExpectedMessage)

    $message = $null
    try {
        & $ScriptBlock | Out-Null
    }
    catch {
        $message = $_.Exception.Message
    }

    Assert-Test (-not [string]::IsNullOrWhiteSpace($message)) 'the invalid input was accepted.'
    Assert-Test ($message -like "*$ExpectedMessage*") "the rejection did not explain the cause: $message"
}

$baseRepo = 'example-owner/web-writing-tool'
$validSha = ('a' * 40)
$validRunId = '123456789'
$validRunAttempt = '1'

function New-Decision {
    param(
        [string] $CdEnabled = 'true',
        [string] $Conclusion = 'success',
        [string] $TriggerEvent = 'push',
        [string] $HeadBranch = 'main',
        [string] $HeadRepositoryFullName = $baseRepo,
        [string] $BaseRepositoryFullName = $baseRepo
    )

    return Test-ShouldNotifyReleaseCandidate `
        -CdEnabled $CdEnabled `
        -Conclusion $Conclusion `
        -TriggerEvent $TriggerEvent `
        -HeadBranch $HeadBranch `
        -HeadRepositoryFullName $HeadRepositoryFullName `
        -BaseRepositoryFullName $BaseRepositoryFullName
}

# --- Test-ShouldNotifyReleaseCandidate: only a successful main push in this repository notifies ---

$successCase = New-Decision
Assert-Test $successCase.ShouldNotify 'a main push with a successful CI conclusion was not approved for notification.'

Assert-Test (-not (New-Decision -CdEnabled '').ShouldNotify) 'an unset CD_ENABLED still approved notification.'
Assert-Test (-not (New-Decision -CdEnabled 'false').ShouldNotify) "CD_ENABLED 'false' still approved notification."
Assert-Test (-not (New-Decision -CdEnabled 'True').ShouldNotify) "CD_ENABLED 'True' (wrong case) still approved notification."
Assert-Test (-not (New-Decision -CdEnabled '1').ShouldNotify) "CD_ENABLED '1' still approved notification."

Assert-Test (-not (New-Decision -Conclusion 'failure').ShouldNotify) 'a failed source CI was approved for notification.'
Assert-Test (-not (New-Decision -Conclusion 'cancelled').ShouldNotify) 'a cancelled source CI was approved for notification.'
Assert-Test (-not (New-Decision -Conclusion 'timed_out').ShouldNotify) 'a timed-out source CI was approved for notification.'

Assert-Test (-not (New-Decision -TriggerEvent 'pull_request').ShouldNotify) 'a pull_request-triggered CI run was approved for notification.'
Assert-Test (-not (New-Decision -TriggerEvent 'schedule').ShouldNotify) 'a schedule-triggered CI run was approved for notification.'
Assert-Test (-not (New-Decision -TriggerEvent 'workflow_dispatch').ShouldNotify) 'a workflow_dispatch-triggered CI run was approved for notification.'

Assert-Test (-not (New-Decision -HeadBranch 'develop').ShouldNotify) 'a push to a non-main branch was approved for notification.'

Assert-Test (-not (New-Decision -HeadRepositoryFullName 'someone-else/web-writing-tool').ShouldNotify) 'a fork head_repository was approved for notification.'

# --- Get-InfraRepositoryParts ---

$parts = Get-InfraRepositoryParts -InfraRepository 'my-org/wwt-seo-infra'
Assert-Test ($parts.Owner -ceq 'my-org') 'the infra repository owner was parsed incorrectly.'
Assert-Test ($parts.Repo -ceq 'wwt-seo-infra') 'the infra repository name was parsed incorrectly.'

Assert-Throws -ScriptBlock { Get-InfraRepositoryParts -InfraRepository 'no-slash-here' } -ExpectedMessage "'owner/repository' format"
Assert-Throws -ScriptBlock { Get-InfraRepositoryParts -InfraRepository 'too/many/slashes' } -ExpectedMessage "'owner/repository' format"
Assert-Throws -ScriptBlock { Get-InfraRepositoryParts -InfraRepository '/missing-owner' } -ExpectedMessage "'owner/repository' format"
Assert-Throws -ScriptBlock { Get-InfraRepositoryParts -InfraRepository 'missing-repo/' } -ExpectedMessage "'owner/repository' format"

# --- ConvertTo-ReleaseCandidatePayload: types and values match the infra contract ---

$payload = ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId $validRunId -RunAttempt $validRunAttempt
Assert-Test ($payload.component -ceq 'wwt') 'the payload component was not wwt.'
Assert-Test ($payload.source_sha -ceq $validSha) 'the payload source_sha did not round-trip the given head SHA.'
Assert-Test ($payload.source_run_id -ceq $validRunId) 'the payload source_run_id did not round-trip the given run id.'
Assert-Test ($payload.source_run_attempt -eq 1) 'the payload source_run_attempt was not converted to an integer.'

$payloadJson = $payload | ConvertTo-Json -Depth 6 -Compress
Assert-Test ($payloadJson -like "*`"source_run_id`":`"$validRunId`"*") "source_run_id did not serialize as a JSON string: $payloadJson"
Assert-Test ($payloadJson -like '*"source_run_attempt":1*') "source_run_attempt did not serialize as a bare JSON number: $payloadJson"
Assert-Test ($payloadJson -notlike '*"source_run_attempt":"1"*') "source_run_attempt serialized as a JSON string instead of a number: $payloadJson"

# The payload must depend only on the explicit HeadSha argument, never on any ambient "current
# workflow" SHA. Setting GITHUB_SHA to a different, decoy value and confirming it has no effect is
# what proves scripts/release-candidate-notify.ps1 cannot accidentally report its own github.sha
# instead of the source CI's workflow_run.head_sha.
$previousGitHubSha = $env:GITHUB_SHA
try {
    $env:GITHUB_SHA = ('f' * 40)
    $payloadWithDecoySha = ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId $validRunId -RunAttempt $validRunAttempt
    Assert-Test ($payloadWithDecoySha.source_sha -ceq $validSha) 'the payload source_sha was influenced by an unrelated GITHUB_SHA value.'
}
finally {
    if ($null -eq $previousGitHubSha) {
        Remove-Item -LiteralPath Env:GITHUB_SHA -ErrorAction SilentlyContinue
    }
    else {
        $env:GITHUB_SHA = $previousGitHubSha
    }
}

Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha 'not-a-sha' -RunId $validRunId -RunAttempt $validRunAttempt } -ExpectedMessage 'source_sha'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha ($validSha.ToUpperInvariant()) -RunId $validRunId -RunAttempt $validRunAttempt } -ExpectedMessage 'source_sha'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha ($validSha.Substring(1)) -RunId $validRunId -RunAttempt $validRunAttempt } -ExpectedMessage 'source_sha'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId 'not-a-number' -RunAttempt $validRunAttempt } -ExpectedMessage 'source_run_id'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId $validRunId -RunAttempt '0' } -ExpectedMessage 'source_run_attempt'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId $validRunId -RunAttempt '-1' } -ExpectedMessage 'source_run_attempt'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId $validRunId -RunAttempt '01' } -ExpectedMessage 'source_run_attempt'
Assert-Throws -ScriptBlock { ConvertTo-ReleaseCandidatePayload -HeadSha $validSha -RunId $validRunId -RunAttempt 'two' } -ExpectedMessage 'source_run_attempt'

# --- Send-ReleaseCandidateDispatch: a fake invoker proves the request shape and that HTTP errors
#     are never swallowed as success ---

$capturedRequests = New-Object System.Collections.Generic.List[pscustomobject]
$fakeSuccessInvoker = {
    param([string] $Uri, [string] $Token, [string] $Body)
    $capturedRequests.Add([pscustomobject]@{ Uri = $Uri; Token = $Token; Body = $Body })
}

Send-ReleaseCandidateDispatch -Owner 'my-org' -Repo 'wwt-seo-infra' -Token 'fake-token' -Payload $payload -Invoker $fakeSuccessInvoker
Assert-Test ($capturedRequests.Count -eq 1) 'the dispatch invoker was not called exactly once.'
Assert-Test ($capturedRequests[0].Uri -ceq 'https://api.github.com/repos/my-org/wwt-seo-infra/dispatches') "the dispatch URI was wrong: $($capturedRequests[0].Uri)"
Assert-Test ($capturedRequests[0].Body -like '*"event_type":"app-release-candidate-v1"*') "the dispatch body did not carry the contracted event_type: $($capturedRequests[0].Body)"
Assert-Test ($capturedRequests[0].Body -like "*`"source_sha`":`"$validSha`"*") "the dispatch body did not carry the given source_sha: $($capturedRequests[0].Body)"

$fakeFailingInvoker = {
    param([string] $Uri, [string] $Token, [string] $Body)
    throw 'simulated HTTP 422 from GitHub'
}

Assert-Throws -ScriptBlock { Send-ReleaseCandidateDispatch -Owner 'my-org' -Repo 'wwt-seo-infra' -Token 'fake-token' -Payload $payload -Invoker $fakeFailingInvoker } -ExpectedMessage 'simulated HTTP 422'

# --- scripts/release-candidate-decide.ps1 end to end, without any external send ---
#
# Runs the real script as a child process of the same PowerShell host that is running this test
# (pwsh or Windows PowerShell), so a wiring mistake such as an env var name mismatch between this
# script and notify-release-candidate.yaml would show up here, not only in the pure library tests
# above.

function Invoke-DecideScript {
    param(
        [string] $CdEnabled,
        [string] $Conclusion = 'success',
        [string] $TriggerEvent = 'push',
        [string] $HeadBranch = 'main',
        [string] $HeadRepositoryFullName = $baseRepo,
        [string] $BaseRepositoryFullName = $baseRepo
    )

    $hostExecutable = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
    $outputFile = [System.IO.Path]::GetTempFileName()
    $scriptPath = Join-Path $PSScriptRoot 'release-candidate-decide.ps1'

    $previousEnv = [ordered]@{
        CD_ENABLED                   = $env:CD_ENABLED
        WORKFLOW_RUN_CONCLUSION      = $env:WORKFLOW_RUN_CONCLUSION
        WORKFLOW_RUN_EVENT           = $env:WORKFLOW_RUN_EVENT
        WORKFLOW_RUN_HEAD_BRANCH     = $env:WORKFLOW_RUN_HEAD_BRANCH
        WORKFLOW_RUN_HEAD_REPOSITORY = $env:WORKFLOW_RUN_HEAD_REPOSITORY
        BASE_REPOSITORY              = $env:BASE_REPOSITORY
        GITHUB_OUTPUT                = $env:GITHUB_OUTPUT
        # Must stay unset for the disabled-CD_ENABLED case below: the decide script must not need
        # them, proving the skip happens before any infra credential would be read.
        INFRA_REPOSITORY             = $env:INFRA_REPOSITORY
        CD_APP_ID                    = $env:CD_APP_ID
        CD_APP_PRIVATE_KEY           = $env:CD_APP_PRIVATE_KEY
    }

    try {
        $env:CD_ENABLED = $CdEnabled
        $env:WORKFLOW_RUN_CONCLUSION = $Conclusion
        $env:WORKFLOW_RUN_EVENT = $TriggerEvent
        $env:WORKFLOW_RUN_HEAD_BRANCH = $HeadBranch
        $env:WORKFLOW_RUN_HEAD_REPOSITORY = $HeadRepositoryFullName
        $env:BASE_REPOSITORY = $BaseRepositoryFullName
        $env:GITHUB_OUTPUT = $outputFile
        Remove-Item -LiteralPath Env:INFRA_REPOSITORY -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath Env:CD_APP_ID -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath Env:CD_APP_PRIVATE_KEY -ErrorAction SilentlyContinue

        & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $scriptPath | Out-Null
        $exitCode = $LASTEXITCODE

        $outputContent = @(Get-Content -LiteralPath $outputFile -ErrorAction SilentlyContinue)
        return [pscustomobject]@{
            ExitCode = $exitCode
            Output   = $outputContent
        }
    }
    finally {
        foreach ($key in $previousEnv.Keys) {
            if ($null -eq $previousEnv[$key]) {
                Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
            }
            else {
                Set-Item -LiteralPath "Env:$key" -Value $previousEnv[$key]
            }
        }
        Remove-Item -LiteralPath $outputFile -Force -ErrorAction SilentlyContinue
    }
}

$disabledRun = Invoke-DecideScript -CdEnabled ''
Assert-Test ($disabledRun.ExitCode -eq 0) 'release-candidate-decide.ps1 exited non-zero for a normal, disabled run.'
Assert-Test ($disabledRun.Output -contains 'should_notify=false') "release-candidate-decide.ps1 did not write should_notify=false to GITHUB_OUTPUT when CD_ENABLED was unset. Output: $($disabledRun.Output -join '; ')"

$enabledRun = Invoke-DecideScript -CdEnabled 'true'
Assert-Test ($enabledRun.ExitCode -eq 0) 'release-candidate-decide.ps1 exited non-zero for a normal, enabled run.'
Assert-Test ($enabledRun.Output -contains 'should_notify=true') "release-candidate-decide.ps1 did not write should_notify=true for a main push success. Output: $($enabledRun.Output -join '; ')"

$prRun = Invoke-DecideScript -CdEnabled 'true' -TriggerEvent 'pull_request'
Assert-Test ($prRun.ExitCode -eq 0) 'release-candidate-decide.ps1 exited non-zero for a normal, pull_request-sourced run.'
Assert-Test ($prRun.Output -contains 'should_notify=false') "release-candidate-decide.ps1 approved notification for a pull_request-sourced CI run. Output: $($prRun.Output -join '; ')"

# --- scripts/release-candidate-recheck.ps1 end to end, reproducing the "Re-run failed jobs" bypass ---
#
# gate's should_notify output is a job output cached at the value it had when gate last succeeded.
# GitHub Actions "Re-run failed jobs" does not re-run a job that already succeeded, so if gate
# approved a run, notify later failed, and CD_ENABLED is then disabled, re-running only notify
# would still see gate's cached should_notify=true. release-candidate-recheck.ps1 exists to reject
# that case live, before any infra credential is touched.

function Invoke-RecheckScript {
    param(
        [string] $CdEnabled,
        [string] $Conclusion = 'success',
        [string] $TriggerEvent = 'push',
        [string] $HeadBranch = 'main',
        [string] $HeadRepositoryFullName = $baseRepo,
        [string] $BaseRepositoryFullName = $baseRepo
    )

    $hostExecutable = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh' } else { 'powershell' }
    $scriptPath = Join-Path $PSScriptRoot 'release-candidate-recheck.ps1'

    $previousEnv = [ordered]@{
        CD_ENABLED                   = $env:CD_ENABLED
        WORKFLOW_RUN_CONCLUSION      = $env:WORKFLOW_RUN_CONCLUSION
        WORKFLOW_RUN_EVENT           = $env:WORKFLOW_RUN_EVENT
        WORKFLOW_RUN_HEAD_BRANCH     = $env:WORKFLOW_RUN_HEAD_BRANCH
        WORKFLOW_RUN_HEAD_REPOSITORY = $env:WORKFLOW_RUN_HEAD_REPOSITORY
        BASE_REPOSITORY              = $env:BASE_REPOSITORY
        # Must stay unset: proves the recheck rejects before any infra credential would be read,
        # the same way Invoke-DecideScript proves it for the gate job above.
        INFRA_REPOSITORY             = $env:INFRA_REPOSITORY
        INFRA_TOKEN                  = $env:INFRA_TOKEN
        CD_APP_ID                    = $env:CD_APP_ID
        CD_APP_PRIVATE_KEY           = $env:CD_APP_PRIVATE_KEY
    }

    # Windows PowerShell turns a native process's stderr into a terminating ErrorRecord under
    # $ErrorActionPreference = 'Stop', the same reason production-compose-lib.ps1's
    # Invoke-DockerCapturing keeps it 'Continue' around a native call. release-candidate-recheck.ps1
    # is expected to write to stderr on the rejection path, so this call needs the same guard.
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $env:CD_ENABLED = $CdEnabled
        $env:WORKFLOW_RUN_CONCLUSION = $Conclusion
        $env:WORKFLOW_RUN_EVENT = $TriggerEvent
        $env:WORKFLOW_RUN_HEAD_BRANCH = $HeadBranch
        $env:WORKFLOW_RUN_HEAD_REPOSITORY = $HeadRepositoryFullName
        $env:BASE_REPOSITORY = $BaseRepositoryFullName
        Remove-Item -LiteralPath Env:INFRA_REPOSITORY -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath Env:INFRA_TOKEN -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath Env:CD_APP_ID -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath Env:CD_APP_PRIVATE_KEY -ErrorAction SilentlyContinue

        & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $scriptPath 2>$null | Out-Null
        $exitCode = $LASTEXITCODE

        return [pscustomobject]@{ ExitCode = $exitCode }
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        foreach ($key in $previousEnv.Keys) {
            if ($null -eq $previousEnv[$key]) {
                Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
            }
            else {
                Set-Item -LiteralPath "Env:$key" -Value $previousEnv[$key]
            }
        }
    }
}

$recheckStillEnabled = Invoke-RecheckScript -CdEnabled 'true'
Assert-Test ($recheckStillEnabled.ExitCode -eq 0) "release-candidate-recheck.ps1 exited non-zero even though every condition still holds. Exit code: $($recheckStillEnabled.ExitCode)"

# The reported bypass, reproduced directly: this is exactly the combination of conditions gate
# would have approved once (success, push, main, no fork), but CD_ENABLED is unset now, as it would
# be after disabling CD_ENABLED and then using "Re-run failed jobs" on a previously failed notify
# job. INFRA_REPOSITORY/CD_APP_ID/CD_APP_PRIVATE_KEY are deliberately left unset by this helper, so
# a zero exit code here would mean the recheck let a disabled-CD run proceed toward the token step.
$recheckNowDisabled = Invoke-RecheckScript -CdEnabled ''
Assert-Test ($recheckNowDisabled.ExitCode -ne 0) 'release-candidate-recheck.ps1 exited zero (success) even though CD_ENABLED is now unset, despite every other condition still matching a previously approved gate decision. This is the "Re-run failed jobs after disabling CD_ENABLED" bypass.'

$recheckFalseString = Invoke-RecheckScript -CdEnabled 'false'
Assert-Test ($recheckFalseString.ExitCode -ne 0) "release-candidate-recheck.ps1 exited zero (success) with CD_ENABLED='false'."

$recheckWrongCase = Invoke-RecheckScript -CdEnabled 'True'
Assert-Test ($recheckWrongCase.ExitCode -ne 0) "release-candidate-recheck.ps1 exited zero (success) with CD_ENABLED='True' (wrong case)."

Write-Output 'All release candidate notify tests passed.'
