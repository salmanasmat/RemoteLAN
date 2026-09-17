# Release Notes

## [1.1.6] - 2026-09-17

RemoteLAN **v1.1.6** eliminates phantom key typing and mouse movements ("the 'A' bug"), isolates input pipeline test suites from the host operating system, and hardens remote desktop viewport focus.

### 🛡️ Fixed & Hardened
- **Eliminated Phantom "A" Key & Mouse Movement**: Discovered that four automated input pipeline tests directly invoked Win32 `NativeMethods.SendInput` on the host workstation during test execution (`dotnet test`), injecting raw `'a'` keystrokes, Ctrl/Shift/Alt modifiers, and cursor snaps into active windows. Added a test isolation delegate hook in `InputInjector` and `DesktopManager` so all tests run against safe in-memory sinks without any OS desktop interference.
- **Session Viewport Keyboard Focus Hardening**: Set `Focusable="False"` across all session toolbar buttons (`Actions`, `Fullscreen`, `Disconnect`), ensuring button clicks and menu interactions never steal keyboard focus from the remote viewport. Keystroke preview handlers are now routed at the Window level with a dedicated guard for the local OS password modal.
- **Automatic Focus Restoration**: Added focus detection in `ScreenViewport_MouseMove` to instantly restore keyboard focus to the remote desktop whenever the user moves the mouse back into the viewport.

## [1.1.5] - 2026-09-17

RemoteLAN **v1.1.5** delivers an immersive borderless fullscreen experience with an auto-hiding peek toolbar, a unified Actions control center, and refined post-installation startup handling.

### 🚀 New Features
- **True Fullscreen with Auto-Hiding Peek Toolbar**: Fullscreen mode automatically hides both the top control bar and bottom status bar for a distraction-free display. Hovering the cursor near the top edge smoothly peeks the floating toolbar without interrupting or resizing the remote video stream.
- **Consolidated Actions Control Center**: Streamlined the session toolbar by replacing cluttered buttons with a unified `⚡ Actions ▾` menu grouping Send Remote Input toggle, Windows OS Login, Ctrl+Alt+Del, and Remote Power actions (Lock, Sleep, Restart, Shutdown).

### ⚡ Improvements
- **Installer Launch by Default**: The "Launch RemoteLAN" option is now checked by default on the installer completion screen.
- **Single-Instance Clean Launch**: Resolved a post-installation startup race condition using Inno Setup mutex verification, ensuring the main window stays open and focused when launched after installation.


## [1.1.4] - 2026-09-17

### Fixed
- **OS Auto-Password Premature Submission**: Fixed an issue where the lock screen wake sequence sent premature Enter keys, causing the password box to submit a single character before the full password was typed.
- **Physical Keystroke Scan Code Fidelity**: Integrated VkKeyScan and hardware scan code translation (MapVirtualKey) with automatic Shift/Ctrl/Alt modifier handling, replacing KEYEVENTF_UNICODE which was ignored by the Windows DirectUI/XAML LogonUI credential provider.
- **Safe Lock Screen Wake Routine**: Replaced Space/Return wake keys with non-printable navigation keys (VK_ESCAPE and VK_UP) to smoothly slide the lock screen curtain open without typing characters into focused inputs.
- **Input Pipeline Stability**: Verified input tracking and mouse throttling to prevent dropped keystrokes, key repeats, or worker thread stalls.


## [1.1.3] - 2026-09-17

### Fixed
- Restored original UI iconography that was inadvertently corrupted by build tooling.

## [1.1.2] - 2026-09-17

### Fixed
- Fixed an issue where the OS password injection and general remote lock screen input were not functioning due to User Interface Privilege Isolation (UIPI) and Secure Desktop restrictions. RemoteLAN now seamlessly elevates to a SYSTEM process within the interactive session on launch, fully unlocking lock screen integration and interactions.
- Improved the grace period for discovered devices in the network list before they are marked as offline, minimizing UI flicker when UDP discovery packets are delayed or lost.


## [1.1.3] - 2026-09-17

RemoteLAN **v1.1.3** introduces automated Windows OS password unlocking for remote sign-in screens, eliminates remote lock screen input freezes, and preserves discovered network devices in persistent history with live online/offline status indicators.

### 🚀 New Features
- **Windows OS Password Auto-Unlock (`0x37`)**: Store Windows OS credentials per remote device and automatically unlock the remote sign-in screen upon connection or via the session toolbar `🔑 OS Login` button.
- **Persistent Device History & Live Status Indicators**: Discovered LAN machines remain permanently in device history across application restarts. Real-time status indicators show vibrant green (`#22C55E`) when active and slate gray (`#94A3B8`) with card dimming and last-seen timestamps when offline.
- **Administrator Elevation Advisory Banner**: Informative banner when running under standard user integrity with a single-click "Restart as Admin" button to enable full SYSTEM desktop switching and remote lock screen interaction.
- **Device History & Credential Management**: 3-dots kebab menu on device cards to configure OS passwords, forget saved PINs or OS credentials, and remove devices from history.

