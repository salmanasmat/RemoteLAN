# Social Media Announcements

## [1.0.0] - 2026-09-16

### LinkedIn

Excited to announce the official 1.0.0 release of RemoteLAN, an open-source, high-performance remote desktop tool designed specifically for local area networks!

Most modern remote desktop tools rely heavily on third-party cloud infrastructure, relay servers, and third-party accounts just to connect two machines sitting on the exact same office switch. That introduces latency, privacy concerns, and unnecessary bandwidth overhead.

RemoteLAN solves this by bringing an AnyDesk-style, bidirectional remote control workflow strictly to your local network with zero cloud dependencies.

Key highlights in our 1.0.0 milestone:
- Bidirectional Peer-to-Peer Control: Every installation can initiate remote connections and receive incoming sessions simultaneously.
- Ultra-Low Latency Streaming: Powered by DirectX 11 DXGI Desktop Duplication for smooth 60+ FPS performance with automatic GDI fallback.
- Windows Lock Screen & Sign-In Support: Seamlessly controls Winlogon desktop sessions, dismisses lock screens, and sends Ctrl+Alt+Del to unlock remote PCs.
- Resilient Input Architecture: Thread-safe key state tracking, duplicate keypress suppression, and disconnect cleanup to completely eliminate stuck keys or phantom inputs.
- Security-First Hardening: Constant-time password validation, automatic IP brute-force lockouts, and zero cloud telemetry.

Built entirely with C#, .NET 8, and WPF. Check out the project on GitHub and download the self-contained Windows installer today!

#OpenSource #DotNet #CSharp #RemoteDesktop #SoftwareEngineering #SysAdmin #Networking #WindowsDev #DevCommunity

---

### Reddit

**Suggested Subreddits**:
- r/csharp
- r/dotnet
- r/programming
- r/sysadmin
- r/selfhosted

**Post Title**:
RemoteLAN v1.0.0 — An open-source, AnyDesk-style peer-to-peer remote desktop built with .NET 8, WPF, and DXGI (Zero Cloud Relay)

**Post Body**:
Hey everyone!

I built **RemoteLAN**, a high-performance LAN-only remote desktop tool, and we just hit our official **v1.0.0 release**!

### Why build this?
In many office, lab, or home network setups, you just want to quickly take control of another PC on your subnet without setting up third-party accounts, paying SaaS subscriptions, or bouncing display video frames off an external cloud relay server.

RemoteLAN provides a unified AnyDesk-style desktop experience: every PC runs the exact same client, automatically discovers nearby machines via UDP, and allows direct, high-throughput TCP socket connections between machines.

### Tech Stack & Architecture:
- **Framework**: C# / .NET 8 / WPF (Desktop GUI in clean Light Mode)
- **Capture Engine**: DirectX 11 DXGI Desktop Duplication (`Vortice.Direct3D11` / `Vortice.DXGI`) with GDI fallback for legacy compatibility.
- **Wire Protocol**: Custom lightweight framing (`MessageType` byte + big-endian length + JPEG-compressed frame / input payload).
- **Lock Screen & UAC**: Uses Windows desktop switching (`DesktopManager`) to attach to `Winlogon` and `sas.dll` / hardware key simulation to wake lock screens and allow remote login as Administrator.
- **Input Pipeline**: State-tracked input injection via Win32 `SendInput` with session isolation, automatic focus-loss release, and duplicate `KeyDown` suppression.
- **Security Hardening**: Constant-time credential checks (`CryptographicOperations.FixedTimeEquals`), automated brute-force IP rate limiting, and parameter-safe system power control (`ProcessStartInfo.ArgumentList`).

### Packaging & Installation:
Bundled with a standalone Inno Setup installer that includes the complete .NET 8 desktop runtime. It configures background Windows startup, system tray minimization, and single-instance activation.

The repository includes a comprehensive test suite (73 automated tests) and full OWASP security documentation (`SECURITY.md`).

Check out the code, read the architecture specs, or grab the Windows installer:
https://github.com/salmanasmat/RemoteLAN

Feedback, issue reports, and contributions are very welcome!

---

### X (Twitter)

RemoteLAN v1.0.0 is live! 🚀

A high-performance, LAN-only remote desktop tool for Windows built with .NET 8 & DirectX 11. Zero cloud relays, 60+ FPS streaming, lock-screen control, and AnyDesk-style auto-discovery.

Download the installer & explore the code:
https://github.com/salmanasmat/RemoteLAN
