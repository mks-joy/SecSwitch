# SecSwitch

**한국형 웹 보안 모듈 런타임 관리자 / Web Security Module Runtime Manager for Windows**

[한국어](#한국어) · [English](#english)

---

## 한국어

SecSwitch는 국내 금융기관, 공공기관, 정부·공공서비스 웹사이트 이용 시 요구되는 각종 로컬 보안 모듈의 **실행 상태와 수명주기(runtime lifecycle)**를 관리하기 위한 Windows 유틸리티입니다.

목표는 단순합니다. **필요할 때는 정상적으로 실행하고, 필요하지 않을 때는 불필요하게 CPU·메모리 등 시스템 자원을 점유하지 않도록 관리하는 것**입니다.

SecSwitch 자체는 기본적으로 Windows 시작 시 상주하지 않습니다. 사용자가 필요할 때 실행하고, 관리 대상으로 선택한 보안 모듈만 일정 시간 준비한 뒤 다시 정리하는 방향을 지향합니다.

### 첫 실행 흐름

1. 설치된 지원 보안 모듈을 스캔합니다.
2. 각 모듈의 제조사, 용도, 알려진 사용처, 실행 상태, 시작 유형, CPU·메모리 점유를 보여줍니다.
3. 사용자가 각 모듈을 **그대로 유지**할지 **On-demand 관리**할지 선택합니다.
4. `유지`를 선택하면 아무것도 변경하지 않습니다.
5. `On-demand`를 선택하면 검증된 서비스는 `Manual` 시작으로 전환하고 현재 실행을 정리합니다.
6. 선택 내용은 `%LOCALAPPDATA%\SecSwitch\profile.json`에 저장됩니다.
7. 이후 보안 사이트를 이용할 때 SecSwitch의 5분 세션을 시작하면 On-demand 대상으로 선택한 모듈만 준비됩니다.
8. 세션 종료 시 SecSwitch가 이번 세션에서 시작한 항목만 원래 상태로 돌립니다.

안전한 종료·원복 방법이 아직 검증되지 않은 모듈은 `observeOnly`로 표시하고 SecSwitch가 임의로 변경하지 않습니다.

### 프로젝트 원칙

- **항상 실행이 아니라 필요할 때만 실행**
- **사용자가 선택한 모듈만 관리**
- **기존 상태 복원** — 세션 시작 전부터 실행 중이던 항목은 임의로 종료하지 않음
- **명시적 허용 목록(allowlist)만 사용**
- **보안 우회 금지** — 설치 여부 위조, 웹사이트 패치, 인증·보안 절차 우회 없음
- **동작의 투명성** — 무엇을 탐지하고 변경하는지 사용자에게 표시
- **커뮤니티 기반 모듈 정의** — 제품별 정보는 JSON 매니페스트로 관리

### 현재 구현 상태

- **v0.1 스캐너: 실제 Windows PC에서 동작 검증 완료**
  - 설치 여부 및 서비스·프로세스 상태 탐지
  - 서비스 시작 유형 표시
  - 1초 샘플 기반 CPU·메모리 점유 측정
  - 제품 설명·용도·알려진 사용처 표시
- **v0.2 설정/세션 엔진: 알파 구현 중**
  - 최초 설정 마법사형 CLI (`setup`)
  - `유지` / `On-demand` 사용자 선택 저장
  - On-demand 서비스의 Manual 전환 및 현재 실행 정리
  - 기본 5분 세션
  - 세션 시작 전 상태 기록
  - SecSwitch가 시작한 서비스·프로세스만 종료 대상으로 기록
  - 세션 연장 및 즉시 종료
  - 세션 상태를 `%LOCALAPPDATA%\SecSwitch\session.json`에 저장

현재 CLI는 기능 검증용입니다. 런타임 동작이 안정화되면 Windows 트레이 앱과 설치형 배포 파일로 사용성을 다듬을 예정입니다.

### 초기 지원 대상

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

### 기술 스택

- C# / .NET 10
- Windows 전용
- Windows 서비스 및 프로세스 상태 조회·제어
- JSON 기반 모듈 매니페스트
- GitHub Actions 기반 Windows 빌드 검증

### 빌드

```powershell
dotnet build SecSwitch.sln
```

최초 설정:

```powershell
dotnet run --project src/SecSwitch.Cli -- setup
```

스캔:

```powershell
dotnet run --project src/SecSwitch.Cli -- scan
```

저장된 관리 프로필 확인:

```powershell
dotnet run --project src/SecSwitch.Cli -- profile show
```

5분 세션 시작:

```powershell
dotnet run --project src/SecSwitch.Cli -- session start --minutes 5
```

세션 상태 / 연장 / 종료:

```powershell
dotnet run --project src/SecSwitch.Cli -- session status
dotnet run --project src/SecSwitch.Cli -- session extend --minutes 5
dotnet run --project src/SecSwitch.Cli -- session stop
```

> 현재 설정/세션 엔진은 알파 단계입니다. 서비스 설정 변경에는 관리자 권한이 필요할 수 있으며, 기능 검증 중에는 중요한 작업을 진행하지 않는 상태에서 테스트하는 것을 권장합니다.

### 라이선스

MIT License. 자세한 내용은 [LICENSE](LICENSE)를 참고하세요.

---

## English

SecSwitch is a Windows utility for managing the runtime lifecycle of security modules commonly required by Korean banking, government, and public-service websites.

Its goal is to keep required modules available when needed while reducing unnecessary background CPU and memory usage when they are not. SecSwitch itself is not intended to become another always-on startup utility.

### First-run flow

1. Scan installed supported security modules.
2. Show vendor, purpose, known usage categories, runtime state, startup type, CPU and memory usage.
3. Let the user choose **Keep** or **On-demand** for each supported module.
4. Keep leaves the current configuration untouched.
5. On-demand converts verified services to Manual startup and cleans up their current runtime where safe.
6. Save the user's choices under `%LOCALAPPDATA%\SecSwitch\profile.json`.
7. Later, a timed SecSwitch session starts only modules selected for On-demand management.
8. At session end, SecSwitch restores only the items it started for that session.

Modules without a verified reversible control path remain `observeOnly` and are never modified automatically.

### Current status

- **v0.1 scanner: validated on a real Windows installation**
  - installation/runtime detection
  - service startup-type reporting
  - sampled CPU/RAM usage
  - module descriptions and known-use hints
- **v0.2 setup/session engine: alpha**
  - first-run CLI setup
  - Keep / On-demand profile
  - Manual service conversion where supported
  - timed sessions with pre-session state tracking
  - restore only services/processes started by SecSwitch

The current CLI is developer-oriented. A lightweight tray application and installer will follow after runtime behavior is validated.

### Build and test

```powershell
dotnet build SecSwitch.sln
dotnet run --project src/SecSwitch.Cli -- setup
dotnet run --project src/SecSwitch.Cli -- scan
dotnet run --project src/SecSwitch.Cli -- profile show
dotnet run --project src/SecSwitch.Cli -- session start --minutes 5
```

### License

MIT. See [LICENSE](LICENSE).
