param([int]$Port = 5011)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$private = Join-Path $env:LOCALAPPDATA 'Temp/opencode/registration-remove-phone-20261020'
New-Item -ItemType Directory -Force $private | Out-Null
Set-Location (Join-Path $root 'PointOfSale.API')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:Smtp__Host = '127.0.0.1'
$env:Smtp__Port = '2526'
$env:Smtp__EnableSsl = 'false'
$env:Smtp__Username = ''
$env:Smtp__Password = ''
@{observedUtc=[DateTime]::UtcNow.ToString('o');smtpHost='127.0.0.1';smtpPort=2526;
  apiDllSha256=(Get-FileHash 'bin/Debug/net9.0/PointOfSale.API.dll').Hash
} | ConvertTo-Json | Set-Content (Join-Path $root ".ai/evidence/registration-remove-phone-20261020/api-launch-$Port.json")
& dotnet run --no-build --no-launch-profile *> (Join-Path $private "api-$Port.log")
exit $LASTEXITCODE
