[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$Filter = ''
)
. (Join-Path $PSScriptRoot '_common.ps1')

Write-Step "dotnet test ($Configuration)"
$testArguments = @('test', $TestsProj, '-c', $Configuration, '--nologo')
if ($Filter) { $testArguments += @('--filter', $Filter) }
Invoke-Tool 'dotnet' $testArguments
Write-Ok 'Tests passed.'
