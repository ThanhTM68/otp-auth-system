# PHASE 0 - Thiết kế database

## 1. Phạm vi và quy ước

Database dùng SQL Server, truy cập qua Entity Framework Core. Schema ban đầu `Users`, `OtpChallenges`, `AuditLogs` được tạo ở Phase 2. Refactor split-flow bổ sung migration `SupportPendingOtpChallenge`; migration cũ được giữ nguyên.

Quy ước chung:

- Tên bảng dùng dạng số nhiều: `Users`, `OtpChallenges`, `AuditLogs`.
- Primary key của User/challenge là UUID v4 (`uniqueidentifier`) do application sinh.
- Tất cả thời điểm dùng UTC và lưu bằng `datetimeoffset(7)` để không làm tròn sai quyết định ở sát `ExpiresAt`.
- Email dùng để tra cứu qua `NormalizedEmail`; server tạo giá trị chuẩn hóa thống nhất, ví dụ trim rồi `ToUpperInvariant()`.
- OTP là chuỗi khi xử lý nhưng database chỉ lưu HMAC-SHA-256 dạng `varbinary(32)`.
- Password chỉ được lưu dưới dạng format hash do ASP.NET Core `PasswordHasher` tạo.
- Mọi thay đổi schema ở các phase sau phải đi qua EF Core Migration; không tự xóa database.

## 2. Sơ đồ quan hệ

```mermaid
erDiagram
    USERS ||--o{ OTP_CHALLENGES : owns
    USERS o|--o{ AUDIT_LOGS : associated_with
    OTP_CHALLENGES o|--o{ AUDIT_LOGS : referenced_by

    USERS {
        uniqueidentifier Id PK
        nvarchar Email
        nvarchar NormalizedEmail UK
        nvarchar PasswordHash
        nvarchar FullName
        bit IsActive
        datetimeoffset CreatedAt
        rowversion RowVersion
    }

    OTP_CHALLENGES {
        uniqueidentifier Id PK
        uniqueidentifier UserId FK
        uniqueidentifier AuthenticationFlowId
        varbinary OtpHash "nullable"
        varchar Purpose
        datetimeoffset CreatedAt
        datetimeoffset ExpiresAt "nullable"
        datetimeoffset FlowExpiresAt
        datetimeoffset SentAt "nullable"
        datetimeoffset ConsumedAt "nullable"
        smallint AttemptCount
        smallint MaxAttempts
        smallint ResendCount
        bit IsRevoked
        rowversion RowVersion
    }

    AUDIT_LOGS {
        bigint Id PK
        varchar EventType
        datetimeoffset CreatedAt
        uniqueidentifier UserId "nullable"
        uniqueidentifier OtpChallengeId "nullable"
        bit Success
        varchar ReasonCode "nullable"
        varchar IpAddress "nullable"
        nvarchar UserAgent "nullable"
        varchar CorrelationId "nullable"
    }
```

## 3. Bảng `Users`

### 3.1. Các cột

| Cột | SQL Server type | Null | Default | Mô tả/quy tắc |
|---|---|---:|---|---|
| `Id` | `uniqueidentifier` | Không | Không | Primary key, UUID v4 do application sinh. |
| `Email` | `nvarchar(254)` | Không | Không | Email hiển thị đã trim; không dùng trực tiếp để so sánh unique. |
| `NormalizedEmail` | `nvarchar(254)` | Không | Không | Giá trị chuẩn hóa để lookup/unique không phân biệt hoa thường. |
| `PasswordHash` | `nvarchar(512)` | Không | Không | Format hash của `PasswordHasher`; không phải password plaintext. |
| `FullName` | `nvarchar(100)` | Không | Không | Tên đã trim, từ 2 đến 100 ký tự. |
| `IsActive` | `bit` | Không | `1` | Tài khoản có được phép bắt đầu/hoàn tất login hay không. |
| `CreatedAt` | `datetimeoffset(7)` | Không | Không | Thời điểm tạo UTC, không thay đổi. |
| `RowVersion` | `rowversion` | Không | SQL Server | EF Core concurrency token. |

