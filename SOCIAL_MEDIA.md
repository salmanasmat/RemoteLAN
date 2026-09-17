# Social Media Announcements

## [1.1.5] - 2026-09-17

### LinkedIn

Excited to share the v1.1.5 update for RemoteLAN, our open-source, LAN-only peer-to-peer remote desktop application built with C# and .NET 8!

When using remote desktop software across local machines, desktop real estate and session control need to feel native, responsive, and distraction-free. In this release, we focused on major UX improvements and session workflow ergonomics:

- Immersive True Fullscreen: Both the top control bar and bottom status bar automatically hide when entering fullscreen mode, giving you edge-to-edge access to the remote display.
- Floating Peek Toolbar: Need quick access to session controls? Simply hover your cursor near the top edge of the screen to smoothly reveal the floating toolbar without resizing the remote video stream or causing frame jitter.
- Unified Actions Control Center: We replaced cluttered individual buttons with a streamlined "Actions" menu, grouping remote input toggles, saved Windows OS credential unlocking, Ctrl+Alt+Del injection, and remote system power controls (Lock, Sleep, Restart, Shutdown).
- Polished Installer Lifecycle: The installer now enables the "Launch" checkbox by default and eliminates background process race conditions, ensuring clean single-instance startup after installation.

Coupled with our recent breakthroughs in Windows Secure Desktop SYSTEM token duplication and automated OS login, RemoteLAN provides a fast, cloud-free alternative for local office and lab remote support.

Explore the open-source repository, check out the architecture specs, and download the standalone Windows installer:
https://github.com/salmanasmat/RemoteLAN

#OpenSource #DotNet #CSharp #RemoteDesktop #SoftwareEngineering #SysAdmin #Networking #WindowsDev #DevCommunity

---

### Reddit

**Suggested Subreddits**:
- r/csharp
- r/dotnet
- r/sysadmin
- r/selfhosted
- r/programming

**Post Title**:
RemoteLAN v1.1.5 — Added borderless fullscreen with auto-hiding peek toolbar, unified Actions menu, and automated Windows lock screen unlock (.NET 8 / DXGI)

**Post Body**:
Hey everyone!

Quick follow-up update on **RemoteLAN**, the open-source, LAN-only remote desktop tool built with C#, .NET 8, and DirectX 11. We just tagged **v1.1.5** with several significant UX and desktop control upgrades.

### What's New in v1.1.5:

1. **True Borderless Fullscreen & Auto-Hiding Peek Toolbar**:
   - In fullscreen mode, the remote desktop fills 100% of the display from edge to edge with zero borders or permanent bars.
   - Moving your cursor to the top edge of the screen reveals a floating control bar overlay. Moving the cursor back down smoothly hides it.
   - Crucially, peeking the toolbar does not resize or reflow the DirectX video viewport, avoiding aspect-ratio jumps or rendering stutter.

2. **Unified Actions Control Center**:
   - Consolidated individual toolbar buttons into a clean `⚡ Actions ▾` dropdown.
   - Provides instant access to:
     - **Send Remote Input** (toggleable mouse/keyboard forwarding)
     - **Windows OS Login** (auto-entering saved OS credentials to unlock remote sign-in screens)
     - **Send Ctrl+Alt+Del** (to wake the lock screen or summon credential tiles)
     - **Remote Power Controls** (Lock, Sleep, Restart, Shutdown)

3. **Installer & Startup Polishing**:
   - The Inno Setup installer now checks "Launch RemoteLAN" by default upon setup completion.
   - Eliminated a race condition between the background service spawn and interactive post-install launch using single-instance mutex validation (`CheckForMutexes`).

### Background on the Technical Stack:
If you missed our earlier updates, RemoteLAN is designed for environments where you want AnyDesk-like peer-to-peer control between PCs on the same LAN without external cloud relays:
- **Display Pipeline**: DirectX 11 DXGI Desktop Duplication (`60+ FPS`) with dynamic pillarbox/letterbox coordinate mapping.
- **Lock Screen / UAC Bypass**: Dynamically elevates to `SYSTEM` within the active interactive session (`CreateProcessWithTokenW` on `winlogon.exe`), allowing reliable remote input injection and credential unlocking even on the Windows Secure Desktop.
- **Hardware Scan Code Fidelity**: Translates keys using `VkKeyScan` and physical scan codes (`MapVirtualKey`) with automatic Shift/Ctrl/Alt modifier synthesis so Windows Credential Providers accept automated inputs.

Check out the project on GitHub:
https://github.com/salmanasmat/RemoteLAN

Installer download:
`dist/RemoteLAN_Setup_v1.1.5.exe`

Would love to hear your thoughts, feedback, or feature requests!

---

### X (Twitter)

RemoteLAN v1.1.5 is out! 🚀

Now featuring true borderless fullscreen with an auto-hiding peek toolbar, a unified Actions control center, and polished Windows installer defaults.

High-performance, zero-cloud LAN remote desktop with .NET 8 & DXGI.

Check it out:
https://github.com/salmanasmat/RemoteLAN

---

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
