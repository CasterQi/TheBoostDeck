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

## PawnIO (bundled signed installer)

PawnIO 2.2.0 or later is an optional kernel driver used by LibreHardwareMonitor to expose CPU temperature, per-core clock, and package power on supported systems. TheBoostDeck redistributes the unmodified official signed installer `PawnIO_setup.exe` (2.2.0) alongside the application.

- The application verifies the installer's SHA-256 (`1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032`) before execution.
- When PawnIO is missing, the application launches the installer with `-install -silent` before initializing the hardware collector. The installer is still elevated through the Windows UAC prompt; TheBoostDeck does not bypass, disable, or automate UAC.
- The installer is unmodified. TheBoostDeck does not link PawnIO source into its own binaries and communicates with the driver only through the documented device/IOCTL interface.
- Project: https://github.com/namazso/PawnIO
- Installer source: https://github.com/namazso/PawnIO.Setup
- Distribution: https://pawnio.eu/
- License: GNU General Public License version 2 or later, with a device-driver linking exception (see the project's `COPYING` and `README.md`). The full GPL-2.0 text is included at `licenses/PawnIO-GPL-2.0.txt`; the PawnIO notice and exception summary are at `licenses/PawnIO-NOTICE.txt`.

The bundled installer is an official, unmodified PawnIO build. Users who redistribute TheBoostDeck must preserve the PawnIO notice and license, and comply with PawnIO's GPL-2.0-or-later terms, including the corresponding-source requirements for PawnIO and the installer.

## Other notices

No third-party source file has been modified by TheBoostDeck. If future changes modify MPL-covered source files, those modified files must remain available under MPL-2.0 as required by that license.
