# Third-party notices

TheBoostDeck uses third-party components. Their licenses remain with their respective owners. The complete package-version inventory is generated in `docs/dependency-inventory.md`.

## LibreHardwareMonitor and sensor toolkits

- LibreHardwareMonitorLib 0.9.6 — MPL-2.0.
- BlackSharp.Core 1.0.7 — MPL-2.0.
- DiskInfoToolkit 1.1.2 — MPL-2.0.
- RAMSPDToolkit-NDD 1.4.2 — MPL-2.0.

The MPL-2.0 text is included at `licenses/MPL-2.0.txt`. TheBoostDeck references these packages without modifying their source files. Source projects:

- https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- https://github.com/Blacktempel/BlackSharp
- https://github.com/Blacktempel/DiskInfoToolkit
- https://github.com/Blacktempel/RAMSPDToolkit

LibreHardwareMonitorLib 0.9.6 package metadata identifies source commit `3d331e3370efb858411f19511373eff65a218701`.

## Microsoft Web.WebView2

Microsoft.Web.WebView2 1.0.2903.40 is distributed under the terms in:

- `licenses/WebView2-LICENSE.txt`
- `licenses/WebView2-NOTICE.txt`

The package contains the WebView2 .NET loader and related build assets. The Microsoft Edge WebView2 Runtime is a separate system runtime and is not bundled by this project.

## HidSharp

HidSharp 2.6.4 is Apache-2.0 licensed. Its copyright and complete license are preserved in `licenses/HidSharp-LICENSE.txt`.

## .NET and Microsoft system packages

The self-contained .NET 8 runtime, Microsoft.NET.ILLink.Tasks, System.CodeDom, System.IO.Ports, System.Management, System.Threading.AccessControl, and related runtime packages declare MIT licensing. The MIT text is included at `licenses/NET-LICENSE.txt`.

## Mono.Posix.NETStandard

Mono.Posix.NETStandard 1.0.0 declares its license and project through Microsoft package links recorded in `licenses/Mono.Posix-LICENSE-URL.txt`.

## PawnIO (external runtime dependency, not bundled)

PawnIO 2.2.0 or later is an optional, separately installed kernel driver used by LibreHardwareMonitor to expose CPU temperature, per-core clock, and package power on supported systems. TheBoostDeck does not bundle, modify, or redistribute the driver or its installer.

- Project: https://github.com/namazso/PawnIO
- Distribution and signed installer: https://pawnio.eu/
- License: GNU General Public License version 2 or later, with a device-driver linking exception (see the project's `COPYING` and `README.md`).
- TheBoostDeck communicates with PawnIO only through the documented device/IOCTL interface; no PawnIO source is linked into TheBoostDeck.

Users who redistribute PawnIO themselves must comply with its GPL-2.0-or-later terms, including providing the license text and corresponding source.

## Other notices

No third-party source file has been modified by TheBoostDeck. If future changes modify MPL-covered source files, those modified files must remain available under MPL-2.0 as required by that license.
