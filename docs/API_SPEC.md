# PHASE 0 - Đặc tả API

## 1. Phạm vi

Tài liệu định nghĩa contract cho ASP.NET Core Web API của hệ thống xác thực OTP. `POST /api/auth/verify-otp`, JWT, `GET /api/auth/me` và `POST /api/auth/resend-otp` đã được hiện thực đến Phase 8. Rate limiting đầy đủ và audit đầy đủ vẫn thuộc các Phase 9 và 10; các quy tắc tương ứng bên dưới là contract mục tiêu, không phải xác nhận chúng đã được hiện thực.

Các endpoint:

| Method | Path | Anonymous | Mục đích |
|---|---|---:|---|
| `POST` | `/api/auth/register` | Có | Đăng ký tài khoản. |
| `POST` | `/api/auth/login` | Có | Kiểm tra password, tạo và gửi OTP challenge. |
| `POST` | `/api/auth/verify-otp` | Có | Xác minh OTP và cấp JWT. |
| `POST` | `/api/auth/resend-otp` | Có | Vô hiệu mã cũ, tạo và gửi mã mới. |
| `GET` | `/api/auth/me` | Không | API protected để kiểm chứng JWT. |

## 2. Quy ước chung

- Chỉ phục vụ qua HTTPS ngoài local development.
- Request và success response dùng `application/json; charset=utf-8`; error response dùng `application/problem+json`.
- Tên property JSON dùng `camelCase`.
- Thời điểm trả theo ISO 8601 UTC, ví dụ `2026-08-22T13:00:00Z`.
- `challengeId` và `id` là UUID string.
- OTP là string đúng 6 chữ số, không phải number, để giữ chữ số `0` ở đầu.
- Mọi response của năm endpoint trong tài liệu, kể cả `/me` và lỗi, có `Cache-Control: no-store` và không được log body/Authorization header nhạy cảm.
- Client không được gửi `UserId`, `Purpose`, `AuthenticationFlowId`, `FlowExpiresAt`, `ResendCount`, TTL, attempts hoặc trạng thái challenge.
- Mọi response lỗi có `traceId`, nhưng không có stack trace, SQL/SMTP detail, tên class hoặc secret.
- `429 Too Many Requests` trả `Retry-After` khi server xác định được thời gian chờ.

## 3. Error format

API dùng Problem Details với extension `code` và `traceId`:

```json
{
  "type": "about:blank",
  "title": "Thông tin đăng nhập không hợp lệ.",
  "status": 401,
  "code": "INVALID_CREDENTIALS",
  "traceId": "00-example-trace-id"
}
```

Chỉ lỗi validation có danh sách field:

```json
{
  "type": "about:blank",
  "title": "Dữ liệu gửi lên không hợp lệ.",
  "status": 400,
  "code": "VALIDATION_ERROR",
  "traceId": "00-example-trace-id",
  "errors": {
    "email": ["Email không đúng định dạng."]
  }
}
```

Thông báo xác thực phải chung. Ví dụ, login không phân biệt email không tồn tại, password sai và tài khoản inactive; verify không phân biệt challenge lạ, mã sai, expired, consumed, revoked hoặc locked.

## 4. `POST /api/auth/register`

### Mục đích

Tạo User mới với password đã hash. Endpoint không tạo OTP, không đăng nhập tự động và không cấp JWT.

### Request

```http
POST /api/auth/register
Content-Type: application/json
```

```json
{
  "email": "student@example.com",
  "password": "<password>",
  "fullName": "Nguyen Van A"
}
```

| Field | Kiểu | Bắt buộc | Validation |
|---|---|---:|---|
| `email` | string | Có | Trim, format email hợp lệ, tối đa 254 ký tự; unique sau normalize. |
| `password` | string | Có | 8-128 ký tự; không trim/normalize; không log. |
| `fullName` | string | Có | Trim, 2-100 ký tự. |

