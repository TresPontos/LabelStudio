# Builds, tests, and publishes LabelStudio into the canonical artifacts\latest folder.
# The latest folder is only replaced after a fully successful pipeline; any failure
# leaves the previous known-good build untouched.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'source-fingerprint.ps1')
Set-Location -LiteralPath $repo

$project = Join-Path $repo 'src\LabelStudio.Desktop\LabelStudio.Desktop.csproj'
$solution = Join-Path $repo 'LabelStudio.slnx'
$artifacts = Join-Path $repo 'artifacts'
$latest = Join-Path $artifacts 'latest'
$staging = Join-Path $artifacts 'latest-staging'
$previous = Join-Path $artifacts 'latest-previous'

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null

# Recover the last known-good build if the process or machine stopped during
# the two-rename promotion window.
if (-not (Test-Path -LiteralPath $latest) -and (Test-Path -LiteralPath $previous)) {
    [void](Move-Item -LiteralPath $previous -Destination $latest)
}

function Invoke-Step {
    param([string]$Description, [scriptblock]$Action)
    Write-Host "==> $Description" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

# Version metadata from git.
$commit = git rev-parse --short HEAD
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
    throw "Could not determine git commit. Is this a git repository?"
}
$statusOutput = git status --porcelain
$dirty = -not [string]::IsNullOrWhiteSpace($statusOutput)
$versionPrefix = Select-String -Path $project -Pattern '<Version>([^<]+)</Version>' |
    Select-Object -First 1
if ($null -eq $versionPrefix) {
    throw "No <Version> element found in $project."
}
$version = $versionPrefix.Matches[0].Groups[1].Value
$builtAtUtc = (Get-Date).ToUniversalTime()
$builtStamp = $builtAtUtc.ToString('yyyyMMddHHmmss')
$commitToken = if ($dirty) { "commitdirty.$commit" } else { "commit.$commit" }
$informationalVersion = "$version+$commitToken.built.$builtStamp"

Write-Host "Building LabelStudio $version ($commit$(if ($dirty) { ' [dirty]' }))" -ForegroundColor Cyan

# Refuse to promote a new build while the latest app is still running; the
# folder swap would fail and the user could end up launching a mixed state.
$running = Get-Process -Name 'LabelStudio.Desktop' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($latest, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw "LabelStudio is currently running from $latest. Close it before building a new latest version."
}

# Discard any staging directory left by a previous interrupted invocation so
# publish can never combine new outputs with stale files.
if (Test-Path -LiteralPath $staging) {
    Remove-Item -LiteralPath $staging -Recurse -Force
}

# Pipeline: restore, test, publish. Each step must succeed.
# On failure the staging folder is discarded; artifacts\latest is never touched
# until every step has passed.
$testsPassed = 0
try {
    Invoke-Step 'Restoring' { dotnet restore $solution }
    Write-Host '==> Running Release tests' -ForegroundColor Cyan
    $testOutput = dotnet test $solution --configuration Release --no-restore 2>&1
    $testExitCode = $LASTEXITCODE
    $testOutput | Out-Host
    if ($testExitCode -ne 0) {
        throw "Running tests failed with exit code $testExitCode."
    }
    foreach ($line in $testOutput) {
        if ("$line" -match 'Passed:\s*(\d+)') {
            $testsPassed += [int]$Matches[1]
        }
    }
    if ($testsPassed -le 0) {
        throw 'Test command succeeded but reported no passing tests; refusing to promote an unverified build.'
    }

    Invoke-Step 'Restoring win-x64 publish assets' { dotnet restore $project -r win-x64 }

    $sourceFingerprint = Get-LabelStudioSourceFingerprint -RepositoryRoot $repo

    Invoke-Step 'Publishing Release build' {
        dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore `
            -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:InformationalVersion=$informationalVersion `
            -p:Version=$version `
            -p:IncludeSourceRevisionInInformationalVersion=false `
            -p:PublishDir="$staging\app\"
    }

    $publishedFingerprint = Get-LabelStudioSourceFingerprint -RepositoryRoot $repo
    if ($publishedFingerprint -ne $sourceFingerprint) {
        throw 'Source changed during the build. The staged build was discarded; run build-latest.cmd again.'
    }
}
catch {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
    throw
}

$publishedExe = Join-Path $staging 'app\LabelStudio.Desktop.exe'
if (-not (Test-Path -LiteralPath $publishedExe)) {
    throw "Publish succeeded but $publishedExe was not found."
}

# Manifest written after all validation passed, alongside the app.
$manifest = [ordered]@{
    version = $version
    commit = $commit
    dirty = $dirty
    builtAtUtc = $builtAtUtc.ToString('o')
    configuration = 'Release'
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    informationalVersion = $informationalVersion
    sourceFingerprint = $sourceFingerprint
    testsPassed = $testsPassed
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'app\build-info.json')

# Atomic-ish promotion: staging -> latest, previous latest archived until success.
if (Test-Path -LiteralPath $previous) {
    Remove-Item -LiteralPath $previous -Recurse -Force
}
if (Test-Path -LiteralPath $latest) {
    [void](Move-Item -LiteralPath $latest -Destination $previous)
}
try {
    [void](Move-Item -LiteralPath (Join-Path $staging 'app') -Destination $latest)
}
catch {
    # Rollback: restore the previous known-good build if the swap failed.
    if (Test-Path -LiteralPath $previous) {
        [void](Move-Item -LiteralPath $previous -Destination $latest)
    }
    throw "Could not promote the new build to latest: $($_.Exception.Message)"
}
if (Test-Path -LiteralPath $previous) {
    Remove-Item -LiteralPath $previous -Recurse -Force
}
Remove-Item -LiteralPath $staging -Recurse -Force

Write-Host ''
Write-Host "Latest build updated: $latest" -ForegroundColor Green
Write-Host "  $version - $commit$(if ($dirty) { '-dirty' }) - built $($builtAtUtc.ToString('yyyy-MM-dd HH:mm')) UTC"
Write-Host "  Launch with: run-latest.cmd" -ForegroundColor Yellow
exit 0
