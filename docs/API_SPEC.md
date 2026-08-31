# API Specification

## 1. Tổng quan

Base route: `/api/auth`

| Method | Endpoint | Authentication | Mục đích |
|---|---|---|---|
| `POST` | `/register` | Anonymous | Đăng ký User. |
| `POST` | `/login` | Anonymous | Xác minh password và tạo pending challenge. |
| `POST` | `/send-otp` | Anonymous | Sinh/gửi OTP lần đầu cho pending challenge. |
| `POST` | `/verify-otp` | Anonymous | Verify OTP, consume challenge và cấp JWT. |
| `POST` | `/resend-otp` | Anonymous | Revoke challenge cũ và gửi OTP/challenge mới. |
| `GET` | `/me` | Bearer JWT | Trả hồ sơ tối thiểu của User đang active. |

Tên JSON property dùng `camelCase`. Timestamp là ISO 8601 UTC. OTP luôn là string 6 chữ số. Mọi auth response có `Cache-Control: no-store`; mọi auth POST body bị giới hạn 16 KiB.

## 2. Error response

Lỗi nghiệp vụ dùng Problem Details với `code` và `traceId`:

```json
{
  "title": "Email hoặc mật khẩu không chính xác.",
  "status": 401,
  "code": "INVALID_CREDENTIALS",
  "traceId": "<server-trace-id>"
}
```

Lỗi validation có thêm `errors` theo field. Response không chứa stack trace, SQL/SMTP detail, password, OTP, OTP HMAC, JWT key hoặc credential.

Các auth POST có thể trả:

- `400 VALIDATION_ERROR` khi DTO không hợp lệ.
- `413 REQUEST_TOO_LARGE` khi body vượt 16 KiB.
- `429 RATE_LIMITED` khi vượt policy endpoint; header `Retry-After` được trả khi limiter có metadata.
- `500 INTERNAL_ERROR` cho lỗi không dự đoán đã sanitize. Global exception boundary cũng áp dụng lỗi `500` cho `/me`.

## 3. `POST /api/auth/register`

**Authentication:** Không yêu cầu.

Tạo User mới. Password được hash trước khi lưu; endpoint không tạo OTP hoặc JWT.

### Request

```json
{
  "email": "student@example.com",
  "password": "<password>",
  "fullName": "Nguyen Van A"
}
```

| Field | Validation |
|---|---|
| `email` | Required, email hợp lệ, tối đa 254 ký tự; được trim. |
| `password` | Required, 8-128 ký tự, không chỉ có khoảng trắng. |
| `fullName` | Required, 2-100 ký tự; được trim. |

### Success — `201 Created`

```json
{
  "id": "6ba7b810-9dad-41d1-80b4-00c04fd430c8",
  "email": "student@example.com",
  "fullName": "Nguyen Van A",
  "isActive": true,
  "createdAt": "2026-08-31T10:00:00Z"
}
```

### Lỗi riêng

| Status | Code | Khi nào |
|---:|---|---|
| `409` | `EMAIL_ALREADY_REGISTERED` | Normalized email đã tồn tại. |

Rate limit: 5 request/3600 giây/IP.

## 4. `POST /api/auth/login`

**Authentication:** Không yêu cầu.

Endpoint chỉ xác minh email/password và tạo pending challenge. Nó **không sinh OTP, không gọi SMTP và không cấp JWT**.

### Request

```json
{
  "email": "student@example.com",
  "password": "<password>"
}
```

| Field | Validation |
|---|---|
| `email` | Required, email hợp lệ, tối đa 254 ký tự; được trim. |
| `password` | Required, 8-128 ký tự, không chỉ có khoảng trắng. |

### Success — `200 OK`

```json
{
  "requiresOtp": true,
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f",
  "purpose": "LOGIN",
  "otpSent": false,
  "maskedEmail": "st***@example.com",
  "flowExpiresAt": "2026-08-31T10:10:00Z"
}
```

Pending challenge chưa có `OtpHash`, `ExpiresAt` hoặc `SentAt`, vì vậy chưa thể verify.

### Lỗi riêng

| Status | Code | Khi nào |
|---:|---|---|
| `401` | `INVALID_CREDENTIALS` | Email lạ, password sai hoặc User inactive; cùng một message. |

