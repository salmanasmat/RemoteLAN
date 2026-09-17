# RemoteLAN

[![Version](https://img.shields.io/badge/version-1.1.3-blue.svg)](https://semver.org)
[![Downloads](https://img.shields.io/github/downloads/salmanasmat/RemoteLAN/total.svg)](https://github.com/salmanasmat/RemoteLAN/releases)
[![Target](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/platform-Windows-0078d4.svg)](https://www.microsoft.com/windows)
[![UI](https://img.shields.io/badge/UI-Light%20Mode-success.svg)](#)

A high-performance, custom, LAN-only remote desktop tool for internal office use built with **C# / .NET 8 / WPF**, designed to work like **AnyDesk** â€” every installation can connect out to other [...]

- **AnyDesk-Style Unified App**: Both PC A and PC B install and run the exact same `RemoteLAN` application.
- **Bidirectional Control**: Connect from PC A to PC B, or from PC B to PC A, or simultaneously.
- **Zero Cloud Relay**: Direct TCP socket connections between machines on your trusted local area network.
- **Continuous Auto-Discovery**: Automatically discovers active RemoteLAN PCs on your subnet in the background and presents them as interactive squared tiles (AnyDesk-style) without requiring manu[...]
- **System Tray Minimization**: Closing the application window (clicking 'X') hides RemoteLAN to the Windows System Tray notification area, keeping the agent server and discovery listening in the [...]
- **Clean Modern Light Mode UI**:
  - **Left Vertical Sidebar**: Displays This PC's identity, IP address, and alphanumeric session access code with one-click copy, regeneration, and custom code assignment, with a quick-access Sett[...]
  - **Top Horizontal Header**: Streamlined remote address input with instant reachability validation before requesting authentication.
  - **Interactive Device Grid**: Discovered LAN machines shown as square cards with machine name, IP, and online badge â€” click to connect.
- **Fast Screen Streaming & Real-Time Lock Screen Control**: High-performance DXGI Desktop Duplication engine with automatic GDI fallback and dynamic desktop switching (`DesktopManager`). Gracefully handles session locks, UAC prompts, and lock screens across session boundaries with thread-safe desktop switching, handle retention, and non-blocking input injection.
- **Windows OS Password Save & Automated Sign-In Screen Unlock**: Save remote Windows user passwords per device. Dedicated `ðŸ”‘ OS Login` toolbar button in the remote viewer and configurable auto-unlock upon connection automatically dismisses the lock screen curtain, paces keystrokes directly into the credential provider, and logs into Windows seamlessly with synchronous Unicode injection and re-entrant SYSTEM impersonation. Full manual mouse clicking and typing on the remote lock/sign-in screen is also supported interactively.
- **Persistent Device History & Live Status Indicators**: Discovered LAN machines remain saved in device history even when powered down or disconnected. Cards feature real-time status indicators (vibrant green `#22C55E` when online, slate gray `#94A3B8` with card dimming and last-seen timestamp when offline). A 3-dots kebab menu allows configuring OS passwords, forgetting saved credentials, and removing entries from history.
- **Administrator Elevation Advisory Banner**: Top notification banner when running under standard user integrity with a single-click "Restart as Admin" option to ensure full Winlogon desktop interaction privileges.
- **Send Ctrl+Alt+Del / Wake Remote Host**: Dedicated one-click "Ctrl+Alt+Del" toolbar button in the remote session viewer to wake the remote lock screen wallpaper, dismiss the lock curtain, and reveal the sign-in prompt.
- **Natural Mouse Pointer & Dynamic Resolution**: Standard mouse arrow pointer in the remote desktop viewport (eliminating awkward `+` crosshair cursors), with live dynamic resolution adaptation a[...]
- **Resilient Input State Management & Bi-directional Input Control**: Remote mouse movement, clicks, wheel scrolling, and keyboard keystrokes via Win32 `SendInput` with letterbox/pillarbox coordi[...]
- **Modern Rounded App Icon & Full Taskbar Integration**: Custom antialiased squircle application icon bundled as multi-resolution `.ico` (16x16 to 256x256) embedded in the Win32 executable, windo[...]
- **Remote Power Actions**: Dedicated `âš¡ Power` dropdown toolbar in the session viewer allowing the controller to execute remote **Lock Workstation**, **Sleep / Suspend**, **Restart PC...**, and[...]
- **Dedicated Settings & Security Interface**: Comprehensive, modern Light Mode settings window accessible via the `âš™ï¸ Settings` button in the left sidebar or system tray context menu:
  - **Security PIN Auto-Rotation**: Configurable automatic generation of a fresh host PIN after a specified time interval (Never by default, 15m, 30m, 1h, 4h, 8h, 24h). Active sessions remain unin[...]
  - **Unattended Access Control**: Toggle unattended access, configure permanent passwords, and reveal/hide password inputs with dynamic card collapsing.
  - **Scrollable & Responsive Content Layout**: Smooth vertical scrolling containers across Security, General, and About tabs ensuring zero card cropping regardless of screen resolution or DPI sca[...]
  - **Unauthorized Access & Brute-Force Protection**: Automatic rate-limiting and temporary IP lockout after repeated failed PIN/password attempts, configurable thresholds, lockout durations, and [...]
  - **General & System Preferences**: Windows auto-startup configuration (registry `Run` key), minimize to tray on close, and startup minimization preferences.
- **About Section & Developer Credentials**: Built-in About view providing project details (v1.1.3, GPL-3.0 open source license, technical architecture) and developer credentials (**Salman Asmat**).
- **Semantic Versioning**: Adheres strictly to [SemVer 2.0.0](https://semver.org) (Current version: `1.1.3`).

---

## Architecture

```
RemoteLAN/
â”œâ”€â”€ RemoteLAN.slnx                    # Solution file
â”œâ”€â”€ src/
â”‚   â”œâ”€â”€ RemoteLAN/                    # Primary Unified AnyDesk-style Application (Host + Client Viewport)
â”‚   â””â”€â”€ RemoteLAN.Protocol/           # Shared wire protocol, framing, messages, discovery models
â””â”€â”€ tests/
    â””â”€â”€ RemoteLAN.Tests/              # Automated unit, discovery, security, power, input pipeline, and lock recovery test suite (81 tests)
```

### Network Protocols

#### 1. LAN Auto-Discovery (UDP Port 9192)
- **Probe**: Broadcasts `REMOTELAN_DISCOVER_V1` across active local network subnets.
- **Response**: Replies with `REMOTELAN_AGENT_V1|{MachineName}|{TcpPort}|{Version}`.

#### 2. Streaming & Control (TCP Port 9191)
All authentication, desktop video frames, and remote mouse/keyboard inputs multiplex over a single TCP connection:

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”¬â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚ MessageType(1B) â”‚ PayloadLength (4B Big-Endian) â”‚ Payload Bytes (Length bytes) â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”´â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”´â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

##### Message Types
- `0x01` â€” `AuthRequest` (PIN verification)
- `0x02` â€” `AuthResponse` (Handshake result & remote screen resolution)
- `0x10` â€” `ScreenFrame` (JPEG-compressed desktop frame)
- `0x20` â€” `MouseMove` (Normalized coordinates `0.0â€“1.0`)
- `0x21` â€” `MouseButton` (Left, Right, Middle / Down, Up)
- `0x22` â€” `MouseWheel` (Wheel scroll delta)
- `0x30` â€” `KeyboardKey` (Virtual Key Code, KeyDown/KeyUp, extended flag)
- `0x35` â€” `SendCtrlAltDel` (Remote CAD / wake sign-in screen command)
- `0x36` â€” `PowerAction` (Remote system Lock, Sleep, Restart, Shutdown)
- `0x37` â€” `UnlockWithOsPassword` (Windows OS lock screen auto-unlock with Unicode password keystroke injection)

---

## Quick Start

### Prerequisites
- [.NET 8.0 SDK or later](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 or Windows Server

### Build

```powershell
# Build entire solution in Release configuration
dotnet build RemoteLAN.slnx -c Release
```

### Run Tests

```powershell
# Execute full test suite including bidirectional and discovery tests
dotnet test tests/RemoteLAN.Tests/RemoteLAN.Tests.csproj --verbosity normal
```

### Create Installer (Inno Setup)

To compile and package the standalone Windows installer:

```powershell
# Execute automated packaging script
.\build-installer.ps1
```

The compiled installer is output to:
`dist/RemoteLAN_Setup_v1.1.3.exe`

- **Fully Self-Contained (.NET 8 Runtime Included)**: Bundles the complete .NET 8 desktop runtime and CoreCLR libraries directly inside the installer â€” no separate .NET installation or download[...]
- **Clean Upgrades**: Automatically terminates running processes, cleans old version binaries, and preserves user credentials in `%LocalAppData%\RemoteLAN`.
- **Post-Install Launch Option**: Includes an unchecked option on the final installer page to launch only if explicitly selected by the user.
- **Automatic Background Startup**: Configures Windows Run key (`--background`) so the host is immediately reachable on boot without displaying the main window.
- **Single-Instance Management**: Prevents duplicate processes; manual launch signals and brings the active background instance to the foreground.
- **System Tray Integration**: Closing the window hides to the notification tray; right-click tray icon to open or exit.
- **Connection Alert**: Main window automatically reveals itself upon incoming remote connection.

### Launching RemoteLAN (Works on Any PC)

Run the unified app on both **PC A** and **PC B**:

```powershell
dotnet run --project src/RemoteLAN/RemoteLAN.csproj
```

#### How it Works:
1. **On PC A**:
   - The left sidebar displays PC A's IP address, an alphanumeric session access code (e.g. `7K2M9X`), and a **Settings** button for unattended access and security configuration.
2. **On PC B**:
   - The left sidebar displays PC B's IP address and its own access credentials.
3. **To connect PC A â†’ PC B**:
   - On PC A, PC B automatically appears as a square device tile in the discovered devices grid.
   - Click PC B's tile (or enter PC B's IP in the top header and click **Connect âž”**).
   - RemoteLAN automatically tests reachability. If reachable, it presents the authentication modal with a **"Remember password for this device"** toggle (or connects instantly if already remembe[...]
   - Enter PC B's session access code or permanent unattended password and click **Connect âž”**.
   - A dedicated remote desktop session window opens with full mouse and keyboard control!
4. **To connect PC B â†’ PC A**:
   - Exact same process in reverse!

---

## Security

RemoteLAN adheres to strict security standards including constant-time authentication verification (`CryptographicOperations.FixedTimeEquals`), automated brute-force IP lockouts, command-injectio[...]
