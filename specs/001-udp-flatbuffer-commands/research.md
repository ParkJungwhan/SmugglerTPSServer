# Research Notes - 001-udp-flatbuffer-commands

## UDP 클라이언트 스택
- Decision: System.Net.Sockets 기반 `UdpClient` + CancellationToken + 타임아웃/재시도 래퍼를 서버 백그라운드 서비스에서 제공한다.
- Rationale: .NET 8에서 기본 제공, 추가 네이티브 의존성 없이 50rps 수준 처리 가능하며 SmugglerLib와 함께 Flatbuffer 버퍼를 그대로 송신할 수 있다.
- Alternatives considered: ENet 클라이언트(추가 래퍼 필요, 배포 복잡), Raw Socket 핸들링(에러/타임아웃 처리 중복 코드 증가).

## Flatbuffer 직렬화/역직렬화 전략
- Decision: `SmugglerLib/Generated/Protocol`의 Flatbuffer 타입을 사용해 명령/응답을 빌드하고 `ByteBuffer`를 그대로 UDP 페이로드로 사용한다.
- Rationale: 스펙에서 제시한 표준 스키마를 준수해 C++ UDP 서버와 호환성을 보장하고, 복호화 오류 시 스키마 버전과 필드 누락을 바로 감지할 수 있다.
- Alternatives considered: 수동 바이트 배열 매핑(버전 불일치 위험), JSON/Proto 변환(스펙 불일치, 추가 비용).

## 신뢰성/타임아웃 정책
- Decision: 요청당 `CorrelationId`를 포함하고 p95 2초 목표에 맞춰 3회 이하 재시도(예: 500ms/1s/1.5s backoff) 후 실패 기록을 남긴다.
- Rationale: UDP 무연결 특성 보완, SLA(2초) 준수, 로그 완전성(FR-005/SC-003) 확보.
- Alternatives considered: 단일 송신 후 대기(패킷 손실 시 사용자 경험 악화), 무제한 재시도(폭주 및 지연 증가).

## 로깅/저장소
- Decision: PostgreSQL에 `command_logs` 테이블을 두고 Dapper로 요청/응답 원본, 상태, 타이밍을 기록하며 UI 조회 API를 제공한다.
- Rationale: 스펙이 PostgreSQL+Dapper를 명시, 가벼운 매핑으로 50rps 로깅에도 부담이 적다.
- Alternatives considered: EF Core(마이그레이션/트래킹 오버헤드), 파일 로깅(검색/집계 곤란, 보존 정책 관리 어려움).

## 구성/비밀 관리
- Decision: UDP 엔드포인트, 포트, 타임아웃, DB 연결 문자열을 `appsettings.{Environment}.json` + user secrets/env 변수에서 주입한다.
- Rationale: 보안 원칙 준수(Constitution), 환경별 조정 용이, 테스트/운영에서 설정 변경만으로 튜닝 가능.
- Alternatives considered: 코드 상수 하드코딩(보안/배포 리스크), CLI 인자 기반만 사용(서비스/호스팅 시 번거로움).