`NormalizedEmail` là cột bổ sung ngoài danh sách tối thiểu để uniqueness không phụ thuộc hoàn toàn vào cách viết hoa/thường của input hoặc collation mặc định. `Email` vẫn được giữ để hiển thị.

### 3.2. Key, index và constraint

- `PK_Users` trên `Id`.
- Unique index `UX_Users_NormalizedEmail` trên `NormalizedEmail`.
- Check constraint: `Email` và `NormalizedEmail` dài từ 3 đến 254 ký tự; `FullName` sau trim dài từ 2 đến 100 ký tự. Đây chỉ là hàng rào cuối; format/normalization vẫn được validate ở application.
- Không có cột `Password` và không expose `PasswordHash` qua DTO/response.
- Không hard-delete User trong phạm vi hiện tại; vô hiệu hóa bằng `IsActive = 0`.

Unique index phải xử lý race của hai request đăng ký cùng email. Application lookup trước để trả lỗi thân thiện, nhưng database mới là nguồn quyết định cuối cùng.

## 4. Bảng `OtpChallenges`

### 4.1. Các cột

| Cột | SQL Server type | Null | Default | Mô tả/quy tắc |
|---|---|---:|---|---|
| `Id` | `uniqueidentifier` | Không | Không | Primary key và `challengeId` opaque trả cho client. |
| `UserId` | `uniqueidentifier` | Không | Không | FK tới `Users.Id`; không lấy từ client khi verify/resend. |
| `AuthenticationFlowId` | `uniqueidentifier` | Không | Không | ID giữ nguyên qua challenge đầu và tối đa 3 challenge resend. |
| `OtpHash` | `varbinary(32)` | Có | `NULL` | HMAC-SHA-256 khi OTP đã được chuẩn bị; pending challenge chưa có OTP để hash. |
| `Purpose` | `varchar(32)` | Không | Không | Hiện tại chỉ có mã cố định `LOGIN`. |
| `CreatedAt` | `datetimeoffset(7)` | Không | Không | Thời điểm tạo challenge/pre-auth flow theo UTC. |
| `ExpiresAt` | `datetimeoffset(7)` | Có | `NULL` | Hạn OTP; null ở pending, được tính khi OTP được chuẩn bị. |
| `FlowExpiresAt` | `datetimeoffset(7)` | Không | Không | Hạn tuyệt đối của password step, mặc định 10 phút từ challenge đầu. |
| `SentAt` | `datetimeoffset(7)` | Có | `NULL` | Chỉ được set sau SMTP success; là mốc cooldown và bằng chứng OTP đã thực sự gửi. |
| `ConsumedAt` | `datetimeoffset(7)` | Có | `NULL` | Được set đúng một lần khi verify thành công. |
| `AttemptCount` | `smallint` | Không | `0` | Số lần OTP đúng format nhưng không khớp. |
| `MaxAttempts` | `smallint` | Không | `5` | Tối đa 5 lần sai theo SR-13. |
| `ResendCount` | `smallint` | Không | `0` | Số lần resend trong flow; giữ/tăng qua row mới, tối đa 3. |
| `IsRevoked` | `bit` | Không | `0` | Vô hiệu do login/resend mới, đạt max attempts hoặc delivery failure. |
| `RowVersion` | `rowversion` | Không | SQL Server | Chống lost update, double consume và race với resend. |

Không cần lưu OTP plaintext hoặc cờ `IsExpired`:

- Cooldown được tính từ `SentAt` của sent challenge hiện tại.
- Expiration là trạng thái dẫn xuất từ `ExpiresAt`; pending chưa có expiration OTP.
- `FlowExpiresAt` và `ResendCount` ngăn resend kéo dài password step vô hạn; resend copy flow ID/hạn cũ và tăng count.
- Nguyên nhân revoke được ghi bằng audit `EventType`/`ReasonCode` thay vì thêm nhiều cờ trạng thái.

### 4.2. HMAC của OTP

