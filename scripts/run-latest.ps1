$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'source-fingerprint.ps1')

$latest = Join-Path $repo 'artifacts\latest'
$exe = Join-Path $latest 'LabelStudio.Desktop.exe'
$manifestPath = Join-Path $latest 'build-info.json'
$buildScript = Join-Path $PSScriptRoot 'build-latest.ps1'

$currentFingerprint = Get-LabelStudioSourceFingerprint -RepositoryRoot $repo
$manifest = $null
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        Write-Host 'Latest build manifest is unreadable; it will be rebuilt.' -ForegroundColor Yellow
    }
}

$needsBuild = -not (Test-Path -LiteralPath $exe) -or
    $null -eq $manifest -or
    $manifest.sourceFingerprint -ne $currentFingerprint

if ($needsBuild) {
    $runningLatest = Get-Process -Name 'LabelStudio.Desktop' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($latest, [StringComparison]::OrdinalIgnoreCase) }
    if ($runningLatest) {
        throw "Source has changed, but LabelStudio is still running from the latest folder. Save and close it, then open run-latest.cmd again so it can build the changes."
    }

    Write-Host 'Source differs from the last successful build; refreshing latest now.' -ForegroundColor Yellow
    $powerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $powerShell -NoProfile -ExecutionPolicy Bypass -File $buildScript
    if ($LASTEXITCODE -ne 0) {
        throw "Automatic latest build failed with exit code $LASTEXITCODE. The previous known-good app remains unchanged."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $currentFingerprint = Get-LabelStudioSourceFingerprint -RepositoryRoot $repo
    if ($manifest.sourceFingerprint -ne $currentFingerprint) {
        throw 'Source changed again during the automatic build. Re-run run-latest.cmd to build the newest edits.'
    }
}

$alreadyRunning = Get-Process -Name 'LabelStudio.Desktop' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.Equals($exe, [StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1
if ($alreadyRunning) {
    Write-Host "Latest build is already running: $($manifest.informationalVersion)"
    $shell = New-Object -ComObject WScript.Shell
    [void]$shell.AppActivate($alreadyRunning.Id)
    exit 0
}

$dirtySuffix = if ($manifest.dirty) { '-dirty' } else { '' }
Write-Host "Launching LabelStudio $($manifest.version) $($manifest.commit)$dirtySuffix"
Write-Host "Built: $($manifest.builtAtUtc)"
Write-Host "Configuration: $($manifest.configuration) ($($manifest.runtimeIdentifier), self-contained)"
Write-Host "Path: $exe"
Start-Process -FilePath $exe -WorkingDirectory $latest
exit 0
