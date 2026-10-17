$ErrorActionPreference = 'Stop'
$e = '.ai/evidence/storefront-phase5-20261017'
$originals = Get-Content "$e/originals-manifest.json" -Raw | ConvertFrom-Json
$rows = @($originals | ForEach-Object {
    $exists = Test-Path $_.path
    $hash = if ($exists) { (Get-FileHash $_.path).Hash } else { '' }
    [pscustomobject]@{ path=$_.path; originalHash=$_.hash; currentHash=$hash; state=if (!$exists) {'missing'} elseif ($hash -eq $_.hash) {'unchanged'} else {'changed'} }
})
$rows | ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8 "$e/originals-comparison-final.json"
$deleted = @(Get-Content "$e/baseline-deleted.txt")
$untracked = @(Get-Content "$e/baseline-untracked.txt")
$preservation = [pscustomobject]@{
    observedUtc=[DateTime]::UtcNow
    headBefore=(Get-Content "$e/baseline-head.txt").Trim()
    headAfter=(git rev-parse HEAD).Trim()
    baselineDeleted=$deleted.Count
    deletedStillAbsent=@($deleted | Where-Object { !(Test-Path $_) }).Count
    baselineUntracked=$untracked.Count
    untrackedStillPresent=@($untracked | Where-Object { Test-Path $_ }).Count
    indexBefore=@(Get-Content "$e/baseline-git-status.txt" | Where-Object { $_ -match '^[12] [^.]|^u ' }).Count
    indexAfter=@(git diff --cached --raw).Count
    originals=$rows.Count
    originalsChanged=@($rows | Where-Object state -eq changed).Count
    originalsUnchanged=@($rows | Where-Object state -eq unchanged).Count
    originalsMissing=@($rows | Where-Object state -eq missing).Count
}
$preservation | ConvertTo-Json | Set-Content -Encoding utf8 "$e/preservation-final.json"
$preservation | Format-List
git status --porcelain=v2 --branch | Set-Content -Encoding utf8 "$e/final-git-status.txt"
$results = foreach ($name in @('admin-baseline','admin-red','admin-shipping-red','manual-focused','editors-green','admin-final','storefront-final')) {
    $a = Get-Content "$e/$name.json" -Raw | ConvertFrom-Json
    [pscustomobject]@{ run=$name; passed=$a.numPassedTests; failed=$a.numFailedTests; pending=$a.numPendingTests }
}
$results | ConvertTo-Json | Set-Content -Encoding utf8 "$e/frontend-results-summary.json"
$results | Format-Table

[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
$OutputEncoding = [Console]::OutputEncoding
$paths = @(Get-Content "$e/task-files.txt") + 'tests/quality-closeout/phase5-preservation.ps1'
$paths += @(Get-ChildItem tests/browser/storefront-phase5*.js | ForEach-Object { $_.FullName.Substring((Get-Location).Path.Length+1).Replace('\','/') })
$paths = @($paths | Sort-Object -Unique)
$paths | Set-Content -Encoding utf8 "$e/task-files.txt"
$paths | ForEach-Object { [pscustomobject]@{path=[string]$_;sha256=(Get-FileHash $_).Hash} } | ConvertTo-Json -Depth 3 | Set-Content -Encoding utf8 "$e/task-files-manifest.json"
$diff = "$e/task-local-utf8.diff"
'' | Set-Content -Encoding utf8 $diff
$ErrorActionPreference = 'Continue' # Native Git line-ending advisories are retained, not fatal script exceptions.
foreach ($path in $paths) {
    $original = "$e/originals/$path"
    if (!(Test-Path $original)) { $original = "$e/additional-originals/$path" }
    if (Test-Path $original) {
        git diff --no-index -- $original $path 2>> "$e/diff-advisories-final.log" | Out-File -Encoding utf8 -Append $diff
    }
}
git diff --check -- $paths 2> "$e/diff-check-advisories-final.log" | Out-File -Encoding utf8 "$e/diff-check-final.log"
Write-Output "Scoped whitespace exit=$LASTEXITCODE"
