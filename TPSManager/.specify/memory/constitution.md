<!--
Sync Impact Report
Version: 1.0.0 → 2.0.0
Modified principles: Core Principles(인코딩 손상) → 코드 품질, 테스트 기준, 퍼포먼스 요건, 보안, 문서 정비
Added sections: 거버넌스, 빌드/테스트/실행 지침, 코딩 스타일(정비)
Removed sections: 인코딩 손상된 기존 항목
Templates requiring updates: .specify/templates/plan-template.md ✅, .specify/templates/spec-template.md ✅, .specify/templates/tasks-template.md ✅
Follow-up TODOs: 없음
-->

# TPSManager 헌장 (Agents)

## 핵심 원칙

### 1. 코드 품질
- MUST 모든 공용 로직을 `../SmugglerLib`에 우선 반영해 서버(`TPSManager`)와 클라이언트(`TPSManager.Client`) 간 동작 차이를 방지한다.
- MUST C# 기본 컨벤션(4-space 들여쓰기, 표준 brace 스타일, 타입/메서드 PascalCase, 로컬/파라미터 camelCase, 기존 `_leading` 필드 유지)을 지키고, 경고 없이 빌드되도록 정적 분석과 린트 결과를 정리한다.
- MUST 작은 단위로 변경을 나누고, PR/커밋 메시지에 의도와 영향을 명확히 한국어로 기록해 리뷰 가독성을 확보한다.
이유: TPS 서버 운영 도구 특성상 서버·클라이언트가 같은 규약을 공유해야 안전하게 배포되고, 리뷰 가능성이 품질 확보의 첫 단계다.

### 2. 테스트 기준
- MUST 신규/변경 기능마다 단위 테스트와 통합·계약 테스트를 우선 작성·갱신하며, `dotnet test`가 재현 가능하게 통과해야 한다.
- MUST 프로토콜·직렬화·클라이언트/서버 경계 변경 시 회귀 테스트를 추가하고, 테스트 공백이 있을 경우 명시적 근거와 보완 계획을 기록한다.
- MUST 테스트 데이터는 멱등·독립적으로 구성하여 병렬 실행 시에도 상태 충돌이 없어야 한다.
이유: TPS 관리 도구는 네트워크·직렬화 회귀가 치명적이며, 재현 가능한 테스트만이 운영 안정성을 보장한다.

### 3. 퍼포먼스 요건
- MUST 관리 API와 배치 작업은 예상 운영 부하(관리자 동시 요청 50rps 기준)에서 p95 지연 200ms 이하 또는 스펙에 정의한 목표를 충족하며, 목표 불확실 시 스펙에 명시한다.
- MUST 클라이언트(WASM)는 최초 로드 3초 이내, 상호작용 입력-응답 p95 100ms 이내를 목표로 하고, 번들 크기/네트워크 제약을 측정해 계획에 남긴다.
- MUST 성능 리스크가 있는 변경에는 프로파일/부하 테스트 계획을 포함하고, 측정 결과를 문서화하여 회귀 기준선을 남긴다.
이유: 실시간 TPS 운영 툴은 관리 응답성과 배포 속도가 게임 안정성에 직결되며, 수치 기준 없이 최적화할 수 없다.

### 4. 보안
- MUST 모든 비밀 값은 user secrets 또는 환경 변수에 저장하고, 저장소에는 어떠한 키·연결 문자열도 커밋하지 않는다.
- MUST 외부 입력(요청, 설정, 파일) 검증과 로깅을 기본으로 하고, 최소 권한 원칙으로 자격 증명·네트워크 접근을 제한한다.
- MUST 의존성 보안 업데이트와 CVE 패치를 정기적으로 확인하고, 적용 여부와 영향도를 기록한다.
이유: 관리 서버는 운영 체계상 높은 권한을 갖기에 작은 노출도 서비스 전체로 확산될 수 있다.

### 5. 문서 정비
- MUST 스펙(plan/spec/tasks)과 운영 문서에 변경 목적, 테스트/성능 결과, 보안 결정 사항을 동일 릴리스에 함께 반영한다.
- MUST 일 단위 작업 로그를 유지하고(한국어), 결정·차이·남은 위험을 기록하여 추적 가능성을 확보한다.
- MUST 새 도구/명령/스크립트가 추가되면 사용법과 전제 조건을 업데이트된 문서에 포함한다.
이유: 분산된 도구·팀 사이에서도 일관된 지식을 공유해야 운영 사고를 줄일 수 있다.

## 프로젝트 구조
- `TPSManager/`: ASP.NET Core 서버 진입점(`Program.cs`), 환경별 설정(`appsettings*.json`), 정적 자산(`wwwroot/`).
- `TPSManager.Client/`: Blazor WebAssembly 클라이언트, 페이지(`Pages/`), 공용 import(`_Imports.razor`), 정적 자산(`wwwroot/`).
- `../SmugglerLib/`: 공용 헬퍼·생성 코드·ENet 바인딩. 서버/클라이언트 모두가 사용하는 규약을 이곳에서 우선 관리한다.
- 빌드 산출물은 각 프로젝트의 `bin/`, `obj/`에 생성되며, 저장소에는 추가하지 않는다.

## 빌드·테스트·실행 지침
- 복구: `dotnet restore SmugglerServer.sln`
- 빌드: 서버 `dotnet build TPSManager/TPSManager.csproj`, 클라이언트 `dotnet build TPSManager.Client/TPSManager.Client.csproj`, 공용 라이브러리 `dotnet build ../SmugglerLib/SmugglerLib.csproj`
- 테스트: `dotnet test` (필수), 필요 시 대상 프로젝트 지정
- 실행(개발): 서버 `dotnet run --project TPSManager/TPSManager.csproj`, 클라이언트 미리보기 `dotnet run --project TPSManager.Client/TPSManager.Client.csproj`
- Codex 출력은 한국어로 작성하고, 일일 로그 파일을 생성·관리한다.

## 코딩 스타일
- C# 기본 brace 스타일과 4-space 들여쓰기 사용.
- 명명 규칙: 타입/메서드 PascalCase, 로컬/파라미터 camelCase, 기존 코드의 `_leading` 필드는 유지.
- 관련 코드는 Controller/Component/Page별로 인접 배치하고, 중복 로직은 `SmugglerLib`로 이동해 공유한다.

## 커밋 및 PR 규범
- 커밋 메시지는 간결한 명령형 한 줄과 필요한 경우 짧은 근거를 포함한다.
- PR에는 의도, 주요 변경점, 테스트 결과(명령과 상태), 관련 이슈/작업 링크, UI 변경 시 스크린샷을 포함한다.
- 빌드/테스트가 통과된 상태에서만 리뷰를 요청한다.

## 거버넌스
- 개정 절차: 변경 제안은 헌장 초안과 영향도를 명시하여 제출하며, 리뷰 후 승인 시 본 문서를 업데이트하고 버전을 올린다.
- 버전 정책: 헌장 변경은 Semantic Versioning을 따른다(MAJOR: 원칙 제거/재정의, MINOR: 새 원칙·의무 추가, PATCH: 표현 명확화). 이번 개정은 원칙 재정의로 MAJOR 상승.
- 준수 점검: 스펙·플랜 작성 시 "Constitution Check" 게이트로 핵심 원칙 충족 여부를 기록하고, 릴리스 전 체크리스트에서 테스트/보안/문서/성능 준수를 확인한다.

**Version**: 2.0.0 | **Ratified**: 2025-12-16 | **Last Amended**: 2025-12-16
