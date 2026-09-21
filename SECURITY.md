# Security Policy & Audit Report

## Security Policy

We take the security of **RemoteLAN** seriously. This document outlines our security practices, recent audit findings, and instructions on reporting vulnerabilities responsibly.

### Supported Versions

| Version | Supported | Notes |
| :--- | :---: | :--- |
| **1.3.x** | 🟢 Yes | Active release branch (Current: v1.3.0) |
| **1.2.x** | 🔴 No | Superseded by v1.3.x |
| **1.1.x** | 🔴 No | Superseded by v1.2.x |
| **1.0.x** | 🔴 No | Superseded by v1.1.x |

### Reporting a Vulnerability

If you discover a security vulnerability in RemoteLAN, please **do not open a public issue**. Instead, report it privately to the maintainers via:
- GitHub Security Advisories: Submit a private advisory report directly on the repository.
- Email: Send vulnerability reports directly to the maintainer team.

Please include:
- A description of the issue and potential impact.
- Step-by-step reproduction steps or proof of concept.
- Affected versions (e.g., v1.2.1).

We will acknowledge receipt of your report within 48 hours, investigate the issue promptly, and release a patch in accordance with semantic versioning.

---

## Security Audit Report (OWASP Standards)

- **Audit Date**: March 2026 (Updated for v1.2.1 release)
- **Audited Target**: RemoteLAN v1.2.1 (`src/RemoteLAN`, `src/RemoteLAN.Protocol`, `tests/RemoteLAN.Tests`)
- **Scope**: Full repository security sweep (Secrets, Injection, Authentication/Authorization, Privilege Management, Supply Chain).

### Executive Summary

| Category | Status | Notes |
| :--- | :---: | :--- |
| **Secrets & Keys** | 🟢 PASSED | Zero hardcoded secrets, private keys, or API tokens in codebase. |
| **Credential Storage** | 🟡 WARNING | User-scoped `%LocalAppData%` JSON keyed by Machine GUIDs; recommend DPAPI encryption at rest. |
| **Injection Vulnerabilities** | 🟢 PASSED | Safe typed enum protocol routing; hardened `ProcessStartInfo.ArgumentList` execution. |
| **Authentication & AuthZ** | 🟢 PASSED | CSPRNG PIN generation, constant-time validation (`FixedTimeEquals`), IP lockout protection. |
| **Privilege Impersonation** | 🟢 PASSED | Strict RAII token impersonation (`ImpersonationScope`) with guaranteed `RevertToSelf()`. |
| **Network & DoS Defense** | 🟢 PASSED | Enforced framing boundaries (`MaxPayloadSize` 20MB limit) preventing buffer exhaustion. |
| **Dependencies & Supply Chain** | 🟢 PASSED | Minimal verified dependency tree on .NET 8; zero vulnerable packages. |

---

### Detailed Findings

#### 1. Secrets Detection (OWASP A07: Identification and Authentication Failures)
* **Scan Result**: 🟢 **PASSED**
* Automated regex and static analysis detected **zero** hardcoded secrets, passwords, credentials, or API keys in application sources and test suites.
* All session codes and security tokens are generated dynamically at runtime using cryptographically secure random number generators (`System.Security.Cryptography.RandomNumberGenerator`).

#### 2. Injection Prevention (OWASP A03: Injection)
* **Scan Result**: 🟢 **PASSED**
* **Command Injection**: `SystemPowerManager` executes Windows system commands (`shutdown.exe`) for remote Lock/Sleep/Restart/Shutdown.
  - *Hardening Applied*: Replaced string-interpolated arguments with structured `ProcessStartInfo.ArgumentList` (`/r`, `/s`, `/t`, `/f`, `/c`), preventing argument injection or delimiter breakout.
  - *Network Protocol Boundary*: Incoming wire commands (`PowerActionMessage`) carry only a strongly typed 1-byte enum (`PowerActionType`), preventing arbitrary command delivery over the network.
* **SQL Injection**: Not applicable (no SQL database used; settings are managed via JSON files).
* **Cross-Site Scripting (XSS)**: Not applicable (native WPF desktop client rendering Direct3D/WPF visuals, no web views or `innerHTML` evaluation).

