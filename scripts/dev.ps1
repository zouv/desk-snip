[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')

# A running instance would lock the build output: close it outright.
Close-RunningAppForDev
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
& (Join-Path $PSScriptRoot 'desktop.ps1') start -Configuration Debug
exit $LASTEXITCODE
