param([string]$Stage = 'baseline')
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$out = Join-Path $root '.ai/evidence/manual-wallet-visibility-20261020'
New-Item -ItemType Directory -Force $out | Out-Null
if ($Stage -eq 'baseline') {
    git status --short | Set-Content (Join-Path $out 'workspace-before.txt')
    git diff --numstat | Set-Content (Join-Path $out 'diff-before.txt')
    $files = @(git diff --name-only)
    $files | ForEach-Object { @{path=$_;hash=(Get-FileHash (Join-Path $root $_)).Hash} } | ConvertTo-Json | Set-Content (Join-Path $out 'existing-work-hashes.json')
}
$listeners = @(Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 5010,5173,5174 } | Select-Object LocalAddress,LocalPort,OwningProcess)
$ids = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
@{observedUtc=[DateTime]::UtcNow.ToString('o');workspace=$root;listeners=$listeners;processes=@(Get-CimInstance Win32_Process | Where-Object {$_.ProcessId -in $ids} | Select-Object ProcessId,CreationDate,ExecutablePath,CommandLine);apiDll=(Get-FileHash (Join-Path $root 'PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll')).Hash;apiDllWritten=(Get-Item (Join-Path $root 'PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll')).LastWriteTimeUtc} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out "runtime-$Stage.json")
$script = Join-Path $root 'tests/browser/manual-wallet-visibility-20261020-baseline.js'
playwright-cli -s=opencode-ui-test run-code --filename=$script | Set-Content (Join-Path $out "browser-$Stage.txt")