Các field do server quản lý như `id`, `passwordHash`, `isActive`, `createdAt` bị bỏ qua hoặc từ chối theo DTO allowlist; không được model-bind vào entity trực tiếp.

### Xử lý

1. Validate DTO và rate limit theo IP.
2. Chuẩn hóa email và kiểm tra unique.
3. Hash password bằng `PasswordHasher`.
4. Tạo User active và audit `REGISTER_SUCCESS` trong một transaction.

### Response thành công - `201 Created`

```json
{
  "id": "6ba7b810-9dad-41d1-80b4-00c04fd430c8",
  "email": "student@example.com",
  "fullName": "Nguyen Van A",
  "isActive": true,
  "createdAt": "2026-08-22T13:00:00Z"
}
```

Response không bao giờ có password, `passwordHash`, OTP hoặc token.

### Lỗi

| Status | Code | Điều kiện |
|---:|---|---|
| `400` | `VALIDATION_ERROR` | Request sai format/range. |
| `409` | `EMAIL_ALREADY_REGISTERED` | Normalized email đã tồn tại, kể cả race tại unique index. |
| `429` | `RATE_LIMITED` | Vượt quota register theo IP. |
| `500` | `INTERNAL_ERROR` | Lỗi không dự đoán; response đã sanitize. |

Việc trả `409` giúp demo/UX rõ nhưng có thể hỗ trợ account enumeration. Thiết kế chấp nhận trade-off này trong phạm vi, đồng thời rate limit register; có thể đổi sang response chung nếu yêu cầu bảo mật triển khai cao hơn.

## 5. `POST /api/auth/login`

### Mục đích

Xác minh email/password, sau đó tạo và gửi OTP. Thành công ở endpoint này chỉ có nghĩa password step đã đúng; chưa phải xác thực hoàn tất.

### Request

```http
POST /api/auth/login
Content-Type: application/json
```

```json
{
  "email": "student@example.com",
  "password": "<password>"
}
```

| Field | Kiểu | Bắt buộc | Validation |
|---|---|---:|---|
| `email` | string | Có | Trim, format hợp lệ, tối đa 254 ký tự. |
| `password` | string | Có | 8-128 ký tự; không trim/normalize; không log. |

### Xử lý

1. Middleware rate limit thô theo IP/endpoint mà không đọc body; service validate/normalize rồi áp dụng quota theo normalized email.
2. Tìm User, kiểm tra `IsActive`, verify `PasswordHash`; email không tồn tại dùng dummy hash để giảm timing leak.
3. Nếu thất bại, ghi `LOGIN_PASSWORD_FAILED`, không tạo challenge và trả cùng một lỗi chung.
4. Nếu đúng, ghi `LOGIN_PASSWORD_SUCCESS` rồi kiểm tra quota phát OTP chung theo User. Quota này tính cả login và resend; vượt quota trả `429` mà không tạo email/challenge.
5. Revoke login challenge cũ, bắt đầu authentication flow 10 phút, tạo OTP/challenge mới với TTL tối đa 3 phút, `ResendCount = 0`, `MaxAttempts = 5` và persist `OtpHash`.
6. Gửi OTP plaintext tạm thời tới email trong database qua SMTP TLS. Nếu delivery fail, revoke challenge mới và trả `503 OTP_DELIVERY_UNAVAILABLE` đã sanitize.
7. Không tạo hoặc trả JWT. Chỉ khi email gửi thành công mới trả challenge metadata.

### Response thành công - `200 OK`

```json
{
  "requiresOtp": true,
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f",
  "purpose": "LOGIN",
  "expiresAt": "2026-08-22T13:03:00Z",
  "flowExpiresAt": "2026-08-22T13:10:00Z",
  "resendAvailableAt": "2026-08-22T13:01:00Z"
}
```

Không trả OTP, OTP hash, UserId nội bộ không cần thiết hoặc JWT. Client chuyển sang màn hình nhập OTP và giữ `challengeId` hiện tại.

