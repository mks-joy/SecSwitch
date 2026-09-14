# SecSwitch

**한국형 웹 보안 모듈 런타임 관리자 / Web Security Module Runtime Manager for Windows**

[한국어](#한국어) · [English](#english)

---

## 한국어

SecSwitch는 국내 금융기관, 공공기관, 정부·공공서비스 웹사이트 이용 시 요구되는 각종 로컬 보안 모듈의 **실행 상태와 수명주기(runtime lifecycle)**를 관리하기 위한 Windows 유틸리티입니다.

목표는 단순합니다. **필요할 때는 정상적으로 실행하고, 필요하지 않을 때는 불필요하게 CPU·메모리 등 시스템 자원을 점유하지 않도록 관리하는 것**입니다.

### 왜 SecSwitch가 필요한가요?

국내의 많은 금융·공공 웹사이트는 공동인증서, 키보드 보안, 방화벽, 접속 정보 확인, 전자서명 등을 위해 별도의 로컬 보안 프로그램 설치를 요구합니다.

문제는 일부 프로그램이 해당 사이트 사용이 끝난 뒤에도 계속 백그라운드에 남아 있거나, Windows 시작과 함께 자동 실행되어 하루 종일 시스템 자원을 점유한다는 점입니다.

SecSwitch는 이러한 프로그램을 제거하거나 웹사이트의 보안 절차를 우회하려는 도구가 아닙니다. **이미 정상적으로 설치된 보안 모듈을 필요할 때 실행하고, 사용 후 불필요하게 남아 있는 프로세스와 서비스를 정리하는 것**이 목적입니다.

### 프로젝트 원칙

- **항상 실행이 아니라 필요할 때만 실행** — 지원되는 모듈을 필요한 시점에 실행하고 세션 종료 후 정리합니다.
- **기존 상태 복원** — SecSwitch 실행 전부터 이미 실행 중이던 모듈은 SecSwitch가 임의로 종료하지 않습니다.
- **명시적 허용 목록(allowlist)만 사용** — 광범위한 프로세스 종료나 일반적인 Windows 서비스 조작은 하지 않습니다.
- **보안 우회 금지** — 설치 여부를 위조하거나, 웹사이트를 패치하거나, 인증·보안 절차를 우회하지 않습니다.
- **동작의 투명성** — 어떤 모듈이 탐지되었고, 실행 중인지, SecSwitch가 무엇을 변경하려는지 사용자에게 보여줍니다.
- **커뮤니티 기반 모듈 정의** — 제품별 서비스명·실행파일·탐지 정보는 코드에 하드코딩하지 않고 JSON 매니페스트로 관리합니다.

### 로드맵

#### v0.1 — 읽기 전용 스캐너

- 알려진 국내 웹 보안 모듈 설치 여부 탐지
- 서비스·프로세스 실행 상태 표시
- JSON 매니페스트 기반 모듈 정의 로딩
- 서비스 시작·종료 등 시스템 변경은 하지 않음

#### v0.2 — 보안 모듈 세션 관리

- 지원되는 모듈을 일정 시간 동안 실행
- 세션 시작 전 상태 저장
- SecSwitch가 직접 시작한 모듈만 세션 종료 시 정리
- 세션 시간 연장 및 즉시 종료

#### v0.3 — Windows 트레이 앱

- 가벼운 트레이 UI
- 현재 실행 중인 보안 모듈 상태 확인
- 기본 5분 보안 모듈 세션
- Windows 시작 시 상주 여부 및 불필요한 백그라운드 실행 진단

### 초기 지원 대상

초기 매니페스트는 실제 국내 금융·공공 웹사이트 이용 과정에서 확인된 다음 제품들을 기반으로 작성하고 있습니다.

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

현재 단계에서 “지원”은 SecSwitch가 해당 모듈을 **탐지할 수 있음**을 의미합니다. 실행·종료 제어 기능은 제품별 동작을 검증하면서 보수적으로 추가합니다.

### 기술 스택

- C# / .NET 10
- Windows 전용
- 가능한 경우 Windows 네이티브 서비스 상태 조회 사용
- JSON 기반 모듈 매니페스트

### 빌드

```powershell
dotnet build SecSwitch.sln
```

CLI 스캐너 실행:

```powershell
dotnet run --project src/SecSwitch.Cli -- scan
```

### 라이선스

MIT License. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.

### 현재 상태

초기 개발 단계입니다. 현재 우선순위는 서비스나 프로세스를 제어하기 전에 **신뢰할 수 있는 읽기 전용 탐지 및 상태 확인 엔진**을 만드는 것입니다.

---

## English

SecSwitch is a Windows utility for managing the runtime lifecycle of security modules commonly required by Korean banking, government, and public-service websites.

The goal is simple: keep required modules available when they are actually needed, while reducing unnecessary background CPU and memory usage when they are not.

### Why SecSwitch?

Many Korean websites require locally installed security software for certificate access, keyboard protection, endpoint checks, firewall functions, or site-specific integrations. These modules may be legitimate requirements for the website being used, but some remain resident long after the session ends or start automatically with Windows.

SecSwitch does **not** bypass website security checks and does **not** disable Windows security features. It manages only explicitly supported third-party web security modules already installed on the user's PC.

### Project principles

- **On demand, not always on.** Start supported modules when needed and clean up modules started by SecSwitch after the session.
- **Restore prior state.** A module that was already running before a SecSwitch session should not be stopped by that session.
- **Allowlist only.** Never perform broad process killing or generic service manipulation.
- **No silent security bypass.** SecSwitch does not fake installation state, patch websites, or circumvent authentication/security controls.
- **Transparent behavior.** Show what is detected, what is running, and what SecSwitch plans to change.
- **Community-maintained module definitions.** Product-specific details live in JSON manifests rather than being hard-coded into the application.

### Roadmap

#### v0.1 — read-only scanner

- Detect known security modules installed on Windows
- Show service/process status
- Load module definitions from JSON manifests
- No start/stop or system modification

#### v0.2 — managed security sessions

- Start supported modules for a timed session
- Remember pre-session state
- Stop only modules started by SecSwitch
- Extend or end a session manually

#### v0.3 — tray application

- Lightweight Windows tray UI
- Running-module overview
- 5-minute session button
- Startup/background residency diagnostics

### Initial module set

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

Support currently means that SecSwitch knows how to identify the module. Runtime control will be added gradually and conservatively.

### Technology

- C# / .NET 10
- Windows-only
- Native Windows service inspection where practical
- JSON module manifests

### Build

```powershell
dotnet build SecSwitch.sln
```

Run the CLI scanner:

```powershell
dotnet run --project src/SecSwitch.Cli -- scan
```

### License

MIT. See [LICENSE](LICENSE).

### Status

Early development. The current priority is building a reliable, read-only inventory and status engine before adding any service/process control.
