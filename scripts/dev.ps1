[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')

if ((Get-ManagedProcess)) {
    Write-Info 'The app is running; asking it to exit before rebuilding.'
    & (Join-Path $PSScriptRoot 'desktop.ps1') stop
    if ($LASTEXITCODE -ne 0) { exit 1 }
}
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
& (Join-Path $PSScriptRoot 'desktop.ps1') start -Configuration Debug
exit $LASTEXITCODE
