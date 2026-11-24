# Sets up and builds Event Horizon Video Wallpaper.
# Compiles VideoWallpaper.exe using the C# compiler built into Windows (no third-party SDK needed).
#
#   .\video-wallpaper.ps1                     start with the newest video in this folder / wallpapers / Downloads
#   .\video-wallpaper.ps1 -Video path.mp4     start a specific file
#   .\video-wallpaper.ps1 -AddToStartup       also start automatically when you sign in
#   .\video-wallpaper.ps1 -RemoveFromStartup  stop starting at sign-in
#   .\video-wallpaper.ps1 -Build              rebuild VideoWallpaper.exe
#   .\video-wallpaper.ps1 -BuildOnly          rebuild VideoWallpaper.exe without launching it

param(
    [string]$Video,
    [switch]$AddToStartup,
    [switch]$RemoveFromStartup,
    [switch]$Build,
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'VideoWallpaper.exe'
$startupLink = Join-Path ([Environment]::GetFolderPath('Startup')) 'Event Horizon Wallpaper.lnk'

if ($RemoveFromStartup) {
    if (Test-Path $startupLink) { Remove-Item $startupLink }
    'Event Horizon Wallpaper will no longer start at sign-in.'
    return
}

if ($Build -or $BuildOnly -or -not (Test-Path $exe)) {
    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    & $csc /nologo /target:winexe /optimize+ /platform:x64 `
        "/win32manifest:$PSScriptRoot\VideoWallpaper.manifest" `
        "/win32icon:$PSScriptRoot\VideoWallpaper.ico" `
        "/out:$exe" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
        "$PSScriptRoot\VideoWallpaper.cs" "$PSScriptRoot\ControlPanel.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Building VideoWallpaper.exe failed.' }
    Write-Host "Built $exe ($([math]::Round((Get-Item $exe).Length / 1KB, 1)) KB)"
    if ($BuildOnly) { return }
}

if (-not $Video) {
    $searchDirs = @($PSScriptRoot, (Join-Path $PSScriptRoot 'wallpapers'), "$env:USERPROFILE\Downloads") | Where-Object { Test-Path $_ }
    $Video = Get-ChildItem $searchDirs -Filter '*.mp4' -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -ne "$env:USERPROFILE\Downloads" -or $_.Name -match '^(event-horizon|saturn|earth-moon|quasar|sagittarius)' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Video -or -not (Test-Path $Video)) {
    throw 'No video found. Download the wallpaper pack from GitHub Releases, export an MP4 from a studio HTML page, or pass -Video <path>.'
}
$Video = (Resolve-Path $Video).Path

$still = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue).WallPaper

if ($AddToStartup) {
    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut($startupLink)
    $lnk.TargetPath = $exe
    $lnk.Arguments = "`"$Video`" `"$still`""
    $lnk.WorkingDirectory = $PSScriptRoot
    $lnk.Save()
    Write-Host "Added to Windows Startup: $startupLink"
}

if (-not (Get-Process VideoWallpaper -ErrorAction SilentlyContinue)) {
    Start-Process $exe -ArgumentList "`"$Video`"", "`"$still`""
}
