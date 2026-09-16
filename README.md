# RemoteLAN

[![Version](https://img.shields.io/badge/version-0.4.0-blue.svg)](https://semver.org)
[![Target](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![UI](https://img.shields.io/badge/UI-Light%20Mode-success.svg)](#)

A high-performance, custom, LAN-only remote desktop tool for internal office use built with **C# / .NET 8 / WPF**, designed to work like **AnyDesk** — every installation can connect out to other PCs and accept incoming connections bidirectionally.

- **AnyDesk-Style Unified App**: Both PC A and PC B install and run the exact same `RemoteLAN` application.
- **Bidirectional Control**: Connect from PC A to PC B, or from PC B to PC A, or simultaneously.
- **Zero Cloud Relay**: Direct TCP socket connections between machines on your trusted local area network.
- **Continuous Auto-Discovery**: Automatically discovers active RemoteLAN PCs on your subnet in the background and presents them as interactive squared tiles (AnyDesk-style) without requiring manual scanning.
- **Clean Modern Light Mode UI**:
  - **Left Vertical Sidebar**: Displays This PC's identity, IP address, and alphanumeric session access code with one-click copy, regeneration, and custom code assignment.
  - **Unattended Access Mode**: Configure a permanent custom password for unattended access without requiring on-screen confirmation.
  - **Top Horizontal Header**: Streamlined remote address input with instant reachability validation before requesting authentication.
  - **Interactive Device Grid**: Discovered LAN machines shown as square cards with machine name, IP, and online badge — click to connect.
- **Fast Screen Streaming**: DXGI Desktop Duplication engine with automatic GDI `BitBlt` fallback, encoded as JPEG at configurable quality and frame rate.
- **Bi-directional Input Control**: Remote mouse movement, clicks, wheel scrolling, and keyboard keystrokes via Win32 `SendInput` with letterbox/pillarbox coordinate scaling.
- **Modern Rounded App Icon & Full Taskbar Integration**: Custom antialiased squircle application icon bundled as multi-resolution `.ico` (16x16 to 256x256) embedded in the Win32 executable, window titlebars, Windows taskbar, and in-app header branding.
- **Alphanumeric Credentials & Unattended Access**: High-entropy 6-character alphanumeric access codes (letters and digits), permanent unattended password support, and client-side credential persistence ("Remember password for this device") for 1-click instant connection.
- **Semantic Versioning**: Adheres strictly to [SemVer 2.0.0](https://semver.org) (Current version: `0.4.0`).

---

## Architecture

```
RemoteLAN/
├── RemoteLAN.slnx                    # Solution file
├── src/
│   ├── RemoteLAN/                    # Primary Unified AnyDesk-style Application (Host + Client Viewport)
│   └── RemoteLAN.Protocol/           # Shared wire protocol, framing, messages, discovery models
└── tests/
    └── RemoteLAN.Tests/              # Automated unit, discovery, and bidirectional test suite (35 tests)
```

### Network Protocols

#### 1. LAN Auto-Discovery (UDP Port 9192)
- **Probe**: Broadcasts `REMOTELAN_DISCOVER_V1` across active local network subnets.
- **Response**: Replies with `REMOTELAN_AGENT_V1|{MachineName}|{TcpPort}|{Version}`.

#### 2. Streaming & Control (TCP Port 9191)
All authentication, desktop video frames, and remote mouse/keyboard inputs multiplex over a single TCP connection:

```
┌─────────────────┬───────────────────────────────┬───────────────────────────────┐
│ MessageType(1B) │ PayloadLength (4B Big-Endian) │ Payload Bytes (Length bytes)  │
└─────────────────┴───────────────────────────────┴───────────────────────────────┘
```

##### Message Types
- `0x01` — `AuthRequest` (PIN verification)
- `0x02` — `AuthResponse` (Handshake result & remote screen resolution)
- `0x10` — `ScreenFrame` (JPEG-compressed desktop frame)
- `0x20` — `MouseMove` (Normalized coordinates `0.0–1.0`)
- `0x21` — `MouseButton` (Left, Right, Middle / Down, Up)
- `0x22` — `MouseWheel` (Wheel scroll delta)
- `0x30` — `KeyboardKey` (Virtual Key Code, KeyDown/KeyUp, extended flag)

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
`dist/RemoteLAN_Setup_v0.4.0.exe`

- **Clean Upgrades**: Automatically terminates running processes, cleans old version binaries, and preserves user credentials in `%LocalAppData%\RemoteLAN`.
- **Automatic Background Startup**: Configures Windows Run key (`--background`) so the host is immediately reachable on boot without displaying the main window.
- **Single-Instance Management**: Prevents duplicate processes; manual launch signals and brings the active background instance to the foreground.
- **Connection Alert**: Main window automatically reveals itself upon incoming remote connection.

### Launching RemoteLAN (Works on Any PC)

Run the unified app on both **PC A** and **PC B**:

```powershell
dotnet run --project src/RemoteLAN/RemoteLAN.csproj
```

#### How it Works:
1. **On PC A**:
   - The left sidebar displays PC A's IP address, an alphanumeric session access code (e.g. `7K2M9X`), and an Unattended Access configuration card.
2. **On PC B**:
   - The left sidebar displays PC B's IP address and its own access credentials.
3. **To connect PC A → PC B**:
   - On PC A, PC B automatically appears as a square device tile in the discovered devices grid.
   - Click PC B's tile (or enter PC B's IP in the top header and click **Connect ➔**).
   - RemoteLAN automatically tests reachability. If reachable, it presents the authentication modal with a **"Remember password for this device"** toggle (or connects instantly if already remembered).
   - Enter PC B's session access code or permanent unattended password and click **Connect ➔**.
   - A dedicated remote desktop session window opens with full mouse and keyboard control!
4. **To connect PC B → PC A**:
   - Exact same process in reverse!
