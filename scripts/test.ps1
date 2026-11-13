[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)
. (Join-Path $PSScriptRoot '_common.ps1')

Write-Step "dotnet test ($Configuration)"
Invoke-Tool 'dotnet' @('test', $TestsProj, '-c', $Configuration, '--nologo')
Write-Ok 'Tests passed.'
