[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot 'PackageTransaction.ps1')

$runtime = 'win-x64'
$version = Get-AppVersion
$dist = Assert-ManagedPath (Join-Path $Root 'dist')
$stageRoot = Assert-ManagedPath (Join-Path $RuntimeDir 'staging')
$staging = Join-Path $stageRoot ('package-' + [Guid]::NewGuid().ToString('N'))
$stageDev = Join-Path $staging 'dev'
$stageRelease = Join-Path $staging 'release'
$devFolder = "$AppName`_${version}`_${runtime}-unpack-debug"
$devSource = Join-Path $staging "source/$devFolder"
$devZip = Join-Path $stageDev "$devFolder.zip"
$releaseUnpack = Join-Path $stageRelease 'unpack'
$releaseZip = Join-Path $stageRelease "$AppName`_${version}`_${runtime}-unpack.zip"
$releaseSha = Join-Path $stageRelease 'SHA256SUMS.txt'

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-BuildInfo([string]$Directory, [string]$Mode) {
    $sdk = (& dotnet --version 2>$null | Select-Object -First 1)
    $lines = @(
        "Product: $AppName"
        "Version: $version"
        "Runtime: $runtime"
        "Mode: $Mode"
        "Deployment: framework-dependent"
        "Built: $((Get-Date).ToUniversalTime().ToString('o'))"
        "Git: $(Get-GitSha)"
        ".NET SDK: $sdk"
    )
    Write-Utf8File (Join-Path $Directory 'build-info.txt') (($lines -join "`n") + "`n")
}

function Write-ReadmeText([string]$Directory, [string]$Mode) {
    $lines = @(
        "$AppName $version ($runtime)"
        "=================================="
        ""
        "$Mode portable build: double-click $AppName.exe."
        "Requires Windows 10 / 11 x64 and the matching .NET runtime"
        "(net48: .NET Framework 4.8; net8+: .NET Desktop Runtime)."
        ""
        "Move or copy the WHOLE folder; do not move the exe by itself."
        ""
        "Capture: Ctrl+Alt+A, drag a region, Enter to copy."
        "Pick annotations from toolbar groups, or use More > Edit."
        "Click the size badge for precise coordinates and dimensions."
        "C copies color; Alt+C copies coordinates."
        "Pin clipboard image: Ctrl+Alt+P."
        "Record: Ctrl+Alt+R. During recording this toggles pause."
        "Drag the recording grip; red Stop saves MP4/GIF/WebP."
        "Global hotkeys can be changed in Settings; conflicts are shown."
        "Interface language: Settings > System > Language. Save to apply."
        "Simplified Chinese, English and system language are supported."
        "Close hides the console by default. Use Exit to quit normally."
        "Data: %LOCALAPPDATA%\DeskSnip (separate from this folder)."
        "Local OCR uses installed Windows languages. Long capture is manual."
        "Audio, WebP, editing and recovery require your FFmpeg/ffprobe."
        "Configure FFmpeg in Settings or PATH; it is not bundled."
        "Online recognition uses a configurable JSON service and a local key."
        "Set it up in Settings; requests are sent only on explicit clicks."
        "Pins restore their independent image/layout data at startup."
        "Requires Windows media components for MP4."
        "See README.md for the full usage guide and verification limits."
    )
    Write-Utf8File (Join-Path $Directory 'README.txt') (($lines -join "`n") + "`n")
}

function Copy-PackageDocs([string]$Directory) {
    foreach ($name in @('README.md', 'AGENTS.md', 'ARCHITECTURE.md', 'ROADMAP.md', 'CHANGELOG.md')) {
        Copy-Item -LiteralPath (Join-Path $Root $name) -Destination (Join-Path $Directory $name)
    }
    Copy-Item -LiteralPath (Join-Path $Root 'docs') -Destination $Directory -Recurse
    Copy-Item -LiteralPath (Join-Path $Root 'arch') -Destination $Directory -Recurse
    Copy-Item -LiteralPath (Join-Path $Root 'licenses') -Destination $Directory -Recurse
}

function New-Zip([string]$Source, [string]$ZipPath, [string]$Prefix) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $base = [IO.Path]::GetFullPath($Source).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $Source -Recurse -File) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = if ([string]::IsNullOrEmpty($Prefix)) { $relative } else { "$Prefix/$relative" }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
        }
    }
    finally { $archive.Dispose() }
}