### Lỗi

| Status | Code | Điều kiện |
|---:|---|---|
| `400` | `VALIDATION_ERROR` | Email/password thiếu hoặc vượt giới hạn. |
| `401` | `INVALID_CREDENTIALS` | Email lạ, password sai hoặc tài khoản inactive; cùng title/message. |
| `429` | `RATE_LIMITED` | Vượt giới hạn theo IP/email; có `Retry-After` khi có thể. |
| `503` | `OTP_DELIVERY_UNAVAILABLE` | Không gửi được OTP; server thực hiện best-effort revoke, row còn sót vẫn có TTL/flow limit. |
| `503` | `OTP_CHALLENGE_UNAVAILABLE` | Không tạo được một open challenge duy nhất sau bounded concurrency retry. |
| `500` | `INTERNAL_ERROR` | Lỗi không dự đoán đã sanitize. |

### Invariant bảo mật

- Password sai không tạo `OtpChallenge` và không phát email.
- Password đúng cũng không cấp JWT.
- Một login thành công mới làm challenge login trước mất hiệu lực.
- Email đích luôn lấy từ User trong database.
- Concurrent challenge creation được retry có giới hạn; nếu vẫn xung đột, trả `503 OTP_CHALLENGE_UNAVAILABLE`, không rơi ra SQL/concurrency detail.

## 6. `POST /api/auth/verify-otp`

### Mục đích

Xác minh OTP cho challenge login. Chỉ endpoint này được trả JWT và chỉ sau khi OTP được consume/commit đúng một lần.

### Request

```http
POST /api/auth/verify-otp
Content-Type: application/json
```

```json
{
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f",
  "otp": "<6-digit-otp>"
}
```

| Field | Kiểu | Bắt buộc | Validation |
|---|---|---:|---|
| `challengeId` | UUID string | Có | UUID hợp lệ; do server cấp. |
| `otp` | string | Có | Regex `^[0-9]{6}$`; không log. |

Request không nhận email, UserId hoặc Purpose. OTP sai định dạng bị validation từ chối và không tăng `AttemptCount`, nhưng vẫn chịu endpoint rate limiter.

### Xử lý theo thứ tự

1. Middleware rate limit thô theo IP/endpoint; sau validation, service áp dụng quota theo challenge/User.
2. Load challenge `LOGIN` và User bằng server state.
3. Từ chối nếu challenge không tồn tại, đã revoke/consume/locked hoặc User inactive.
4. Từ chối và audit `OTP_EXPIRED` nếu `now >= ExpiresAt`.
5. Tái tính HMAC và so sánh fixed-time.
6. OTP sai: tăng attempt atomically, audit `OTP_VERIFY_FAILED`; lần sai thứ 5 đồng thời revoke challenge.
7. OTP đúng: conditional update `ConsumedAt` và audit `OTP_VERIFY_SUCCESS` trong transaction.
8. Chỉ sau commit thành công mới tạo JWT TTL 15 phút.

