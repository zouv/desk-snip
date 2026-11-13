[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('start', 'stop', 'restart', 'status')][string]$Command = 'status',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$StopTimeoutSeconds = 30
)
. (Join-Path $PSScriptRoot '_common.ps1')

function Start-App {
    $process = Get-ManagedProcess
    if ($process) { Write-Info 'Already running.'; Show-Status; return }
    $exe = Get-ExePath $Configuration
    if (-not $exe) {
        Write-Info "No $Configuration build found; building first."
        & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
        $exe = Get-ExePath $Configuration
        if (-not $exe) { throw 'Build produced no exe' }
    }
    # This is the interactive product window, not a background helper.
    $started = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) -WindowStyle Normal -PassThru
    Start-Sleep -Milliseconds 1200
    if ($started.HasExited) {
        throw "The app exited immediately (code $($started.ExitCode))."
    }
    Save-RunRecord $started $exe $Configuration
    Write-Ok "Started $AppName (pid $($started.Id), $Configuration)."
}

function Stop-App {
    $process = Get-ManagedProcess
    if (-not $process) {
        Write-Info 'Not running.'
        Remove-Item -LiteralPath $RunRecord -Force -ErrorAction SilentlyContinue
        return
    }
    Write-Info "Asked the app to exit; waiting up to ${StopTimeoutSeconds}s (never force-killed)."
    $signal = $null
    try {
        $signal = [Threading.EventWaitHandle]::OpenExisting('Local\DeskSnip.Stop.' + $process.Id)
        [void]$signal.Set()
    }
    catch [Threading.WaitHandleCannotBeOpenedException] {
        [void]$process.CloseMainWindow()
    }
    finally { if ($signal) { $signal.Dispose() } }
    $deadline = (Get-Date).AddSeconds($StopTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) {
            Remove-Item -LiteralPath $RunRecord -Force -ErrorAction SilentlyContinue
            Write-Ok 'Stopped.'
            return
        }
        Start-Sleep -Milliseconds 300
    }
    Write-Host "[fail] $AppName is still running after ${StopTimeoutSeconds}s. This script never force-kills." -ForegroundColor Red
    Write-Host '       Close it normally, then run stop again.' -ForegroundColor Red
    exit 1
}

function Show-Status {
    $process = Get-ManagedProcess
    if ($process) {
        $saved = Get-Content -LiteralPath $RunRecord -Raw | ConvertFrom-Json
        Write-Ok "Running: pid $($process.Id), $($saved.configuration), since $($saved.startedAt)"
        Write-Info "Exe: $($saved.path)"
    } else {
        Write-Info 'Not running.'
    }
}

switch ($Command) {
    'start'   { Start-App }
    'stop'    { Stop-App }
    'restart' { Stop-App; Start-App }
    'status'  { Show-Status }
}
