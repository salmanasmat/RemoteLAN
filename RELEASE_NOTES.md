# Release Notes

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
