# Mechanises the "no critical NuGet vulnerabilities" release gate in docs/ci-cd-design.md.
# Reads "dotnet list package --vulnerable" as JSON and fails when any High/Critical finding exists.
# Test projects are in scope on purpose: the release gate is not limited to production projects.
param(
    [string[]] $BlockedSeverity = @('High', 'Critical')
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot 'WebWritingTool.slnx'
$solution = 'WebWritingTool.slnx'

if (-not (Test-Path -LiteralPath $solutionPath)) {
    throw "Solution file was not found at $solutionPath"
}

$listArguments = @(
    'list',
    $solution,
    'package',
    '--vulnerable',
    '--include-transitive',
    '--format',
    'json',
    '--output-version',
    '1'
)

$output = & (Join-Path $PSScriptRoot 'dotnet.ps1') @listArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet list package --vulnerable failed with exit code $LASTEXITCODE."
}

# The command runs through the development SDK container, so restore logs can surround the JSON.
$text = ($output | Out-String)
$jsonStart = $text.IndexOf('{')
$jsonEnd = $text.LastIndexOf('}')
if ($jsonStart -lt 0 -or $jsonEnd -le $jsonStart) {
    throw "dotnet list package did not return JSON output."
}

$report = $text.Substring($jsonStart, $jsonEnd - $jsonStart + 1) | ConvertFrom-Json

$findings = New-Object System.Collections.Generic.List[object]
foreach ($project in @($report.projects)) {
    if (-not $project.frameworks) {
        continue
    }

    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project.path)
    foreach ($framework in @($project.frameworks)) {
        $packages = @($framework.topLevelPackages) + @($framework.transitivePackages)
        foreach ($package in $packages) {
            if (-not $package -or -not $package.vulnerabilities) {
                continue
            }

            foreach ($vulnerability in @($package.vulnerabilities)) {
                $findings.Add([pscustomobject]@{
                        Project  = $projectName
                        Package  = $package.id
                        Version  = $package.resolvedVersion
                        Severity = $vulnerability.severity
                        Advisory = $vulnerability.advisoryurl
                    })
            }
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Output 'NuGet vulnerability scan: no vulnerable packages found.'
    exit 0
}

Write-Output 'NuGet vulnerability scan findings:'
$findings | Sort-Object Severity, Project, Package | Format-Table -AutoSize | Out-String | Write-Output

$blocked = @($findings | Where-Object { $BlockedSeverity -contains $_.Severity })
if ($blocked.Count -gt 0) {
    Write-Output "Blocked by $($blocked.Count) vulnerability finding(s) with severity: $($BlockedSeverity -join ', ')."
    exit 1
}

Write-Output "No findings with severity: $($BlockedSeverity -join ', ')."
exit 0