Do OTP chỉ có một triệu khả năng, raw SHA-256 có thể bị thử hết offline nếu database lộ. Giá trị đề xuất:

```text
OtpHash = HMAC-SHA-256(
    OtpHashingKey,
    CanonicalEncode(AuthenticationFlowId, ChallengeId, UserId, Purpose, Otp)
)
```

- `OtpHashingKey` là secret ngẫu nhiên riêng tối thiểu 256 bit, không dùng chung JWT signing key và không lưu trong database/source.
- `CanonicalEncode` phải có định dạng không nhập nhằng, không ghép chuỗi tùy ý.
- Khi verify, server tái tính HMAC và dùng fixed-time comparison.
- Rotation key phải giữ khả năng verify challenge còn trong TTL hoặc revoke chúng có chủ đích.

### 4.3. Key, index, foreign key và constraint

- `PK_OtpChallenges` trên `Id`.
- `FK_OtpChallenges_Users_UserId`: `UserId -> Users.Id`, delete behavior `NO ACTION`.
- Index `IX_OtpChallenges_UserId_Purpose_CreatedAt` trên `(UserId, Purpose, CreatedAt DESC)` để tra cứu lịch sử.
- Index `IX_OtpChallenges_AuthenticationFlowId_CreatedAt` trên `(AuthenticationFlowId, CreatedAt)` để audit một flow.
- Filtered unique index `UX_OtpChallenges_UserId_Purpose_Open` trên `(UserId, Purpose)` với filter:

```sql
WHERE IsRevoked = 0 AND ConsumedAt IS NULL
```

Filtered index không thể dùng đồng hồ hiện tại, vì vậy challenge đã hết hạn nhưng chưa revoke vẫn được coi là “open” đối với index. Login/resend phải revoke row open cũ trong cùng transaction trước khi insert row mới.

Check constraint đã hiện thực trong model/migration:

```text
Purpose IN ('LOGIN')
ExpiresAt IS NULL OR (ExpiresAt > CreatedAt AND ExpiresAt <= FlowExpiresAt)
(OtpHash IS NULL AND ExpiresAt IS NULL AND SentAt IS NULL)
OR (OtpHash IS NOT NULL AND DATALENGTH(OtpHash) = 32 AND ExpiresAt IS NOT NULL
    AND (SentAt IS NULL OR (SentAt >= CreatedAt AND SentAt < ExpiresAt)))
AttemptCount >= 0 AND AttemptCount <= MaxAttempts
MaxAttempts >= 1 AND MaxAttempts <= 5
ResendCount >= 0 AND ResendCount <= 3
ConsumedAt IS NULL OR (SentAt IS NOT NULL AND ConsumedAt >= SentAt AND ConsumedAt < ExpiresAt)
```

Giới hạn `MaxAttempts <= 5` bảo đảm cấu hình không vô tình yếu hơn SR-13. Các invariant `FlowExpiresAt <= CreatedAt + 10 phút`, đạt max-attempt thì phải revoke, và tổ hợp consumed/revoked hiện vẫn do service enforce; đây là phần defense-in-depth còn lại trong `SECURITY_REVIEW.md`. Khi cần thêm purpose phải dùng migration cập nhật constraint.

### 4.4. Trạng thái challenge

| Trạng thái logic | Điều kiện | Có thể verify? | Có thể resend? |
|---|---|---:|---:|
| Pending | `OtpHash`, `ExpiresAt`, `SentAt` đều null; password đã đúng nhưng chưa bấm gửi | Không | Không; chỉ first send |
| Prepared | Có `OtpHash`/`ExpiresAt`, `SentAt` null trong khi delivery/finalize | Không | Không |
| Usable, còn lượt resend | Chưa revoke/consume/khóa; `now < ExpiresAt`, `now < FlowExpiresAt`, `ResendCount < 3` | Có | Có sau cooldown/rate limit |
| Usable, đã hết lượt resend | Chưa revoke/consume/khóa; `now < ExpiresAt`, `now < FlowExpiresAt`, `ResendCount = 3` | Có | Không |
| OTP expired, còn lượt resend | Chưa revoke/consume/khóa; `now >= ExpiresAt`, `now < FlowExpiresAt`, `ResendCount < 3` | Không | Có sau cooldown/rate limit |
| OTP expired, hết lượt resend | Chưa revoke/consume/khóa; `now >= ExpiresAt`, `now < FlowExpiresAt`, `ResendCount = 3` | Không | Không |
| Flow expired | `now >= FlowExpiresAt` | Không | Không; phải login password lại |
| Locked/revoked | `IsRevoked = 1`, gồm đạt 5 lần sai | Không | Không; phải login password lại |
| Consumed | `ConsumedAt != NULL` | Không | Không |

