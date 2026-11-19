# Shared helpers for scripts/*.ps1 (dot-source this file). ASCII only; runs on Windows PowerShell 5.1 and PowerShell 7+.
$ErrorActionPreference = 'Stop'

$script:AppName = 'DeskSnip'
$script:EnvPrefix = 'DESK_SNIP_'
$script:Root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$script:RuntimeDir = Join-Path $Root '.runtime'
$script:RunRecord = Join-Path $RuntimeDir 'app.json'
$script:Sln = Join-Path $Root ($AppName + '.sln')
$script:DesktopProj = Join-Path $Root "src/$AppName.App/$AppName.App.csproj"
$script:TestsProj = Join-Path $Root "tests/$AppName.Tests/$AppName.Tests.csproj"

function Write-Step([string]$Text) { Write-Host "[step] $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "[info] $Text" }
function Write-Ok([string]$Text) { Write-Host "[ ok ] $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) { Write-Host "[warn] $Text" -ForegroundColor Yellow }

function Invoke-Tool([string]$File, [string[]]$Arguments, [string]$WorkingDirectory = $Root) {
    Push-Location $WorkingDirectory
    try {
        & $File @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $File $($Arguments -join ' ')" }
    } finally { Pop-Location }
}

function Write-Utf8File([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

# Single version source: Directory.Build.props (read through MSBuild, so imports and overrides are honoured).
function Get-AppVersion {
    $value = & dotnet msbuild $DesktopProj -getProperty:Version -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read Version from MSBuild' }
    $version = ($value -join '').Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid Version '$version' (expected major.minor.patch)" }
    return $version
}

# Newest built exe for a configuration (TFM folder is decided at init time, so glob it).
function Get-ExePath([string]$Configuration = 'Debug') {
    $bin = Join-Path $Root "src/$AppName.App/bin/$Configuration"
    if (-not (Test-Path -LiteralPath $bin)) { return $null }
    $found = Get-ChildItem -LiteralPath $bin -Filter "$AppName.exe" -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/]publish[\\/]' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($found) { return $found.FullName }
    return $null
}

# ---- path safety: never operate outside the repo, never follow junctions / symlinks ----
function Assert-ManagedPath([string]$Path, [string]$Base = $Root) {
    $baseFull = [IO.Path]::GetFullPath($Base).TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if (-not $target.StartsWith($baseFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe path: $target (must be inside $baseFull)"
    }
    $probe = $target
    while ($probe -and $probe.Length -gt $baseFull.Length) {
        if ((Test-Path -LiteralPath $probe) -and ((Get-Item -LiteralPath $probe -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Path contains a reparse point (junction/symlink): $probe"
        }
        $probe = Split-Path -Parent $probe
    }
    return $target
}

function Remove-ManagedPath([string]$Path, [string]$Base = $Root) {
    $target = Assert-ManagedPath $Path $Base
    if (-not (Test-Path -LiteralPath $target)) { return }
    $nestedLink = Get-ChildItem -LiteralPath $target -Recurse -Force -ErrorAction Stop |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1
    if ($nestedLink) { throw "Path contains a nested reparse point (junction/symlink): $($nestedLink.FullName)" }
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try { Remove-Item -LiteralPath $target -Recurse -Force; return }
        catch {
            if ($attempt -eq 5) { throw "Cannot delete $target (is the app still running? use: sh manager.sh stop): $($_.Exception.Message)" }
            Start-Sleep -Seconds 1
        }
    }
}

# ---- desktop process state (.runtime/app.json: pid + exe path + start time, so a reused pid is not mistaken for us) ----
function Get-ManagedProcess {
    if (-not (Test-Path -LiteralPath $RunRecord)) { return $null }
    try { $saved = Get-Content -LiteralPath $RunRecord -Raw | ConvertFrom-Json } catch { return $null }
    $process = Get-Process -Id $saved.pid -ErrorAction SilentlyContinue
    if (-not $process) { return $null }
    try {
        if ($process.StartTime.ToUniversalTime().Ticks.ToString() -eq [string]$saved.started) { return $process }
    } catch { }
    return $null
}

function Save-RunRecord($Process, [string]$Exe, [string]$Configuration) {
    New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null
    $record = [ordered]@{
        pid = $Process.Id; path = $Exe; configuration = $Configuration
        started = $Process.StartTime.ToUniversalTime().Ticks.ToString()
        startedAt = (Get-Date).ToString('o')
    }
    Write-Utf8File $RunRecord ($record | ConvertTo-Json)
}

# ---- running app guard: dev force-closes it, package asks first ----
function Get-RunningAppProcesses {
    return @(Get-Process -Name $AppName -ErrorAction SilentlyContinue)
}

function Write-RunningAppProcesses($Processes) {
    Write-Warn "$AppName is running:"
    foreach ($process in $Processes) {
        $path = $null
        try { $path = [string]$process.Path } catch { }
        if ([string]::IsNullOrWhiteSpace($path)) { $path = '(path unavailable)' }
        Write-Host ("       PID {0}  {1}" -f $process.Id, $path)
    }
}

function Stop-RunningAppProcesses($Processes) {
    foreach ($process in $Processes) {
        try { $process.Kill(); [void]$process.WaitForExit(5000) } catch { }
        finally { $process.Dispose() }
    }
    Start-Sleep -Milliseconds 300
    $remaining = @(Get-RunningAppProcesses)
    if ($remaining.Count -gt 0) {
        $details = ($remaining | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ', '
        foreach ($process in $remaining) { $process.Dispose() }
        throw "Could not close $AppName ($details). Close it manually and retry."
    }
    Remove-Item -LiteralPath $RunRecord -Force -ErrorAction SilentlyContinue
}

# dev: close a running instance outright, then continue with the rebuild.
function Close-RunningAppForDev {
    $processes = @(Get-RunningAppProcesses)
    if ($processes.Count -eq 0) { return }
    Write-RunningAppProcesses $processes
    Write-Warn 'Force-closing discards any unsaved state in the running app.'
    Write-Info "Force-closing $AppName before rebuilding."
    Stop-RunningAppProcesses $processes
    Write-Ok "Closed the running $AppName instance(s); continuing."
}

# package: list the running instance and only close it after an explicit Y.
function Request-CloseRunningAppForPackage {
    $processes = @(Get-RunningAppProcesses)
    if ($processes.Count -eq 0) { return }
    Write-RunningAppProcesses $processes
    Write-Warn 'Force-closing discards any unsaved state in the running app.'
    $forceClose = [Environment]::GetEnvironmentVariable($EnvPrefix + 'FORCE_CLOSE')
    if ($forceClose -ne '1') {
        if ([Console]::IsInputRedirected) {
            throw "$AppName is running. Close it first, or set $($EnvPrefix)FORCE_CLOSE=1 to confirm force-closing it."
        }
        $answer = Read-Host "Force-close $AppName and continue packaging? Enter Y to confirm; anything else cancels"
        if ($answer -notmatch '^\s*[Yy]([Ee][Ss])?\s*$') {
            Write-Info 'Packaging cancelled; the running app was left alone.'
            exit 2
        }
    }
    Stop-RunningAppProcesses $processes
    Write-Ok "Closed the running $AppName instance(s); continuing."
}

function Get-GitSha {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) { return 'unknown (git not found)' }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $head = & git -C $Root rev-parse --verify --quiet HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $head) { return (($head -join '').Trim()) }
    } finally { $ErrorActionPreference = $previous }
    return 'unknown (no commit)'
}
