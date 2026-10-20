param([string]$Stage = 'baseline', [switch]$ConfiguredSmtp)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$private = Join-Path $env:LOCALAPPDATA 'Temp/opencode/registration-phone-20261020'
New-Item -ItemType Directory -Path $private -Force | Out-Null
Set-Location (Join-Path $root 'PointOfSale.API')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5010'
if (-not $ConfiguredSmtp) {
    $env:Smtp__Host = '127.0.0.1'
    $env:Smtp__Port = '2526'
    $env:Smtp__EnableSsl = 'false'
    $env:Smtp__Username = ''
    $env:Smtp__Password = ''
}
# Existing SMTP registration is used unchanged, targeting only the local QA sink.
@{observedUtc=[DateTime]::UtcNow.ToString('o');stage=$Stage;configuredSmtp=[bool]$ConfiguredSmtp;
  smtpOverrideHost=$(if ($ConfiguredSmtp) { $null } else { '127.0.0.1' });
  apiDllSha256=(Get-FileHash (Join-Path (Get-Location) 'bin/Debug/net9.0/PointOfSale.API.dll')).Hash
} | ConvertTo-Json | Set-Content (Join-Path $root ".ai/evidence/registration-phone-20261020/api-launch-$Stage.json")
& dotnet run --no-build --no-launch-profile *> (Join-Path $private "api-$Stage.log")
exit $LASTEXITCODE