Khi nhiều điều kiện cùng đúng, trạng thái terminal `Consumed/Revoked` và `Flow expired` được ưu tiên. `ResendCount = 3` không tự revoke và không làm OTP hiện tại mất hiệu lực; nó chỉ cấm tạo lần resend thứ 4.

Các chuyển trạng thái hợp lệ:

```text
Pending -> Prepared                      (first send bắt đầu)
Prepared -> Sent                         (SMTP và finalize thành công)
Pending/Prepared/Sent -> Revoked         (login mới/delivery failure)
Sent -> Consumed                         (OTP đúng)
Sent -> Revoked at AttemptCount = 5      (quá nhiều OTP sai)
Sent -> OTP Expired -> Revoked           (resend trong flow hoặc cleanup)
Flow Expired -> Revoked                  (login mới hoặc cleanup)
```

Không có chuyển từ Consumed/Revoked về Open. Resend luôn tạo row mới.

## 5. Bảng `AuditLogs`

### 5.1. Mục tiêu

AuditLog là nhật ký bảo mật dạng append-only, phục vụ điều tra và chứng minh các control. Bảng tuyệt đối không chứa password, password hash, OTP, OTP hash, JWT, Authorization header, secret, raw request/response body hoặc raw exception.

### 5.2. Các cột

| Cột | SQL Server type | Null | Mô tả/quy tắc |
|---|---|---:|---|
| `Id` | `bigint IDENTITY` | Không | Primary key tăng dần. |
| `EventType` | `varchar(64)` | Không | Mã sự kiện từ allowlist. |
| `CreatedAt` | `datetimeoffset(7)` | Không | Thời điểm UTC phía server. |
| `UserId` | `uniqueidentifier` | Có | User liên quan; null nếu login bằng email không tồn tại. |
| `OtpChallengeId` | `uniqueidentifier` | Có | ID tham chiếu thông tin bất biến; không tạo physical FK để purge challenge không sửa audit. |
| `Success` | `bit` | Không | Kết quả thành công/thất bại của event. |
| `ReasonCode` | `varchar(64)` | Có | Mã lý do nội bộ từ allowlist; không chứa input/exception tự do. |
| `IpAddress` | `varchar(45)` | Có | IPv4/IPv6 sau xử lý trusted proxy; là dữ liệu cá nhân. |
| `UserAgent` | `nvarchar(256)` | Có | User-Agent được giới hạn độ dài; phải sanitize trước khi ghi/hiển thị. |
| `CorrelationId` | `varchar(64)` | Có | ID do server tạo/validate nghiêm ngặt để liên kết với application log đã sanitize. |

Không thêm cột `Details`, `Message` hoặc JSON tự do ở thiết kế ban đầu vì chúng dễ trở thành đường rò rỉ dữ liệu bí mật. Nếu phase sau thực sự cần metadata, phải dùng allowlist key/value và security review riêng.

### 5.3. Key, index và foreign key

- `PK_AuditLogs` trên `Id`.
- `FK_AuditLogs_Users_UserId` nullable, delete behavior `NO ACTION` vì không hard-delete User trong phạm vi.
- `OtpChallengeId` là logical/informational reference, không có physical FK. Nhờ vậy việc purge challenge không update hoặc xóa AuditLog append-only.
- Index `IX_AuditLogs_CreatedAt` trên `CreatedAt DESC`.
- Index `IX_AuditLogs_UserId_CreatedAt` trên `(UserId, CreatedAt DESC)`.
- Index `IX_AuditLogs_EventType_CreatedAt` trên `(EventType, CreatedAt DESC)`.
- Có thể thêm index theo `OtpChallengeId` nếu truy vấn điều tra cần; tránh index quá mức trong bài demo.