function Write-Manifest([string]$Directory) {
    $manifestPath = Join-Path $Directory 'unpack-manifest.json'
    $base = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $manifestFull = [IO.Path]::GetFullPath($manifestPath)
    $files = @(Get-ChildItem -LiteralPath $Directory -Recurse -File |
        Where-Object { -not [string]::Equals($_.FullName, $manifestFull, [StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName)
    $entries = @(foreach ($file in $files) {
        [ordered]@{
            path = $file.FullName.Substring($base.Length).Replace('\', '/')
            size = $file.Length
            sha256 = Get-Sha256 $file.FullName
        }
    })
    $manifest = [ordered]@{
        format = 1
        product = $AppName
        version = $version
        mode = 'Release'
        platform = $runtime
        createdAt = (Get-Date).ToUniversalTime().ToString('o')
        files = $entries
    }
    $json = $manifest | ConvertTo-Json -Depth 5
    Write-Utf8File $manifestPath ($json + "`n")
}

function Test-PackageContents([string]$Directory, [bool]$AllowSymbols) {
    foreach ($required in @("$AppName.exe", "$AppName.Core.dll", 'build-info.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $required))) {
            throw "Package incomplete, missing: $required"
        }
    }
    $base = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $forbidden = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force -File |
        Where-Object { $_.FullName.Substring($base.Length).Replace('\', '/') -match '(^|/)(\.env|node_modules|\.git|\.runtime|credentials|recording-drafts|history|pins)(/|$)|(^|/)(settings|history|pins|captures|last-session)\.json$|(^|/)recognition\.dat$' })
    if ($forbidden.Count -gt 0) { throw "Disallowed package content: $($forbidden[0].Name)" }
    if (-not $AllowSymbols) {
        $symbols = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Filter '*.pdb')
        if ($symbols.Count -gt 0) { throw "Release package contains symbols: $($symbols[0].Name)" }
    }
}

$lock = $null
try {
    New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
    $lockPath = Join-Path $stageRoot 'package.lock'
    try { $lock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None') }
    catch { throw "Another packaging run holds $lockPath. Wait for it to finish." }

    New-Item -ItemType Directory -Force -Path $devSource, $releaseUnpack, $stageDev | Out-Null

    Write-Step '[1/6] Debug publish'
    Invoke-Tool 'dotnet' @('publish', $DesktopProj, '-c', 'Debug', '-o', $devSource, '--nologo')
    Write-BuildInfo $devSource 'Debug'
    Write-ReadmeText $devSource 'Debug'
    Copy-PackageDocs $devSource
    Test-PackageContents $devSource $true

    Write-Step '[2/6] Release publish'
    Invoke-Tool 'dotnet' @('publish', $DesktopProj, '-c', 'Release', '-o', $releaseUnpack, '--nologo')
    Get-ChildItem -LiteralPath $releaseUnpack -Recurse -File -Filter '*.pdb' | Remove-Item -Force
    Write-BuildInfo $releaseUnpack 'Release'
    Write-ReadmeText $releaseUnpack 'Release'
    Copy-PackageDocs $releaseUnpack
    Test-PackageContents $releaseUnpack $false

    Write-Step '[3/6] Manifest'
    Write-Manifest $releaseUnpack

    Write-Step '[4/6] Zip'
    New-Zip $devSource $devZip $devFolder
    New-Zip $releaseUnpack $releaseZip ''
    Test-PackageZip $devSource $devZip $devFolder
    Test-PackageZip $releaseUnpack $releaseZip ''

    Write-Step '[5/6] SHA256'
    @(
        "$(Get-Sha256 $releaseZip) *$(Split-Path -Leaf $releaseZip)"
        "$(Get-Sha256 $devZip) *$(Split-Path -Leaf $devZip)"
    ) | Set-Content -LiteralPath $releaseSha -Encoding ASCII

    Write-Step '[6/6] Publish'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    Publish-PackageSet $Root $staging $dist
    Remove-ManagedPath $staging

    Write-Ok "Packaged $AppName $version"
    Write-Info "  dev     : $(Join-Path $dist ("dev/" + (Split-Path -Leaf $devZip)))"
    Write-Info "  unpack  : $(Join-Path $dist 'release/unpack')"
    Write-Info "  release : $(Join-Path $dist ("release/" + (Split-Path -Leaf $releaseZip)))"
    Write-Info "  sha256  : $(Join-Path $dist 'release/SHA256SUMS.txt')"
}
catch {
    Write-Host "[fail] Packaging failed; staging kept for inspection: $staging" -ForegroundColor Red
    throw
}
finally {
    if ($lock) { $lock.Dispose() }
}
