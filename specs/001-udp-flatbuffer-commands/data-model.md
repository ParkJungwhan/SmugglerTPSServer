# Data Model - 001-udp-flatbuffer-commands

## CommandPacket (Flatbuffer)
- Fields: CommandType(enum), Payload(typed table from SmugglerLib.Generated.Protocol), CorrelationId(Guid), Timestamp(long, unix ms)
- Relationships: Sent via UDP to remote server; response maps back via CorrelationId.
- Validation: CommandType must be known; Payload must match schema version; CorrelationId non-empty; Timestamp within clock skew window (±5m) for observability.

## CommandDispatch (Server record)
- Fields: Id(Guid), CommandType, RequestBytes(byte[]), SentAt(utc), Attempt(int), TimeoutMs(int), Status(pending/sent/failed/succeeded), ResponseId(Guid?)
- Relationships: Links to CommandResult via ResponseId/CorrelationId; one-to-many attempts per user action.
- Validation: TimeoutMs > 0 and ≤ 2000 (p95 목표), Attempt >= 1, RequestBytes length > 0.

## CommandResult (Server record)
- Fields: Id(Guid), CorrelationId(Guid), ResponseBytes(byte[]), ReceivedAt(utc), DurationMs(int), Outcome(success/fault/timeout), ErrorCode(string?), ErrorMessage(string?)
- Relationships: Tied to CommandDispatch by CorrelationId; surfaced to UI and persisted to DB.
- Validation: DurationMs >= 0; ResponseBytes length > 0 when Outcome=success; ErrorMessage required when Outcome != success.

## DB Tables (PostgreSQL)
- `command_logs`
  - Columns: id(uuid, pk), correlation_id(uuid, unique), command_type(text), request_bytes(bytea), response_bytes(bytea null), outcome(text), attempt int, timeout_ms int, sent_at timestamptz, received_at timestamptz null, duration_ms int null, error_code text null, error_message text null
  - Indexes: correlation_id unique, sent_at, outcome
  - Constraints: timeout_ms > 0 and <= 2000; attempt >= 1
- `command_events` (optional fine-grain audit)
  - Columns: id(uuid pk), correlation_id(uuid), event_type(text: dispatched|retry|timeout|received|failed), payload jsonb, occurred_at timestamptz
  - Indexes: correlation_id, occurred_at

## UI View Models
- CommandRequestView: CommandType, Parameters(form bound), TimeoutMs(default 2000), Retries(default 2)
- CommandResponseView: CorrelationId, Outcome, DurationMs, ResponsePreview(string), ErrorMessage, ReceivedAt
- HistoryRow: CommandType, Outcome, SentAt, DurationMs, Attempts

## State Transitions
1. pending -> sent (UDP 송신 성공)
2. sent -> succeeded (응답 수신) | sent -> failed (소켓 오류) | sent -> timeout (타임아웃 초과)
3. failed/timeout + retries 남음 -> pending(다음 attempt)
4. succeeded/failed/timeout -> logged (DB 기록 완료)
