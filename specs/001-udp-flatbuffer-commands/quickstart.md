# Quickstart - 001-udp-flatbuffer-commands

## Prerequisites
- .NET 8 SDK 설치 확인 (`dotnet --list-sdks`)
- PostgreSQL 인스턴스 및 연결 문자열 준비 (user secrets/env로 주입)
- UDP 대상 서버 엔드포인트/포트/Flatbuffer 프로토콜 버전 확인

## 설정
1) 서버 설정 (`TPSManager/appsettings.Development.json` 또는 환경변수)
- `Udp:Host`, `Udp:Port`, `Udp:SendTimeoutMs`, `Udp:ReceiveTimeoutMs`, `Udp:MaxRetries`
- `ConnectionStrings:CommandDb` (PostgreSQL)
- `Logging` 레벨: UDP 전송/응답 카테고리 Information 이상

2) User Secrets (개발):
```ps1
cd C:\Repo\SmugglerTPSServer\TPSManager
dotnet user-secrets set "ConnectionStrings:CommandDb" "Host=...;Username=...;Password=...;Database=..."
```

## 빌드
```ps1
cd C:\Repo\SmugglerTPSServer
 dotnet restore SmugglerServer.sln
 dotnet build TPSManager/TPSManager.csproj
 dotnet build TPSManager.Client/TPSManager.Client.csproj
```

## 실행
- 서버: `dotnet run --project TPSManager/TPSManager.csproj`
- 클라이언트(WASM dev): `dotnet run --project TPSManager.Client/TPSManager.Client.csproj`
- 필요 시 UDP 대상 서버/포트는 `appsettings.*`로 조정 후 재시작

## 테스트
```ps1
cd C:\Repo\SmugglerTPSServer
 dotnet test
```
- UDP 전송/Flatbuffer 역직렬화 테스트 케이스 추가 후 `dotnet test`로 확인
- p95 2초 목표를 위한 로드 테스트는 추후 시나리오/스크립트로 측정

## 개발 흐름
1) SmugglerLib.Generated.Protocol 스키마 확인 후 필요한 명령 UI/DTO 정의
2) 서버에 UDP 브리지 서비스/컨트롤러 추가, DB 로깅 파이프라인 구현
3) 클라이언트 페이지에서 명령 전송 폼 + 실시간 응답/로그 리스트 표시
4) 로깅/모니터링: DB에 저장된 결과 조회 API 연결 및 UI 필터 추가
