# SecSwitch

**Web Security Module Runtime Manager for Windows**

SecSwitch is a Windows utility for managing the runtime lifecycle of security modules commonly required by Korean banking, government, and public-service websites.

The goal is simple: keep required modules available when they are actually needed, while reducing unnecessary background CPU and memory usage when they are not.

## Why SecSwitch?

Many Korean websites require locally installed security software for certificate access, keyboard protection, endpoint checks, firewall functions, or site-specific integrations. These modules are often legitimate requirements for the website being used, but some remain resident long after the session ends or start automatically with Windows.

SecSwitch does **not** bypass website security checks and does **not** disable Windows security features. It manages only explicitly supported third-party web security modules already installed on the user's PC.

## Project principles

- **On demand, not always on.** Start supported modules when needed and clean up modules started by SecSwitch after the session.
- **Restore prior state.** A module that was already running before a SecSwitch session should not be stopped by that session.
- **Allowlist only.** Never perform broad process killing or generic service manipulation.
- **No silent security bypass.** SecSwitch does not fake installation state, patch websites, or circumvent authentication/security controls.
- **Transparent behavior.** Show what is detected, what is running, and what SecSwitch plans to change.
- **Community-maintained module definitions.** Product-specific details live in data files rather than being hard-coded into the application.

## Roadmap

### v0.1 — read-only scanner

- Detect known security modules installed on Windows
- Show service/process status
- Load module definitions from JSON manifests
- No start/stop or system modification

### v0.2 — managed security sessions

- Start supported modules for a timed session
- Remember pre-session state
- Stop only modules started by SecSwitch
- Extend or end a session manually

### v0.3 — tray application

- Lightweight Windows tray UI
- Running-module overview
- 5-minute session button
- Startup/background residency diagnostics

## Initial module set

The first manifests are based on modules observed in real Korean banking/public-service workflows, including:

- AnySign4PC
- AhnLab Safe Transaction
- nProtect Online Security
- MagicLine4NX
- WIZVERA Process Manager
- IPinside / Interezen LWS
- SignKoreaWD
- CrossCert UniSign
- RAON K
- TouchEn nxFirewall
- ExAdapter_NxWeb

Support means only that SecSwitch knows how to identify the module. Runtime control will be added gradually and conservatively.

## Technology

- C# / .NET 10
- Windows-only
- Native Windows service inspection where practical
- JSON module manifests

## Build

```powershell
dotnet build SecSwitch.sln
```

Run the CLI scanner:

```powershell
dotnet run --project src/SecSwitch.Cli -- scan
```

## License

MIT. See [LICENSE](LICENSE).

## Status

Early development. The current priority is building a reliable, read-only inventory and status engine before adding any service/process control.
