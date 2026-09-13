# Runs last in the "notify" job of notify-release-candidate.yaml, after the infra-scoped GitHub
# App token has been minted. Builds the app-release-candidate-v1 payload from the source CI's
# workflow_run event fields and sends the repository_dispatch. A non-2xx response, a malformed
# input, or any other error must fail this step; nothing here treats an HTTP error as success.
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'release-candidate-notify-lib.ps1')

function Read-RequiredEnvironmentVariable {
    param([Parameter(Mandatory)] [string] $Name)

    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required environment variable '$Name' is not set."
    }
    return $value
}

$infraRepository = Read-RequiredEnvironmentVariable -Name 'INFRA_REPOSITORY'
$token = Read-RequiredEnvironmentVariable -Name 'INFRA_TOKEN'
$headSha = Read-RequiredEnvironmentVariable -Name 'SOURCE_SHA'
$runId = Read-RequiredEnvironmentVariable -Name 'SOURCE_RUN_ID'
$runAttempt = Read-RequiredEnvironmentVariable -Name 'SOURCE_RUN_ATTEMPT'

$parts = Get-InfraRepositoryParts -InfraRepository $infraRepository
$payload = ConvertTo-ReleaseCandidatePayload -HeadSha $headSha -RunId $runId -RunAttempt $runAttempt

Write-Output "Sending app-release-candidate-v1 for wwt@$headSha (run $runId attempt $runAttempt) to $($parts.Owner)/$($parts.Repo)."

$invoker = {
    param([string] $Uri, [string] $Token, [string] $Body)

    $response = Invoke-WebRequest -Uri $Uri -Method Post -Headers @{
        Authorization          = "Bearer $Token"
        Accept                 = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    } -ContentType 'application/json; charset=utf-8' -Body $Body -UseBasicParsing

    # Invoke-WebRequest already throws on a non-2xx status. This re-checks for the exact documented
    # success code so an unexpected 2xx cannot be mistaken for a completed dispatch either.
    $statusCode = [int] $response.StatusCode
    if ($statusCode -ne 204) {
        throw "GitHub returned unexpected status $statusCode for the repository_dispatch request."
    }
}

Send-ReleaseCandidateDispatch -Owner $parts.Owner -Repo $parts.Repo -Token $token -Payload $payload -Invoker $invoker

Write-Output 'Sent the release candidate dispatch to infra.'
