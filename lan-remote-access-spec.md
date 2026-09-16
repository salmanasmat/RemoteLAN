# RemoteLAN — Phased Build Spec

**Goal:** A simple, custom, LAN-only remote desktop tool for internal office use. No cloud relay, no login walls, no artificial wait timers. Two components: an **Agent** (runs on target PCs) and a **Controller** (viewer app used to connect out).

**Stack:** C# / .NET 8 / WPF. Direct TCP sockets, no HTTP/WebSocket overhead — this is a trusted private LAN.

This is a standalone project — do not merge with or reference any other remote-access/monitoring tool.

---

## Phase 1 — Screen Streaming (RemoteLAN Agent → RemoteLAN Controller, one-way)

**Objective:** Get a live view of a remote PC's screen with no input control yet. Prove the capture/stream/render pipeline works end-to-end.

### Agent
- .NET 8 console or tray app (`RemoteLAN.Agent`)
- Capture screen using DXGI Desktop Duplication API (fall back to GDI `BitBlt` if DXGI unavailable)
- Encode captured frames as JPEG (quality ~70, configurable) at a target ~10–15 fps to start
- Open a `TcpListener` on a fixed configurable port (e.g. `9191`)
- On client connect: stream frames continuously using a simple framing protocol:
  - 4-byte big-endian `int32` frame length prefix
  - followed by that many bytes of JPEG data
- Handle client disconnect gracefully (stop capturing, wait for next connection)

### Controller
- WPF app (`RemoteLAN.Controller`) with a single text field for target IP + a Connect button
- On connect: open `TcpClient`, read the length-prefixed frame stream, decode JPEG, render into an `Image` control in a tight loop
- Show basic connection status (Connecting / Connected / Disconnected / Error)
- Handle malformed/partial frames defensively (this is a live socket stream, don't assume clean reads)

### Acceptance criteria
- Controller can connect to Agent's IP:port and see a live, reasonably smooth screen feed
- Reconnecting after a disconnect works without restarting either app
- CPU usage on Agent is reasonable at idle vs during capture (sanity check, not a hard number yet)

---

## Phase 2 — Input Injection (RemoteLAN Controller → RemoteLAN Agent)

**Objective:** Add remote control — mouse and keyboard input from Controller drives the Agent's PC.

### Agent
- Add a second logical channel (either a second TCP connection, or multiplex over the same connection with a message-type byte prefix — pick multiplexing to keep it to one socket)
- Message types: `MouseMove(x,y)`, `MouseDown(button)`, `MouseUp(button)`, `MouseWheel(delta)`, `KeyDown(vkCode)`, `KeyUp(vkCode)`
- Translate incoming coordinates (Controller's view coordinates) to the Agent's actual screen resolution — handle scaling if resolutions differ
- Inject input using the Win32 `SendInput` API (P/Invoke)

### Controller
- Capture local mouse events within the rendered screen-view control; translate control-relative coordinates to remote-screen coordinates before sending
- Capture keyboard input while the view has focus; forward key up/down events
- Serialize input events using the same length-prefixed framing style as Phase 1, with a leading message-type byte to distinguish from frame data if multiplexed

### Acceptance criteria
- Mouse movement, clicks, and scroll on Controller reliably move/click/scroll on the Agent's actual desktop
- Keyboard input (including modifier keys — Shift/Ctrl/Alt) is correctly reproduced on the Agent
- Coordinate mapping is accurate even when Controller window is resized

---

## Phase 3 — Access Control (PIN Handshake)

**Objective:** A lightweight gate so a connection isn't accepted from just any device that finds the IP — without building a full account/login system.

### Agent
- On startup, generate (or let the user set) a numeric PIN, displayed in the tray icon's tooltip/flyout
- On new TCP connection, before allowing frame streaming to begin: require the Controller to send the PIN as the first message
- If PIN is correct: proceed to Phase 1 streaming behavior
- If incorrect: send a rejection message and close the connection
- Optional: PIN can be static (set once in a config file) or regenerated each Agent session — decide based on how much friction is acceptable for your office use

### Controller
- On connect, prompt for the PIN in a simple dialog before the stream view opens
- Send PIN as the first framed message; handle rejection by showing an error and returning to the connect screen

### Acceptance criteria
- Connections without the correct PIN are refused and never reach screen/input functionality
- PIN entry adds minimal friction — no account creation, no external server round-trip, just a local check

---

## Notes for the coding agent (Gemini/Antigravity)

- Keep `RemoteLAN.Agent` and `RemoteLAN.Controller` as two separate projects in one solution (e.g. `RemoteLAN.sln`), sharing a small common library (`RemoteLAN.Protocol`) for the framing/protocol code (message types, length-prefix read/write helpers) to avoid duplicating wire-format logic
- No dependency on any relay/cloud service anywhere in this codebase — all connections are direct IP:port on LAN
- Favor clear, defensive socket-handling code (timeouts, disconnects, partial reads) over premature optimization
- Build and validate each phase before starting the next — Phase 2 depends on Phase 1's transport working correctly, Phase 3 wraps both
