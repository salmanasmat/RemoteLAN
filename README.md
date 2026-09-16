# RemoteLAN

[![Version](https://img.shields.io/badge/version-0.3.0-blue.svg)](https://semver.org)
[![Target](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![UI](https://img.shields.io/badge/UI-Light%20Mode-success.svg)](#)

A high-performance, custom, LAN-only remote desktop tool for internal office use built with **C# / .NET 8 / WPF**, designed to work like **AnyDesk** — every installation can connect out to other PCs and accept incoming connections bidirectionally.

- **AnyDesk-Style Unified App**: Both PC A and PC B install and run the exact same `RemoteLAN` application.
- **Bidirectional Control**: Connect from PC A to PC B, or from PC B to PC A, or simultaneously.
- **Zero Cloud Relay**: Direct TCP socket connections between machines on your trusted local area network.
- **LAN Auto-Discovery**: Automatically discovers running RemoteLAN PCs on your subnet via lightweight UDP broadcast (port `9192`) — select any machine from a dropdown with zero manual IP entry.
- **Fast Screen Streaming**: DXGI Desktop Duplication engine with automatic GDI `BitBlt` fallback, encoded as JPEG at configurable quality and frame rate.
- **Bi-directional Input Control**: Remote mouse movement, clicks, wheel scrolling, and keyboard keystrokes via Win32 `SendInput` with letterbox/pillarbox coordinate scaling.
- **PIN Handshake Security**: Lightweight 6-digit PIN authentication handshake before streaming or control begins.
- **Clean Light Mode Interface**: Modern, crisp light mode UI with "This PC" and "Remote PC" panels.
- **Semantic Versioning**: Adheres strictly to [SemVer 2.0.0](https://semver.org) (Current version: `0.3.0`).

---

## Architecture

```
RemoteLAN/
├── RemoteLAN.slnx                    # Solution file
├── src/
│   ├── RemoteLAN/                    # Primary Unified AnyDesk-style Application (Host + Client Viewport)
│   ├── RemoteLAN.Protocol/           # Shared wire protocol, framing, messages, discovery models
│   ├── RemoteLAN.Agent/              # Standalone agent wrapper
│   └── RemoteLAN.Controller/         # Standalone controller wrapper
└── tests/
    └── RemoteLAN.Tests/              # Automated unit, discovery, and bidirectional test suite (26 tests)
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

### Launching RemoteLAN (Works on Any PC)

Run the unified app on both **PC A** and **PC B**:

```powershell
dotnet run --project src/RemoteLAN/RemoteLAN.csproj
```

#### How it Works:
1. **On PC A**:
   - The left card (**This PC**) displays PC A's IP address and a 6-digit security PIN.
2. **On PC B**:
   - The left card displays PC B's IP address and its own 6-digit security PIN.
3. **To connect PC A → PC B**:
   - On PC A, click **Scan LAN** (or pick PC B from the **Discovered LAN PCs** dropdown).
   - Enter PC B's PIN and click **Connect to Remote PC**.
   - A dedicated remote desktop session window opens with full mouse and keyboard control!
4. **To connect PC B → PC A**:
   - Exact same process in reverse!
