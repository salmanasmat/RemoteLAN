# Release Notes

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
