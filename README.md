# SmugglerTPSServer

C# UDP 기반 TPS 서버. 싱글 스레드.

- PostgreSQL, Visual Studio 2022

## NuGet Packages

현재 프로젝트에서 사용하는 주요 NuGet 패키지는 아래와 같습니다.

| Package | Usage |
| --- | --- |
| `Google.FlatBuffers` | FlatBuffer 기반 패킷 직렬화/역직렬화. |
| `Dapper` | DB 접근용 마이크로 ORM. |
| `Npgsql` | PostgreSQL 연결 드라이버. |

## Implemented Packets

서버 패킷 구조는 기본적으로 `[4 bytes EProtocol:int][FlatBuffer payload]` 형식입니다.

### Client -> Server

현재 서버에서 처리하는 클라이언트 패킷은 아래와 같습니다.

| Protocol | Description |
| --- | --- |
| `CS_Ping` | 즉시 RTT 측정을 수행하고 `SC_Pong`으로 응답 |
| `CL_AuthRequest` | 디바이스 키/유저명 기반 인증 요청 처리 후 세션 발급 |
| `CS_LoadCompleteRequest` | 로딩 완료 알림 처리 후 월드 진입 절차 진행 |
| `CS_MoveNotification` | 플레이어 이동 입력 큐 적재 |
| `CS_Heartbeat` | 세션 유지 및 타임아웃 갱신 |
| `CS_AttackRequest` | 공격 판정 요청 처리 |
| `CS_ChatRequest` | 채팅 메시지 수신 후 룸 전체 브로드캐스트 |

### Server -> Client

현재 서버에서 생성하거나 브로드캐스트하는 패킷은 아래와 같습니다.

| Protocol | Description |
| --- | --- |
| `SC_Pong` | `CS_Ping`에 대한 응답 |
| `LC_AuthResponse` | 인증 성공 후 세션 키/플레이어 시퀀스 전달 |
| `SC_LoadCompleteResponse` | 로딩 완료 요청 확인 응답 |
| `SC_AddNotification` | 플레이어/블록 오브젝트 추가 및 기존 월드 상태 동기화 |
| `SC_RemoveNotification` | 플레이어 제거 또는 연결 종료 알림 |
| `SC_SyncMove` | 플레이어 이동 상태 브로드캐스트 |
| `SC_ChangeStateNotification` | 사망/리스폰 등 상태 변경 알림 |
| `SC_SyncAttack` | 공격 결과 동기화 |
| `SC_SyncChat` | 룸 채팅 메시지 동기화 |

## License

This project is licensed under the MIT License.
