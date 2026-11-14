[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')

$failed = @()
function Add-Failure([string]$Name) { $script:failed += $Name }

Write-Step 'tests'
try {
    & (Join-Path $PSScriptRoot 'test.ps1')
    if ($LASTEXITCODE -ne 0) { Add-Failure 'tests' }
}
catch {
    Write-Warn 'Tests failed; continuing strict build and documentation checks.'
    Add-Failure 'tests'
}

Write-Step 'dotnet build -warnaserror (no incremental)'
& dotnet build $Sln -c Debug -warnaserror --no-incremental --nologo
if ($LASTEXITCODE -ne 0) { Add-Failure 'dotnet build -warnaserror' }

Write-Step 'documentation check'
& sh (Join-Path $Root 'check-docs.sh')
if ($LASTEXITCODE -ne 0) { Add-Failure 'check-docs' }

if ($failed.Count -gt 0) {
    Write-Host "[fail] verify failed: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Ok 'verify passed.'
