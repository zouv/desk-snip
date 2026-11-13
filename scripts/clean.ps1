[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')

if ((Get-ManagedProcess)) {
    throw 'The app is running; its files are locked. Close it first: sh manager.sh stop'
}

$targets = @()
foreach ($parent in @('src', 'tests')) {
    $dir = Join-Path $Root $parent
    if (Test-Path -LiteralPath $dir) {
        foreach ($project in Get-ChildItem -LiteralPath $dir -Directory) {
            $targets += (Join-Path $project.FullName 'bin'), (Join-Path $project.FullName 'obj')
        }
    }
}
$targets += (Join-Path $Root 'dist'), (Join-Path $RuntimeDir 'staging'), (Join-Path $RuntimeDir 'package.lock')
foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-ManagedPath $target
        Write-Info "Removed $($target.Substring($Root.Length + 1))"
    }
}
Write-Ok 'Clean.'