### 5.4. Event bắt buộc

| EventType | Success thường dùng | Khi ghi |
|---|---|---|
| `REGISTER_SUCCESS` | `true` | User được tạo thành công. |
| `LOGIN_PASSWORD_SUCCESS` | `true` | Email/password đúng, trước bước OTP. |
| `LOGIN_PASSWORD_FAILED` | `false` | Email/password sai hoặc tài khoản inactive; response client vẫn giống nhau. |
| `OTP_SEND_REQUESTED` | `true` | First-send hợp lệ bắt đầu; không ghi OTP/email đầy đủ. |
| `OTP_CREATED` | `true` | OTP HMAC/expiration đã được persist; không ghi OTP. |
| `OTP_SENT` | `true` | SMTP thành công và `SentAt` đã được commit. |
| `OTP_VERIFY_FAILED` | `false` | OTP không khớp hoặc challenge bị replay/revoke/lock/not found; expired dùng event riêng. |
| `OTP_EXPIRED` | `false` | Verify challenge tại/sau `ExpiresAt`. |
| `OTP_VERIFY_SUCCESS` | `true` | `ConsumedAt` được commit thành công. |
| `OTP_RESEND_SUCCESS` | `true` | Challenge mới đã gửi email thành công. |

Policy hiện thực: lần sai thứ 5 ghi thêm `OTP_MAX_ATTEMPTS_REACHED`; SMTP failure ghi `OTP_DELIVERY_FAILED`; JWT được cấp ghi `JWT_ISSUED`; OTP đã consume ghi `OTP_REPLAY_REJECTED`; resend không thành công ghi `OTP_RESEND_FAILED` với reason code allowlist. `OTP_EXPIRED` được ghi một lần cho lần verify expired và không ghi thêm `OTP_VERIFY_FAILED` cho cùng request. Event/reason code là hằng số nội bộ, không lấy trực tiếp từ dữ liệu client.

Khi client gửi một challenge ID không tồn tại, audit dùng `ReasonCode = CHALLENGE_NOT_FOUND` nhưng để `OtpChallengeId = NULL`; không sao chép identifier tùy ý từ request vào AuditLog.

## 6. Transaction và tính nhất quán

### 6.1. Register

Transaction chứa insert User và `REGISTER_SUCCESS`. Unique index xử lý hai register đồng thời. Không ghi audit success nếu transaction User không commit.

### 6.2. Login password đúng / tạo pending challenge

Trong transaction ngắn với isolation phù hợp:

1. Revoke mọi open `LOGIN` challenge của User.
2. Insert pending challenge mới với `OtpHash`, `ExpiresAt`, `SentAt` đều null.
3. Insert `LOGIN_PASSWORD_SUCCESS`.
4. Commit và trả response; không gọi SMTP/JWT.

### 6.3. Gửi OTP lần đầu

Chỉ pending challenge hợp lệ được chuyển sang prepared. Server sinh OTP/HMAC và expiration, persist prepared state cùng `OTP_SEND_REQUESTED`/`OTP_CREATED`, gọi SMTP ngoài transaction, rồi chỉ sau success mới set `SentAt` và ghi `OTP_SENT`. Nếu SMTP hoặc finalize thất bại, operation fail closed/revoke và ghi `OTP_DELIVERY_FAILED`; client không nhận success giả. Gọi first-send lần hai không được chuyển thành resend.

### 6.4. Verify OTP sai

