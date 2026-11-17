# TheBoostDeck

**English** | [简体中文](README.zh-CN.md)

TheBoostDeck is a Windows desktop performance monitor designed for a gamer's secondary display. It combines a WPF host, a WebView2 dashboard, and LibreHardwareMonitor sensors in a ready-to-use, responsive application.

![TheBoostDeck dashboard](docs/screenshots/TheBoostDeck-window-controls.jpg)

## Highlights

- Real-time CPU, GPU, memory, dedicated VRAM, fixed-disk, network, and available motherboard sensors, including CPU fan RPM when exposed.
- Responsive 16:9 and 9:16 layouts with automatic card reflow during resize or orientation changes.
- Resizable always-on-top window, screen selection, tray hide/show, and persistent placement.
- Frameless HUD header with language, pin, hide-to-tray, maximize/restore, and close controls; maximize respects the current monitor work area and taskbar.
- Per-monitor DPI awareness and physical GPU/network filtering.
- One shared sampling scheduler with a tray-selectable 0.1–5 s cadence; snapshots are batched into one process-local message per tick.
- Sampling and chart rendering pause when the window is hidden.

## Requirements

- 64-bit Windows 10 version 1809 or later, or Windows 11.
- Microsoft Edge WebView2 Runtime. It is preinstalled on many Windows 10/11 systems.
- [PawnIO](https://pawnio.eu/) 2.2.0 or later for CPU temperature, per-core frequency, and package power on systems where LibreHardwareMonitor uses the PawnIO kernel driver. The application still starts without it, but those CPU metrics degrade to `--` and the dashboard reports the missing dependency.
- The published application is self-contained and does not require a separate .NET Desktop Runtime installation.

## Run

1. Install PawnIO 2.2.0 or later if CPU temperature, frequency, or package power is important. Download it from <https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe> or <https://pawnio.eu/>, verify the publisher, and approve the UAC prompt. The application detects the driver and shows a warning when it is missing or older than 2.2.0.
2. Start `TheBoostDeck.exe`.
3. The window opens on the Windows primary display by default. Use the tray menu to move it to another monitor.
4. Use the pin button or tray menu to toggle always-on-top.
5. Use the tray menu to move the window to another screen.
6. Minimizing or closing the window through the Windows shell hides it to the tray and pauses sampling. Double-click the tray icon or choose **显示 / 隐藏** to restore it.
7. Use the header close button or choose **退出** in the tray menu to exit completely.

The first release is not code-signed, so Windows SmartScreen may show a warning.

## PawnIO driver

TheBoostDeck does not silently install a kernel driver. It detects PawnIO at startup by reading its uninstall registration and warns in the dashboard when the driver is absent, unreadable, or older than 2.2.0. Install it yourself with the official signed installer; the installer supports `-install -silent` after UAC consent.

```powershell
.\PawnIO_setup.exe -install -silent
```

The tested 2.2.0 installer had a valid Authenticode signature and SHA-256 `1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`. PawnIO is an independent GPL-2.0-or-later project with a device-driver linking exception; it is not part of TheBoostDeck's Apache-2.0 source and is not bundled in this release. See `THIRD-PARTY-NOTICES.md`.

## Custom theme

The first launch creates:

```text
%LocalAppData%\TheBoostDeck\themes\custom.css
```

Override CSS variables from `:root` in `src/TheBoostDeck.App/Web/dashboard.html`, then restart the application. The default file contains a commented example. No theme download or online theme store is used.

## Build from source

Install the .NET 8 SDK and WebView2 Runtime, then run from the repository root:

```powershell
dotnet restore .\TheBoostDeck.sln
dotnet build .\TheBoostDeck.sln --configuration Debug
dotnet test .\tests\TheBoostDeck.Tests\TheBoostDeck.Tests.csproj --configuration Debug
```

Publish a self-contained Windows x64 single-file build:

```powershell
dotnet publish .\src\TheBoostDeck.App\TheBoostDeck.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false `
  --output .\artifacts\publish\TheBoostDeck
```

The dashboard HTML is embedded in the executable and extracted under `%LocalAppData%\TheBoostDeck\Web` at startup. Node.js is not required by the application; it is only used by the optional WebView2 capture script in `tools/`.

## Project layout

```text
src/TheBoostDeck.Core             Telemetry contracts, bounded buffer, sampling service
src/TheBoostDeck.Infrastructure   LibreHardwareMonitor and Windows data collection
src/TheBoostDeck.App              WPF host, WebView2 bridge, tray, settings, dashboard
tests/TheBoostDeck.Tests          Unit and serialization tests
tools/TheBoostDeck.HardwareProbe  Hardware diagnostics helper
tools/capture-webview.mjs         Optional responsive capture utility
docs/                             Design notes, screenshots, and verification records
```

## Verification and known limits

- All automated tests pass.
- Real WebView2 data and interactions were inspected through a loopback-only debugging session.
- Responsive captures cover 1280×720, 720×1280, and 1920×1080 with no horizontal overflow or JavaScript runtime errors.
- Topmost toggle, hide behavior, single-instance rejection, and persistent screen placement were exercised.
- Long-duration soak testing, game-frame-rate impact testing, sleep/resume, broad hardware compatibility, and a true standard-user versus administrator comparison remain release-validation work.
- FPS, frame time, game detection, in-game OSD, remote browser access, automatic startup, automatic updates, and hardware control are not included in this version.

## Privacy

TheBoostDeck does not upload telemetry. Settings, logs, extracted dashboard files, WebView2 data, and optional themes are stored locally under `%LocalAppData%\TheBoostDeck`.

## License

Project-owned code is licensed under Apache-2.0. See `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `licenses/` for dependency terms.