#### 3. Authentication & Access Control (OWASP A01: Broken Access Control & A07: Authentication)
* **Scan Result**: 🟢 **PASSED**
* **Cryptographic Randomness**: Temporary session PINs are generated using `RandomNumberGenerator.GetInt32` across an alphanumeric alphabet (`23456789ABCDEFGHJKLMNPQRSTUVWXYZ`), avoiding ambiguous characters (0, 1, I, O).
* **Timing Attack Prevention**:
  - *Hardening Applied*: Candidate PINs and unattended passwords in `PinManager.ValidatePin` are verified using `CryptographicOperations.FixedTimeEquals` to prevent side-channel timing analysis.
* **Brute-Force & Rate Limiting**:
  - `AgentServer` and `SettingsManager` implement automatic IP lockout: after 5 consecutive failed authentication attempts (configurable), the offending client IP is temporarily locked out for 10 minutes (configurable).
* **Session Lifecycle**:
  - Sessions are strictly scoped using incrementing generation IDs (`_sessionGeneration`). Disconnection immediately revokes session privileges and resets hardware input queues.
* **Machine Identity & Dynamic IP Defense**:
  - Agents generate and persist a persistent cryptographically unique GUID (`agent.id`). Controllers deduplicate and identify hosts by machine identity rather than transient network IPs, preventing host spoofing and multi-NIC crosstalk.
  - Multi-identifier credential mapping resolves saved credentials across `MachineId`, `MachineName`, and `IpAddress` cross-referenced through `DeviceHistory`, ensuring credentials cannot be misattributed when DHCP leases change.

#### 4. System Privilege Elevation & Token Impersonation
* **Scan Result**: 🟢 **PASSED**
* RemoteLAN interacts with Winlogon desktops to provide remote assistance during lock screens and UAC prompts.
* **RAII Protection**: `DesktopManager.ImpersonationScope` wraps all Win32 `ImpersonateLoggedOnUser` operations in `IDisposable` scopes with guaranteed `RevertToSelf()` in `finally` blocks.
* All opened process, token, and desktop handles (`OpenProcessToken`, `DuplicateTokenEx`, `OpenDesktop`, `OpenInputDesktop`) are explicitly closed in dedicated `finally` blocks (`CloseHandle`, `CloseDesktop`).
* **Lock Screen Keystroke Safety**:
  - `DesktopManager.UnlockWithPassword` uses non-destructive lock-screen wake (native `SendSAS(false)` and navigation keys) without emitting destructive cancellation keys (`VK_ESCAPE`) that disrupt credential providers.
  - Normalizes keystroke entry state (detects and disables active CapsLock via `NativeMethods.GetKeyState`) prior to sending unlock sequences, preventing password corruption over the Winlogon boundary.

#### 5. Network Buffer Safety & DoS Prevention (OWASP A04: Insecure Design)
* **Scan Result**: 🟢 **PASSED**
* `NetworkFrameReader` validates incoming length headers: any frame exceeding `ProtocolConstants.MaxPayloadSize` (20MB) or indicating negative length is rejected immediately with `InvalidDataException`, dropping the connection and preventing memory exhaustion.

#### 6. Credential Storage at Rest
* **Scan Result**: 🟡 **WARNING (Future Hardening Recommended)**
* Configuration and credentials (unattended access password and saved remote device passwords) are stored in JSON at `%LocalAppData%\RemoteLAN\settings.json`, partitioned and mapped by persistent Machine GUIDs.
* While access is restricted by Windows operating system NTFS file permissions to the current user and Administrators:
  - *Recommendation*: Encrypt stored passwords at rest using Windows Data Protection API (DPAPI: `ProtectedData.Protect` with `DataProtectionScope.CurrentUser`) to protect credentials against unauthorized tools running under the same user context.

#### 7. Dependency Analysis (OWASP A06: Vulnerable and Outdated Components)
* **Scan Result**: 🟢 **PASSED**
* Evaluated third-party packages:
  - `System.Drawing.Common` (10.0.12)
  - `Vortice.Direct3D11` (3.8.3)
  - `Vortice.DXGI` (3.8.3)
* Dependencies are up to date, minimal, and run against .NET 8 LTS.

---

### Verification
- Full test suite passed (89 unit, integration, and regression tests).
- All security hardening modifications tested and verified.