### Response thành công - `200 OK`

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "expiresAt": "2026-08-22T13:15:00Z"
}
```

JWT tối thiểu có:

- `sub`: User ID.
- `jti`: token ID ngẫu nhiên.
- `iat`, `exp`: issued/expiration time.
- `iss`, `aud`: issuer và audience theo configuration.

Không đưa password, OTP, OTP hash, signing key hoặc thông tin nội bộ vào claims.

### Lỗi

| Status | Code | Điều kiện |
|---:|---|---|
| `400` | `VALIDATION_ERROR` | `challengeId`/OTP sai format. |
| `401` | `OTP_VERIFICATION_FAILED` | Challenge lạ, OTP sai, expired, consumed, revoked, locked hoặc User inactive; response chung. |
| `429` | `RATE_LIMITED` | Vượt endpoint limiter; hard limit 5 OTP sai/challenge vẫn độc lập. |
| `500` | `INTERNAL_ERROR` | Lỗi không dự đoán; không trả stack trace. |

Audit giữ reason code nội bộ để phân biệt sai mã, hết hạn, replay và max attempts; client không nhận chi tiết này.

### Concurrency

Hai request dùng OTP đúng đồng thời phải tranh conditional consume/`RowVersion`. Chỉ request commit `ConsumedAt` được nhận JWT; request còn lại trả `401 OTP_VERIFICATION_FAILED`. JWT không được tạo “trước để dành”.

## 7. `POST /api/auth/resend-otp`

### Mục đích

Gửi OTP hoàn toàn mới cho một password login đang chờ, đồng thời vô hiệu challenge/mã cũ.

### Request

```http
POST /api/auth/resend-otp
Content-Type: application/json
```

```json
{
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f"
}
```

| Field | Kiểu | Bắt buộc | Validation |
|---|---|---:|---|
| `challengeId` | UUID string | Có | UUID hiện tại do server cấp. |

Endpoint không nhận email, UserId, OTP cũ, `MaxAttempts` hay TTL từ client.

### Điều kiện resend

- Challenge là open challenge `LOGIN` hiện tại của User.
- Chưa consumed, chưa revoked và `AttemptCount < MaxAttempts`.
- Đã qua cooldown 60 giây từ `CreatedAt`.
- Qua rate limit theo IP và User.
- `now < FlowExpiresAt` và `ResendCount < 3`.
- Challenge OTP expired được phép resend khi flow còn hạn/lượt; challenge đã khóa, flow hết 10 phút hoặc đã resend 3 lần phải login password lại.

### Xử lý

1. Middleware limit thô theo IP; service validate/load rồi kiểm tra quota theo User, quota phát OTP chung và state/concurrency.
2. Revoke challenge cũ.
3. Sinh OTP/challenge mới, giữ `AuthenticationFlowId`/`FlowExpiresAt`, tăng `ResendCount`, đặt `CreatedAt = now`, `ExpiresAt = min(CreatedAt + 3 phút, FlowExpiresAt)` và attempt count về 0.
4. Ghi `OTP_RESEND` và `OTP_CREATED`, rồi commit.
5. Gửi OTP mới tới `User.Email` trong database, với email nêu hạn thực tế từ `ExpiresAt`; sau SMTP, reload/recheck challenge vẫn usable trước khi trả `200`.
6. Nếu delivery thất bại, không phục hồi challenge cũ, trả `503` và thực hiện best-effort revoke challenge mới. Process/compensation lỗi có thể để row open đến TTL/flow expiry.

### Response thành công - `200 OK`

```json
{
  "challengeId": "51c4335b-172a-44c0-b6db-520b90e938a9",
  "purpose": "LOGIN",
  "expiresAt": "2026-08-22T13:05:00Z",
  "flowExpiresAt": "2026-08-22T13:10:00Z",
  "resendAvailableAt": "2026-08-22T13:03:00Z"
}
```

Client phải thay challenge ID cũ bằng ID mới. Mã/challenge cũ bị từ chối ngay cả khi email chứa mã cũ đến sau email mới.

### Lỗi

| Status | Code | Điều kiện |
|---:|---|---|
| `400` | `VALIDATION_ERROR` | `challengeId` sai format. |
| `400` | `RESEND_NOT_AVAILABLE` | Challenge lạ/không hiện tại, consumed, revoked, locked, hết flow/lượt hoặc User inactive; cùng message chung. |
| `429` | `RESEND_COOLDOWN` | Chưa đủ 60 giây; trả `Retry-After`. |
| `429` | `RATE_LIMITED` | Vượt quota theo IP/User; trả `Retry-After` khi có thể. |
| `503` | `OTP_DELIVERY_UNAVAILABLE` | SMTP failure; server thực hiện best-effort revoke challenge mới. |
| `500` | `INTERNAL_ERROR` | Lỗi không dự đoán đã sanitize. |

Hai resend đồng thời phải kết thúc với tối đa một challenge open; request thua concurrency reload state và trả `400 RESEND_NOT_AVAILABLE`, không gửi/return challenge thứ hai còn hiệu lực.

## 8. `GET /api/auth/me` - protected API

### Mục đích

Chứng minh JWT authentication/authorization hoạt động và trả hồ sơ công khai của chính người dùng.

### Request

```http
GET /api/auth/me
Authorization: Bearer <jwt>
```

Endpoint không nhận UserId từ query/body. UserId luôn lấy từ claim `sub` sau khi JWT middleware xác minh token.

### Kiểm tra token

- Header dùng đúng Bearer scheme.
- Signature hợp lệ với signing key được cấu hình.
- Thuật toán đúng HS256; không chấp nhận thuật toán khác/`none`.
- Issuer và audience khớp.
- Token chưa hết hạn, với clock skew tối đa 30 giây.
- `sub` là UUID hợp lệ và User tương ứng còn `IsActive`.

Active-user policy này phải được tái sử dụng trên mọi protected endpoint để disable tài khoản có hiệu lực ngay dù JWT chưa hết hạn.

### Response thành công - `200 OK`

```json
{
  "id": "6ba7b810-9dad-41d1-80b4-00c04fd430c8",
  "email": "student@example.com",
  "fullName": "Nguyen Van A"
}
```

### Lỗi

| Status | Code | Điều kiện |
|---:|---|---|
| `401` | `UNAUTHORIZED` | Thiếu token, token sai signature/issuer/audience, malformed hoặc hết hạn. |
| `403` | `ACCOUNT_INACTIVE` | Token hợp lệ nhưng User không còn tồn tại/active. |
| `500` | `INTERNAL_ERROR` | Lỗi không dự đoán đã sanitize. |

Response `401` có `WWW-Authenticate: Bearer` nhưng không mô tả chi tiết lỗi chữ ký cho client.

## 9. JWT và logout

- Access token TTL mặc định 15 phút, không có refresh token.
- Signing key HS256 ngẫu nhiên tối thiểu 256 bit, tách khỏi OTP HMAC key và không hard-code/commit.
- Client lưu token theo cách giảm rủi ro XSS phù hợp với frontend được chọn ở phase sau; không đưa token vào URL.
- Logout của demo chỉ xóa token phía client. Token bị đánh cắp hoặc bản sao token vẫn hợp lệ đến `exp`.
- Nếu cần revoke tức thời khi logout/disable User, phải bổ sung token denylist/security stamp trong yêu cầu tương lai; không tự mở rộng Phase 0.

## 10. Rate limit và header

Giá trị khởi đầu đồng bộ với `REQUIREMENTS.md`:

| Endpoint | Giới hạn đề xuất |
|---|---|
| Register | 5 request/giờ/IP. |
| Login | 5 request/phút/IP và 10 request/15 phút/normalized email. |
| Verify OTP | 10 request/phút/IP, cộng hard limit 5 OTP sai/challenge. |
| Resend OTP | Cooldown 60 giây; 5 request/15 phút/IP và 3 request/15 phút/User. |
| Phát OTP chung | Tổng 5 OTP/15 phút/User qua cả login thành công và resend. |

Middleware chỉ áp dụng quota IP/endpoint và request-size. Quota theo normalized email/challenge/User chạy trong service sau validation/lookup, không để middleware đọc/log body password hoặc OTP. Password login thành công cũng là một lần phát OTP, vì vậy quota chung ngăn né resend limiter bằng cách gọi lại login liên tục.

Ví dụ response rate limit:

```http
HTTP/1.1 429 Too Many Requests
Retry-After: 60
Cache-Control: no-store
Content-Type: application/problem+json
```

Partition key chứa email không được ghi ra log. Deployment sau reverse proxy chỉ tin `X-Forwarded-For` từ proxy được cấu hình tin cậy.

## 11. Audit theo endpoint

| Endpoint/tình huống | Event tối thiểu |
|---|---|
| Register thành công | `REGISTER_SUCCESS` |
| Login password đúng | `LOGIN_PASSWORD_SUCCESS`, kể cả khi quota phát OTP chặn bước tiếp theo |
| Login qua quota và tạo challenge | `OTP_CREATED` |
| Login password sai/inactive/unknown | `LOGIN_PASSWORD_FAILED` |
| Verify OTP sai | `OTP_VERIFY_FAILED`; bắt buộc thêm `OTP_MAX_ATTEMPTS` khi đạt 5 |
| Verify challenge expired | Chỉ `OTP_EXPIRED` cho request đó |
| Verify thành công | `OTP_VERIFY_SUCCESS`, sau khi cấp JWT ghi `AUTHENTICATION_SUCCESS` |
| Resend thành công | `OTP_RESEND`, `OTP_CREATED` |
| SMTP lỗi | Bắt buộc `OTP_DELIVERY_FAILED` |
| Rate limit | `RATE_LIMITED` nếu không tạo log flood |

Audit không chứa email/password request thô, password hash, OTP, OTP hash, JWT, Authorization header, SMTP credential hoặc raw exception.

## 12. Swagger/OpenAPI

- Mô tả rõ login chỉ hoàn thành password step và không trả JWT.
- Khai báo Bearer security scheme cho `GET /api/auth/me`.
- Bốn auth endpoint anonymous không được gắn security requirement nhầm.
- Example dùng placeholder `<password>`, `<6-digit-otp>`, `<jwt>`; không dùng secret thật.
- Swagger UI chỉ bật theo môi trường/configuration phù hợp và không chứa credential mặc định.
- Schema response tuyệt đối không expose `PasswordHash`, `OtpHash`, `AttemptCount` hoặc internal reason code.
- Phase triển khai phải tùy chỉnh model-validation, JWT challenge và rate-limit rejection để tất cả vẫn theo Problem Details contract này thay vì body mặc định khác nhau của framework.

## 13. Ma trận yêu cầu bảo mật API

| Yêu cầu | Contract bảo đảm |
|---|---|
| Password không lộ/lưu plaintext | Request field không log; register response không trả password/hash. |
| OTP không lộ/lưu plaintext | Login/resend response chỉ có challenge metadata; verify dùng placeholder và body bị redact. |
| Expiration/single-use/replay | Verify check expiry/state và conditional consume trước JWT. |
| Max attempts | Verify có hard limit 5; resend không reset challenge đã khóa. |
| Resend an toàn | Chỉ nhận challenge ID, cooldown/rate limit, revoke cũ, trả ID mới. |
| Không JWT trước OTP | Register/login/resend schemas không có token; token chỉ có trong verify `200`. |
| JWT expiration | Response có `expiresIn`/`expiresAt`; protected endpoint validate lifetime. |
| Input validation | DTO allowlist và bảng constraint rõ cho mọi field. |
| Error handling | Problem Details chung, không stack trace/nội bộ. |
| Secret leakage | Không secret trong schema/example; key/credential lấy từ configuration an toàn. |

## 14. Vấn đề còn cần xử lý

- Tạo OpenAPI thực tế và kiểm tra response schema ở Phase triển khai phù hợp.
- Chốt issuer/audience theo môi trường và tạo key thật; giá trị không được đưa vào tài liệu/repository.
- Đo/tinh chỉnh rate limit và quota phát email với SMTP provider thật.
- Chọn client token storage khi thực hiện frontend và đánh giá XSS/CSRF tương ứng.
- Nếu yêu cầu UX cần phân biệt OTP expired với mã sai, phải security review trước khi thay lỗi chung.
- API/version tương lai cho refresh, revoke token, đổi password/email hoặc quản trị User nằm ngoài phạm vi hiện tại.
