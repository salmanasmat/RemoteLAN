# RemoteLAN

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](https://semver.org)
[![Target](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![UI](https://img.shields.io/badge/UI-Light%20Mode-success.svg)](#)

A high-performance, custom, LAN-only remote desktop tool for internal office use built with **C# / .NET 8 / WPF**.

- **Zero Cloud Relay**: Direct TCP socket connections between machines on your trusted local area network.
- **Fast Screen Streaming**: DXGI Desktop Duplication engine with automatic GDI `BitBlt` fallback, encoded as JPEG at configurable quality and frame rate.
- **Bi-directional Input Control**: Remote mouse movement, clicks, wheel scrolling, and keyboard keystrokes via Win32 `SendInput` with letterbox/pillarbox coordinate scaling.
- **PIN Handshake Security**: Lightweight 6-digit PIN authentication handshake before streaming or control begins.
- **Clean Light Mode Interface**: Modern, crisp light mode UI for both the Agent and Controller.
- **Semantic Versioning**: Adheres strictly to [SemVer 2.0.0](https://semver.org) (Current version: `0.1.0`).

---

## Architecture

```
RemoteLAN/
├── RemoteLAN.slnx                    # Solution file
├── src/
│   ├── RemoteLAN.Protocol/           # Shared wire protocol, framing, messages, and packet reader/writer
│   ├── RemoteLAN.Agent/              # Target PC app (DXGI/GDI capture, JPEG compression, PIN manager, SendInput)
│   └── RemoteLAN.Controller/         # Client viewer app (WPF viewport, mouse/keyboard forwarding, frame renderer)
└── tests/
    └── RemoteLAN.Tests/              # Unit & End-to-End integration test suite (18 automated tests)
```

### Multiplexed Wire Framing

All data (authentication, video stream, mouse and keyboard inputs) multiplexes over a single TCP socket (default port `9191`):

```
┌─────────────────┬───────────────────────────────┬───────────────────────────────┐
│ MessageType(1B) │ PayloadLength (4B Big-Endian) │ Payload Bytes (Length bytes)  │
└─────────────────┴───────────────────────────────┴───────────────────────────────┘
```

#### Message Types
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
# Execute full unit and end-to-end integration test suite
dotnet test tests/RemoteLAN.Tests/RemoteLAN.Tests.csproj --verbosity normal
```

### Launching RemoteLAN

1. **On Target PC (RemoteLAN Agent)**:
   ```powershell
   dotnet run --project src/RemoteLAN.Agent/RemoteLAN.Agent.csproj
   ```
   - Note the **Local IP Address** and **Security PIN** displayed on the agent window.

2. **On Controlling PC (RemoteLAN Controller)**:
   ```powershell
   dotnet run --project src/RemoteLAN.Controller/RemoteLAN.Controller.csproj
   ```
   - Enter the target PC's **IP Address**, **Port** (default: `9191`), and the **Security PIN**.
   - Click **Connect** to view and control the remote desktop.