Rate limit: 5 request/60 giây/IP.

## 5. `POST /api/auth/send-otp`

**Authentication:** Không yêu cầu Bearer token; quyền first send được ràng buộc bởi pending challenge sinh sau password verification.

Request không nhận email/UserId. Server luôn lấy người nhận từ User gắn với challenge.

### Request

```json
{
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f"
}
```

`challengeId` là UUID bắt buộc.

### Xử lý

1. Validate challenge `LOGIN`, User active, flow còn hạn và đúng pending state.
2. Sinh OTP 6 chữ số bằng CSPRNG, lưu HMAC/expiration ở prepared state.
3. Gửi OTP tạm thời qua MailKit tới `User.Email`.
4. Sau SMTP success mới set `SentAt` và trả success.

First send lần hai trên cùng challenge bị từ chối; client phải dùng resend flow sau khi OTP đã sent.

### Success — `200 OK`

```json
{
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f",
  "purpose": "LOGIN",
  "otpSent": true,
  "maskedEmail": "st***@example.com",
  "expiresAt": "2026-08-31T10:03:00Z",
  "flowExpiresAt": "2026-08-31T10:10:00Z",
  "resendAvailableAt": "2026-08-31T10:01:00Z"
}
```

### Lỗi riêng

| Status | Code | Khi nào |
|---:|---|---|
| `400` | `OTP_SEND_NOT_AVAILABLE` | Challenge lạ, invalid/revoked/consumed/expired, không còn pending hoặc đã first-send. |
| `503` | `OTP_DELIVERY_UNAVAILABLE` | SMTP hoặc finalize sent state thất bại; challenge được xử lý fail closed. |

Rate limit: 3 request/300 giây/IP.

## 6. `POST /api/auth/verify-otp`

**Authentication:** Không yêu cầu Bearer token. JWT chỉ được tạo trong success response của endpoint này.

### Request

```json
{
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f",
  "otp": "000123"
}
```

| Field | Validation |
|---|---|
| `challengeId` | UUID bắt buộc. |
| `otp` | String bắt buộc, regex `^[0-9]{6}$`. |

OTP sai định dạng bị model validation từ chối trước service và không tăng `AttemptCount`; request vẫn chịu rate limit HTTP.

