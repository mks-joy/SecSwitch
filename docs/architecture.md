# Architecture

SecSwitch is designed around a strict separation between **discovery**, **session management**, and **elevated control**.

## Components

### `SecSwitch.Core`

Shared domain models and read-only detection logic.

Responsibilities:

- Load module manifests from `modules/*.json`
- Detect installed/running modules
- Read service/process state
- Build session snapshots
- Contain no UI code

### `SecSwitch.Cli`

Developer-facing command-line interface.

Initial commands:

- `scan` — detect supported modules
- `status` — show current state

Future commands:

- `on --minutes 5`
- `off`

### `SecSwitch.Tray`

Future Windows tray UI.

Responsibilities:

- Start/extend/end a managed session
- Show remaining session time
- Show running modules
- Surface startup/background-residency diagnostics

### `SecSwitch.Elevated`

Future short-lived elevated helper for operations that require administrator privileges.

The tray process should not remain permanently elevated.

## Session model

A managed security session must preserve pre-existing state.

Example:

1. AnySign is stopped.
2. MagicLine is already running before SecSwitch starts a session.
3. SecSwitch starts AnySign.
4. The session ends.
5. SecSwitch stops AnySign but leaves MagicLine running.

This prevents SecSwitch from disrupting modules that were already in use for another reason.

## Module manifests

Product-specific details live in JSON manifests under `modules/`.

A manifest may describe:

- Service names
- Process executable names and paths
- Detection paths
- Runtime start strategy
- Runtime stop strategy
- Vendor/product metadata

Runtime control is allowlist-based. Unknown services/processes must never be controlled automatically.

## Safety boundaries

SecSwitch must never:

- Disable Microsoft Defender
- Disable Windows Firewall
- Patch browser/site security checks
- Pretend a required module is installed when it is not
- Kill arbitrary processes based on broad name matching
- Download or execute untrusted binaries

SecSwitch manages only explicitly supported third-party web security modules already installed on the PC.