### ⚡ Improvements
- **Instantaneous SYSTEM Desktop Impersonation**: Cached duplicated SYSTEM tokens in `DesktopManager` to eliminate repeated ~50-100ms process search latency per keystroke/mouse move.
- **Lock Screen Transition Pacing & Unicode Key Injection**: Wakes the lock screen wallpaper curtain, waits for LogonUI animation, clears existing inputs, and injects exact Unicode characters (`KEYEVENTF_UNICODE`).
- **Dynamic Thread Desktop Switching**: Resolved thread desktop handle invalidation, ensuring continuous input delivery across `winsta0\Default` and `winsta0\Winlogon`.

### 🐛 Bug Fixes
- **Unclickable/Untypeable Lock Screen Fixed**: Resolved UIPI drops and thread desktop disassociation when interacting with the remote Windows lock/sign-in screen.
- **Duplicate Key Keystroke Drops**: Fixed key tracking in `InputInjector` to prevent rapid duplicate characters (e.g. double letters in passwords) from being dropped.
- **Discovered Devices Disappearing**: Discontinued removing devices that miss a single UDP broadcast; devices now seamlessly transition to offline status and persist in history.

### 🏗️ Infrastructure & Testing
- **Expanded Test Suite (81 Tests)**: Added new unit and regression tests for OS password serialization, settings persistence, history management, and `DiscoveredAgent` status notifications.
- **Self-Contained Inno Setup Installer**: Re-built self-contained installer (`dist/RemoteLAN_Setup_v1.1.3.exe`) bundling the full .NET 8 runtime with clean upgrade routines and Windows startup configuration.

---

## [1.0.0] - 2026-09-16

We are thrilled to announce the official **1.0.0 General Availability** release of **RemoteLAN**! 
RemoteLAN is a high-performance, LAN-only, bidirectional remote desktop solution designed to bring seamless, AnyDesk-style peer-to-peer remote control to local office networks without cloud relays or external dependencies.

### 🚀 New Features
- **Official 1.0.0 Production Release**: Mature, stable architecture featuring bidirectional remote desktop connectivity between any PCs on the local network.
- **Dedicated Settings & Security Suite**: Full settings interface featuring automated PIN rotation (15m to 24h intervals), unattended access toggling, permanent password configuration, and brute-force lockout management.
- **Remote Power Controls**: Dedicated session toolbar menu enabling remote Lock Workstation, Sleep/Suspend, Restart PC, and Shutdown PC with confirmation safeguards.
- **Native Windows Lock Screen Control**: Integrated `DesktopManager` with Winlogon desktop switching and `sas.dll` / hardware key simulation to dismiss lock screens and enter Windows credentials remotely.
- **Continuous LAN Auto-Discovery**: Peer-to-peer UDP broadcast discovery instantly presenting available machines as interactive cards.

### ⚡ Improvements
- **DirectX 11 DXGI Desktop Duplication**: Ultra-low-latency 60+ FPS screen capture with automatic GDI fallback and thread-safe COM lifecycle guards.
- **Natural Viewport Mouse Pointer**: Replaced crosshair cursors with a native arrow pointer and pixel-perfect letterbox/pillarbox coordinate translation across dynamic display resolution changes.
- **Single-Instance Windows Management**: Running background instances smoothly surface to the foreground when launched manually from the Start menu or desktop shortcut.
- **System Tray Minimization**: Seamless background hosting with Windows auto-start (`--background`), minimize to tray on close, and incoming connection alerts.

### 🐛 Bug Fixes
- **Phantom Input & Key Repeat Elimination**: Implemented thread-safe input tracking via `ConcurrentDictionary`, duplicate `KeyDown` suppression, focus-loss key release, and guaranteed session reset cleanup on disconnect to eliminate accidental key repeats or stuck mouse buttons.
- **DirectX Capture Thread Safety**: Synchronized `DxgiScreenCapturer` initialization and disposal to prevent COM memory race conditions (`AccessViolationException`) during rapid session teardowns.
- **Hardware Test Isolation**: Decoupled test suites with `IInputInjector` and `TestInputInjector` to prevent simulated inputs from interfering with the physical developer operating system during test runs.

### 📚 Documentation
- **OWASP Security Audit & Policy (`SECURITY.md`)**: Comprehensive security audit report detailing secrets detection, timing attack mitigation, injection resistance, and privilege management.
- **Updated Architectural Guide (`README.md`)**: Expanded networking specifications, framing format details, quick start commands, and installer documentation.

### 🏗️ Infrastructure & Maintenance
- **Self-Contained Inno Setup Installer**: Production-ready installer bundling the complete .NET 8 desktop runtime, clean version upgrades, and Windows auto-startup configuration.
- **OWASP Security Hardening**: Constant-time PIN and password validation via `CryptographicOperations.FixedTimeEquals` and parameterized execution via `ProcessStartInfo.ArgumentList`.
- **Deterministic Test Suite**: Complete test suite of 73 unit, integration, and regression tests running deterministically with xUnit test parallelization controls.
