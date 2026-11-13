[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
. (Join-Path $PSScriptRoot '_common.ps1')

$running = Get-ManagedProcess
if ($running) {
    Write-Warn 'The app is running; building may fail on locked files. Close it first: sh manager.sh stop (this never force-kills).'
}
Write-Step "dotnet build ($Configuration)"
Invoke-Tool 'dotnet' @('build', $Sln, '-c', $Configuration, '--nologo')
Write-Ok "Build finished: $(Get-ExePath $Configuration)"