- Tăng `AttemptCount`, set revoke khi đạt max và ghi audit trong một transaction ngắn có `RowVersion` optimistic concurrency.
- Mỗi request OTP sai hợp lệ phải được tính đúng một lần; không được lost update.
- Khi tăng từ 4 lên 5, đồng thời đặt `IsRevoked = 1` và ghi audit.
- Khi gặp concurrency conflict, rollback, reload và đánh giá lại toàn bộ state với cùng request; tiếp tục tới khi update commit, challenge đã terminal hoặc request bị hủy. Không tự coi là thành công và không để lost update.

### 6.5. Verify OTP đúng

Conditional update chỉ được thành công nếu challenge vẫn chưa consumed/revoked, attempts dưới max, chưa hết hạn, đúng purpose và User active. `ConsumedAt` cùng `OTP_VERIFY_SUCCESS` được commit trong một transaction. JWT chỉ được tạo/trả sau commit; concurrency loser không được cấp token.

### 6.6. Resend

Trong transaction ngắn:

1. Xác nhận request trỏ tới open challenge hiện tại và trạng thái cho phép resend.
2. Yêu cầu challenge đã sent và kiểm tra cooldown/rate-limit từ `SentAt`.
3. Revoke challenge cũ.
4. Insert prepared replacement với OTP HMAC hoàn toàn mới; copy `AuthenticationFlowId`/`FlowExpiresAt`, tăng `ResendCount`, và cắt `ExpiresAt` tại flow expiry.
5. Commit prepared replacement/`OTP_CREATED` rồi gửi SMTP; sau delivery thành công set `SentAt` và ghi `OTP_SENT`/`OTP_RESEND_SUCCESS`.
6. Nếu delivery/finalize fail, ghi `OTP_RESEND_FAILED`/`OTP_DELIVERY_FAILED` và fail closed; challenge cũ vẫn bị revoke để OTP cũ không hồi sinh.

`Serializable` cho đoạn revoke/insert ngắn, `RowVersion` và filtered unique index tạo ba lớp bảo vệ dễ giải thích cho bản demo. Unique/concurrency exception phải được ánh xạ sang lỗi an toàn, không trả SQL detail.

## 7. Truy vấn và invariant quan trọng

- Lookup User chỉ qua `NormalizedEmail` có unique index.
- Lookup verify/resend qua `OtpChallenges.Id`; không query bằng OTP hash. “Current/latest” được xác định trước hết bởi open row duy nhất, không chỉ dựa vào timestamp; lịch sử dùng `(CreatedAt, Id)` làm tie-breaker.
- Challenge usable khi và chỉ khi:

```text
Purpose == LOGIN
AND IsRevoked == false
AND ConsumedAt == null
AND AttemptCount < MaxAttempts
AND OtpHash != null
AND SentAt != null
AND ExpiresAt != null
AND now < ExpiresAt
AND now < FlowExpiresAt
AND User.IsActive == true
```

- `challengeId` là identifier khó đoán, không phải secret thay thế OTP.
- Địa chỉ email nhận OTP luôn lấy qua quan hệ `OtpChallenge.User.Email`; `/send-otp` và `/resend-otp` không nhận email quyết định người nhận.
- Không dựa vào thứ tự `Id` để xác định mới nhất; dùng `CreatedAt` và điều kiện open.

## 8. Bảo vệ dữ liệu

| Dữ liệu | Có lưu? | Bảo vệ |
|---|---:|---|
| Password plaintext | Không | Chỉ tồn tại trong request/memory ngắn; không log; HTTPS. |
| Password hash | Có, `Users.PasswordHash` | Quyền DB tối thiểu; không trả qua API/log. |
| OTP plaintext | Không | Chỉ tồn tại tạm để HMAC và gửi SMTP; không log/audit. |
| OTP HMAC | Có, `OtpChallenges.OtpHash` | Key tách khỏi DB/source; không expose. |
| JWT | Không lưu trong thiết kế hiện tại | Chỉ trả qua HTTPS; không log; TTL 15 phút. |
| SMTP/JWT/DB/HMAC secrets | Không lưu trong bảng/source | Configuration an toàn, User Secrets/environment/secret store. |
| IP | Có thể lưu trong AuditLogs | Parse thành địa chỉ IP hợp lệ, phân quyền và retention vì là dữ liệu cá nhân. |

