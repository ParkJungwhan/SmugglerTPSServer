# Implementation Plan: 001-udp-flatbuffer-commands

**Branch**: `001-udp-flatbuffer-commands` | **Date**: 2025-12-18 | **Spec**: C:\Repo\SmugglerTPSServer\specs\001-udp-flatbuffer-commands\spec.md  
**Input**: Feature specification from `/specs/001-udp-flatbuffer-commands/spec.md`

## Summary

Blazor UI에서 기존 SmugglerLib Flatbuffer 프로토콜을 이용해 UDP 서버로 명령을 송신하고 응답을 수신하는 브리지 기능을 추가한다. 서버 측은 UDP 소켓/ENet 클라이언트를 통해 명령을 직렬화·역직렬화하고, UI는 결과/에러를 실시간으로 표시하며 모든 요청/응답을 PostgreSQL에 기록한다. 성능 목표는 p95 2초 이내 응답, 50 rps 지속 처리, 로그 100% 보존이다.

## Technical Context

**Language/Version**: C# / .NET 8.0  
**Primary Dependencies**: ASP.NET Core (서버), Blazor WebAssembly (클라이언트), SmugglerLib.Generated.Protocol (Flatbuffer), System.Net.Sockets/ENet(UDP), Dapper + Npgsql (DB)  
**Storage**: PostgreSQL (명령/응답 로그, 메타데이터)  
**Testing**: `dotnet test` (서버/클라이언트/SmugglerLib 단위·통합 테스트)  
**Target Platform**: Windows/Linux 서버 + WASM 클라이언트  
**Project Type**: 웹(ASP.NET Core API + Blazor WASM) + 공유 라이브러리(SmugglerLib)  
**Performance Goals**: p95 ≤ 2초(명령 왕복), 50 rps 이상 지속, DB 기록 누락 0건  
**Constraints**: Flatbuffer 스키마 준수, UDP 무연결 특성 대응(재시도/타임아웃), 비밀키/엔드포인트 환경설정 분리  
**Scale/Scope**: 운영자용 도구, 동시 수십 명 UI 사용 + 백엔드 50 rps 수준

## Constitution Check

- 코드 원칙: 공용 로직은 `../SmugglerLib` 활용 및 후방 호환성 유지, 보안/문서화 요구 준수.
- 테스트: dotnet test 가능 상태 유지, UDP/Flatbuffer/계약 테스트 계획 포함.
- 퍼포먼스: UDP 왕복 p95 2초, 50rps 목표에 맞춰 측정·로그 기준 설정.
- 보안: 비밀값은 user secrets/env, 최소 권한 원칙.
- 문서: plan/spec/research/data-model/quickstart/contracts 산출물과 로그 가시성 확보.

## Project Structure

```text
specs/001-udp-flatbuffer-commands/
|-- plan.md              # 구현 계획
|-- research.md          # 기술 선택/불확실성 해소 결과
|-- data-model.md        # 엔터티/상태/검증 정의
|-- quickstart.md        # 실행/개발 가이드
|-- contracts/           # API/프로토콜 계약(OpenAPI 등)
|-- tasks.md             # Phase 2 산출물 (추후)
`-- spec.md              # 기능 스펙

TPSManager/              # ASP.NET Core 서버 (UDP 브리지 API, DB 로깅)
TPSManager.Client/       # Blazor WASM 클라이언트 (명령 전송/응답 표시)
SmugglerLib/             # Flatbuffer/ENet/공유 헬퍼, 프로토콜 정의
```

**Structure Decision**: 서버(ASP.NET Core) + 클라이언트(Blazor WASM) + 공유 라이브러리(SmugglerLib) 구조를 유지하고, 문서 산출물은 `specs/001-udp-flatbuffer-commands/` 아래에 배치한다. API 계약은 `contracts/`에 OpenAPI로 정의하며 DB 스키마/로그 모델은 `data-model.md`에 정리한다.

## Complexity Tracking

(필요 시 위반 사항을 여기에 기록)
