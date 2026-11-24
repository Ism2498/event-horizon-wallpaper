# Event Horizon Wallpaper

A **~115 KB** native Windows 11/10 GPU-accelerated video wallpaper engine and a collection of interactive pixel-art space studios (TON 618, Sagittarius A\*, Quasar, Saturn, Earth & Moon).

Built in plain C# using **D3D11 + Media Foundation** hardware decoding and compiled with the `csc.exe` already built into Windows — no Electron, no Chromium, no mpv, no external runtimes.

## Features

- **Tiny & Zero-Dependency:** Compiles directly to a single ~115 KB `VideoWallpaper.exe` with Windows' built-in `.NET` compiler.
- **Hardware GPU Decoding:** Uses D3D11 + Media Foundation (`mfplat.dll`, `mfreadwrite.dll`) slotted behind desktop icons (including full Windows 11 24H2+ `Progman`/`WorkerW` layered-child support).
- **Auto-Pause for Performance:** Automatically pauses decoding (near-0% CPU/GPU) when a fullscreen or maximized app is active, and when your laptop switches to battery power.
- **Multi-Monitor Support:** Switch between **Duplicate** (per-monitor), **Span** (across virtual desktop), or **Primary** monitor only, with live display hot-plug handling.
- **Scaling & Playback Speed:** **Fill** (aspect-preserving crop), **Fit** (letterboxed), or **Stretch**, plus **0.5x / 1x / 1.5x / 2x** playback speed.
- **Use Any Video:** Drag and drop any `.mp4`, `.m4v`, `.mov`, or `.wmv` file or folder straight onto the pixel-art Control Panel, or click `+ ADD`.
- **Star-Streak Warp Transitions:** Switching wallpapers extracts bright stars from the departing frame and streaks them across deep space while smoothly loading the next scene.
- **Browser Studios Included:** 5 standalone HTML5 Canvas + WebCodecs studios to customize tilt, zoom, pixel size, and export seamless 1080p/1440p/4K 60fps MP4 loops right in Chrome or Edge.

## Quick Start

### Option 1: Download from GitHub Releases
1. Download `EventHorizonWallpaper-win-x64.zip` and the optional pre-rendered 1080p 60fps `.mp4` pack from **Releases**.
2. Unzip them into the same folder (or put the `.mp4` files in a `wallpapers/` subfolder).
3. Double-click `Start Video Wallpaper.cmd` (or run `VideoWallpaper.exe`).
4. Left-click the tray icon to open the pixel-art Control Panel; right-click for the quick menu.

### Option 2: Build from Source (Windows)
No Visual Studio or SDK required — uses the C# compiler built into Windows:

```powershell
# Compile VideoWallpaper.exe
powershell -ExecutionPolicy Bypass -File .\video-wallpaper.ps1 -BuildOnly

# Run with a specific video (or auto-detect MP4s in this folder / wallpapers/)
powershell -ExecutionPolicy Bypass -File .\video-wallpaper.ps1 -Video .\my-loop.mp4

# Start automatically at Windows sign-in
powershell -ExecutionPolicy Bypass -File .\video-wallpaper.ps1 -AddToStartup

# Remove from startup
powershell -ExecutionPolicy Bypass -File .\video-wallpaper.ps1 -RemoveFromStartup
```

## Browser Studios (Generate Your Own Loops)

Open any of these locally (or via the GitHub Pages site) in Chrome or Edge to tweak parameters and export seamless MP4s at 1080p, 1440p, or 4K:

- `event-horizon-closeup.html` — TON 618 supermassive black hole close-up
- `sagittarius-a.html` — Sagittarius A\* galactic center
- `quasar.html` — Relativistic jet quasar
- `saturn.html` — Pixel-art Saturn & rings
- `earth-moon.html` — Earth & Moon orbital loop
- `transition-demo.html` — Live preview of the star-streak warp transition

## Settings & Storage

Preferences (pinned wallpaper, rotation interval, scale mode, monitor mode, playback speed, auto-pause rules, and custom video folders) are saved automatically to:

```text
%APPDATA%\VideoWallpaper\settings.txt
```

## License

- Code: [MIT License](LICENSE)
- Font: `Silkscreen` (`Silkscreen-Regular.ttf`, `Silkscreen-Bold.ttf`) is licensed under the SIL Open Font License 1.1 (`Silkscreen-OFL.txt`).