### Success — `200 OK`

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresIn": 900,
  "expiresAt": "2026-08-31T10:18:00Z"
}
```

Trước khi cấp JWT, server yêu cầu challenge đã sent, đúng purpose, User active, còn hạn/lượt, chưa consumed/revoke; OTP HMAC phải khớp và `ConsumedAt` phải persist thành công.

### Lỗi riêng

| Status | Code | Message/ý nghĩa |
|---:|---|---|
| `400` | `OTP_NOT_SENT` | OTP chưa được gửi. |
| `400` | `OTP_VERIFICATION_FAILED` | OTP không chính xác hoặc challenge không hợp lệ chung. |
| `400` | `OTP_EXPIRED` | OTP hoặc authentication flow đã hết hạn. |
| `400` | `OTP_NOT_CURRENT` | Challenge đã consume/revoke/replay hoặc không còn current. |
| `400` | `OTP_MAX_ATTEMPTS` | Đã đạt tối đa 5 lần sai. |

Rate limit: 10 request/60 giây/IP, đồng thời có hard limit 5 OTP sai/challenge.

## 7. `POST /api/auth/resend-otp`

**Authentication:** Không yêu cầu Bearer token; server resolve User/email từ sent challenge hiện tại.

### Request

```json
{
  "challengeId": "b3bb189f-8bf9-4a52-a8c8-fcba5db4f88f"
}
```

`challengeId` là UUID bắt buộc. Endpoint không nhận email, UserId hoặc OTP cũ.

Challenge phải đã sent, chưa consumed/revoke/lock, còn flow và còn lượt resend. Cooldown là 60 giây từ `SentAt`. Resend revoke challenge cũ và tạo replacement với OTP mới.

### Success — `200 OK`

```json
{
  "challengeId": "51c4335b-172a-44c0-b6db-520b90e938a9",
  "purpose": "LOGIN",
  "expiresAt": "2026-08-31T10:05:00Z",
  "flowExpiresAt": "2026-08-31T10:10:00Z",
  "resendAvailableAt": "2026-08-31T10:03:00Z"
}
```

Client phải thay challenge ID cũ bằng ID mới. OTP/challenge cũ bị từ chối kể cả khi TTL cũ chưa hết.

### Lỗi riêng

| Status | Code | Khi nào |
|---:|---|---|
| `400` | `RESEND_NOT_AVAILABLE` | Challenge không hợp lệ/current, pending, consumed, revoked, locked, hết flow hoặc hết lượt resend. |
| `429` | `RESEND_COOLDOWN` | Chưa qua 60 giây; có header `Retry-After` và extension `retryAfterSeconds`. |
| `503` | `OTP_DELIVERY_UNAVAILABLE` | SMTP/finalize thất bại; replacement được best-effort revoke và challenge cũ không được phục hồi. |

Rate limit: 3 request/300 giây/IP, độc lập với cooldown và giới hạn 3 resend/flow.

## 8. `GET /api/auth/me`

**Authentication:** Bắt buộc Bearer JWT.

```http
GET /api/auth/me
Authorization: Bearer <jwt>
```

JWT middleware kiểm tra signature HS256, issuer, audience, lifetime và expiration với clock skew 30 giây. Action lấy User ID từ claim `sub` và đọc lại database để bảo đảm User còn active.

### Success — `200 OK`

```json
{
  "id": "6ba7b810-9dad-41d1-80b4-00c04fd430c8",
  "email": "student@example.com",
  "fullName": "Nguyen Van A"
}
```

### Lỗi

| Status | Code | Khi nào |
|---:|---|---|
| `401` | `UNAUTHORIZED` hoặc body rỗng | Thiếu/token không hợp lệ được JWT middleware trả Problem Details `UNAUTHORIZED`; claim `sub` không parse được bị action từ chối bằng bare `401`. |
| `403` | `ACCOUNT_INACTIVE` | Token hợp lệ nhưng User không còn tồn tại/active. |

## 9. JWT và protected flow

- Algorithm: HS256; validator chỉ cho phép HS256 và signed token.
- Issuer: `OTPAuth.API` theo configuration hiện tại.
- Audience: `OTPAuth.Client` theo configuration hiện tại.
- Lifetime: 15 phút; clock skew 30 giây.
- Signing key: `Jwt:SigningKey`, Base64 tối thiểu 256 bit và khác `Otp:HashingKey`.
- Không có refresh token hoặc server-side logout/revocation.

UI chỉ lưu access token trong `sessionStorage`; không đưa token vào URL hoặc console. Logout xóa token phía client.

## 10. Audit theo endpoint

| Tình huống | Event chính |
|---|---|
| Register thành công | `REGISTER_SUCCESS` |
| Login đúng/sai | `LOGIN_PASSWORD_SUCCESS` / `LOGIN_PASSWORD_FAILED` |
| First send prepared/sent | `OTP_SEND_REQUESTED`, `OTP_CREATED`, `OTP_SENT` |
| Delivery lỗi | `OTP_DELIVERY_FAILED` |
| Verify sai/hết hạn/replay/max attempts | `OTP_VERIFY_FAILED`, `OTP_EXPIRED`, `OTP_REPLAY_REJECTED`, `OTP_MAX_ATTEMPTS_REACHED` |
| Verify thành công/JWT | `OTP_VERIFY_SUCCESS`, `JWT_ISSUED` |
| Resend thành công/thất bại | `OTP_RESEND_SUCCESS`, `OTP_RESEND_FAILED` |

Audit không chứa password, `PasswordHash`, OTP, `OtpHash`, JWT, Authorization header, SMTP password hoặc raw exception.

## 11. Rate limiting và giới hạn hiện tại

| Endpoint | Permit | Window | Partition |
|---|---:|---:|---|
| Register | 5 | 3600 giây | Remote IP |
| Login | 5 | 60 giây | Remote IP |
| Send OTP | 3 | 300 giây | Remote IP |
| Verify OTP | 10 | 60 giây | Remote IP |
| Resend OTP | 3 | 300 giây | Remote IP |

Limiter hiện là fixed-window in-memory cho một application instance. Chưa có quota theo normalized email/User hoặc issuance budget dùng chung cho first send và resend; đây là giới hạn đã biết, không phải control đã hiện thực.
