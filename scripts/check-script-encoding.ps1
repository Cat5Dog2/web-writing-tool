# Enforces the PowerShell script encoding rule in docs/coding-guidelines.md 19.1.
#
# Windows PowerShell 5.1 reads a BOM-less file as the system ANSI code page. A UTF-8 non-ASCII
# character then decodes into different bytes and, on a Japanese code page, can consume the
# following newline. The next source line disappears from the parse with no error at all, so the
# script silently behaves differently on 5.1 than on pwsh.
#
# A BOM would fix the decoding but the repository ships every script without one, and adding a BOM
# to some scripts and not others is its own trap. Keeping the scripts ASCII removes the ambiguity.
param(
    [string] $Directory = 'scripts'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot $Directory

if (-not (Test-Path -LiteralPath $target)) {
    throw "Directory was not found at $target"
}

$files = @(Get-ChildItem -LiteralPath $target -Filter '*.ps1' -File | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No PowerShell scripts were found under $target"
}

$failures = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)

    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $failures.Add("$($file.Name): has a UTF-8 BOM. Save it without one.")
        continue
    }

    $offset = -1
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -gt 0x7F) {
            $offset = $index
            break
        }
    }

    if ($offset -ge 0) {
        # Report the line number so the offending comment is easy to find.
        $line = 1
        for ($index = 0; $index -lt $offset; $index++) {
            if ($bytes[$index] -eq 0x0A) {
                $line++
            }
        }

        $failures.Add("$($file.Name): line $line has a non-ASCII byte 0x$('{0:X2}' -f $bytes[$offset]). Use English text.")
    }
}

foreach ($failure in $failures) {
    Write-Output $failure
}

if ($failures.Count -gt 0) {
    Write-Output ''
    Write-Output "Script encoding check failed for $($failures.Count) file(s). See docs/coding-guidelines.md 19.1."
    exit 1
}

Write-Output "Script encoding check passed for $($files.Count) script(s) under $Directory."
exit 0
