[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')

$missing = 0
function Report([string]$Name, [string]$Detail, [string]$State) {
    $color = @{ ok = 'Green'; warn = 'Yellow'; fail = 'Red' }[$State]
    Write-Host ("[{0,-4}] {1,-16} {2}" -f $State, $Name, $Detail) -ForegroundColor $color
    if ($State -eq 'fail') { $script:missing++ }
}

Report 'PowerShell' "$($PSVersionTable.PSVersion) ($($PSVersionTable.PSEdition))" 'ok'

$props = Get-Content -LiteralPath (Join-Path $Root 'Directory.Build.props') -Raw
$tfm = if ($props -match '<WindowsTfm>([^<]+)</WindowsTfm>') { $Matches[1] } else { 'unknown' }
$need = if ($tfm -match '^net(\d+)') { [int]$Matches[1] } else { 0 }
$best = 0
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    foreach ($line in @(& dotnet --list-sdks)) { if ($line -match '^(\d+)\.') { $best = [Math]::Max($best, [int]$Matches[1]) } }
}
if ($best -eq 0) { Report '.NET SDK' 'dotnet not found (install the .NET SDK)' 'fail' }
elseif ($tfm -eq 'net48') { Report '.NET SDK' "$(& dotnet --version) (targets net48; needs .NET Framework 4.8 targeting pack)" 'ok' }
elseif ($need -gt 0 -and $best -lt $need) { Report '.NET SDK' "newest SDK is $best.x but project targets $tfm (install SDK $need+)" 'fail' }
else { Report '.NET SDK' "$(& dotnet --version) (project targets $tfm)" 'ok' }

Report 'Target framework' "$tfm (UI: wpf)" 'ok'
if (Get-Command git -ErrorAction SilentlyContinue) { Report 'git' ((& git --version) -join '') 'ok' } else { Report 'git' 'not found: build-info.txt will say unknown' 'warn' }

if ($missing -gt 0) { Write-Host "[fail] $missing required item(s) missing." -ForegroundColor Red; exit 1 }
Write-Ok 'Environment looks good.'