SQL Server backup và connection cũng cần encryption/quyền truy cập phù hợp trong môi trường thật. Điều này là trách nhiệm vận hành, không thay thế application controls.

## 9. Dọn dữ liệu và retention

- Phase 0 không tạo cleanup job và không xóa dữ liệu.
- Challenge expired/revoked/consumed có thể được giữ ngắn hạn để test/audit rồi purge bằng một chính sách được phê duyệt ở phase sau.
- Khi purge `OtpChallenges`, `AuditLogs.OtpChallengeId` vẫn giữ giá trị informational vì không có physical FK; AuditLog không bị update.
- AuditLogs nên có retention dài hơn challenge nhưng phải cân bằng yêu cầu môn học, dung lượng và dữ liệu cá nhân.
- Không hard-delete User trong phạm vi; `IsActive` phục vụ vô hiệu hóa.

## 10. Đối chiếu yêu cầu bảo mật

- SR-01..03: `Users` chỉ có `PasswordHash`, không có password/log plaintext.
- SR-04..07: OTP 6 số do CSPRNG; chỉ HMAC được lưu; schema audit không có OTP.
- SR-08..12: `SentAt`, `ExpiresAt`, `ConsumedAt`, `RowVersion` và conditional transaction bảo đảm chỉ mã đã gửi mới verify, expiration/single-use/replay protection.
- SR-13..14: `AttemptCount`, `MaxAttempts <= 5`, revoke khi đạt 5.
- SR-15..17: resend tạo row mới, revoke row cũ, `SentAt` làm mốc cooldown 60 giây; flow expiry/resend count là lớp bổ sung chống kéo dài vô hạn.
- SR-18: index hỗ trợ partition/check nhanh; rate limiter nằm ở application.
- SR-19..21: database không cấp token; `ConsumedAt` commit là điều kiện cho JwtTokenService.
- SR-22..23: `AuditLogs` hỗ trợ đủ event và không có cột secret/raw payload.
- SR-24..26: constraints là lớp cuối; application vẫn validate và sanitize error.
- SR-27: không có bảng/cột lưu application secrets.

## 11. Quyết định thiết kế quan trọng

- Bổ sung `NormalizedEmail` và unique index để bảo vệ uniqueness đúng khi concurrent.
- Bổ sung `RowVersion` cho User/challenge để xử lý optimistic concurrency.
- Bổ sung `AuthenticationFlowId`, `FlowExpiresAt` 10 phút và `ResendCount` tối đa 3 cho resend an toàn.
- Dùng `varbinary(32)` HMAC-SHA-256 có key riêng cho OTP.
- Migration `SupportPendingOtpChallenge` làm `OtpHash`/`ExpiresAt` nullable, thêm `SentAt`, backfill row OTP cũ bằng `SentAt = CreatedAt` và cập nhật state constraints; migration cũ không bị sửa.
- Chỉ một open challenge trên mỗi `(UserId, Purpose)`.
- Expired là trạng thái dẫn xuất; khi tạo mới phải revoke open row cũ.
- Resend tạo row mới thay vì tái sử dụng/cập nhật OTP hash của row cũ, giúp audit và chống mã cũ rõ ràng.
- Audit dùng field cấu trúc và allowlist, không có trường text/JSON tự do.
- Transaction không bao quanh SMTP; delivery failure dùng compensation best-effort và bị giới hạn bởi TTL/flow expiry nếu compensation không chạy được.

## 12. Vấn đề còn cần xử lý

- Chốt retention cụ thể cho challenge/audit và quyền thực hiện purge mà không sửa AuditLog.
- Chốt cách rotation `OtpHashingKey` trong cửa sổ challenge còn TTL.
- Kiểm tra filtered index, constraints và isolation thật bằng integration test SQL Server ở Phase 2/11.
- Chốt database encryption, backup và least-privilege account cho môi trường triển khai.
- Tất cả entity/configuration/migration chỉ được tạo khi người dùng yêu cầu đúng phase tiếp theo.
