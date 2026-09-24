function Get-LabelStudioSourceFingerprint {
    param([Parameter(Mandatory = $true)][string]$RepositoryRoot)

    $files = git -C $RepositoryRoot ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not enumerate source files for the latest-build fingerprint.'
    }

    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in ($files | Sort-Object -Unique)) {
        $fullPath = Join-Path $RepositoryRoot $relativePath
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $fileHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
            $entries.Add("$relativePath|$fileHash")
        }
    }

    $fingerprintInput = [System.Text.Encoding]::UTF8.GetBytes(($entries -join "`n"))
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [System.BitConverter]::ToString($hasher.ComputeHash($fingerprintInput)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}
