param([Parameter(Mandatory=$true)][string]$OutputPath)
$ErrorActionPreference = 'Stop'
$processes = @(Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'dotnet|PointOfSale|rabbit|docker|wsl|iis|node' } | ForEach-Object {
    [pscustomobject]@{ pid=$_.ProcessId; parent=$_.ParentProcessId; name=$_.Name; started=$_.CreationDate.ToUniversalTime().ToString('o'); executable=$_.ExecutablePath; command= $(if ($_.CommandLine -match 'password|pwd=|token|secret|ConnectionStrings') { '[redacted]' } else { $_.CommandLine }) }
})
$connections = @(Get-NetTCPConnection -ErrorAction SilentlyContinue | Where-Object { $_.RemotePort -in 5432,5672,5671 -or $_.LocalPort -eq 5010 } | Select-Object LocalAddress,LocalPort,RemoteAddress,RemotePort,State,OwningProcess)
$services = @(Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'PointOfSale|Shopixy|rabbit|docker|wsl|iis|postgres' -or $_.DisplayName -match 'PointOfSale|Shopixy|RabbitMQ|Docker|PostgreSQL' } | Select-Object Name,State,ProcessId)
$config = @(Get-ChildItem 'PointOfSale.API/appsettings*.json' | ForEach-Object {
    try { $j=Get-Content $_.FullName -Raw | ConvertFrom-Json; [pscustomobject]@{ file=$_.Name; webhookClaimsEnabled=$j.Shipping.WebhookClaims.Enabled } } catch { [pscustomobject]@{ file=$_.Name; parseError=$_.Exception.GetType().Name } }
})
$environmentFlag = [Environment]::GetEnvironmentVariable('Shipping__WebhookClaims__Enabled')
$artifacts = @('PointOfSale.API/bin/Debug/net9.0/PointOfSale.API.dll','PointOfSale.API/bin/Debug/net9.0/PointOfSale.Infrastructure.dll') | ForEach-Object { if (Test-Path $_) { $f=Get-Item $_; [pscustomobject]@{ path=$_; lastWriteUtc=$f.LastWriteTimeUtc.ToString('o'); sha256=(Get-FileHash $_ -Algorithm SHA256).Hash } } }
$health = @('/health/live','/health/ready') | ForEach-Object {
    try { $r=Invoke-WebRequest -Uri "http://localhost:5010$_" -UseBasicParsing -TimeoutSec 15; [pscustomobject]@{ path=$_; status=[int]$r.StatusCode; content=$r.Content } } catch { [pscustomobject]@{ path=$_; error=$_.Exception.GetType().Name } }
}
[pscustomobject]@{ observedAt=[DateTime]::UtcNow.ToString('o'); processes=$processes; connections=$connections; services=$services; configFlags=$config; agentEnvironmentFlag=$environmentFlag; artifacts=$artifacts; health=$health; head=(git rev-parse HEAD); stagedNames=@(git diff --cached --name-only) } | ConvertTo-Json -Depth 15 | Set-Content $OutputPath
Get-Content $OutputPath
