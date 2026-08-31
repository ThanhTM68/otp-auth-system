# Yêu cầu hệ thống OTP Authentication

## 1. Mục tiêu và phạm vi

Hệ thống là ứng dụng demo xác thực người dùng theo luồng:

```text
Email + Password
→ Pending challenge
→ Người dùng yêu cầu gửi OTP
→ Verify OTP
→ Consume challenge
→ JWT
→ Protected API
```

Password đúng chỉ hoàn thành bước xác minh thứ nhất. Hệ thống chỉ coi người dùng đã xác thực sau khi OTP hợp lệ được consume và JWT được cấp.

### Trong phạm vi

- Đăng ký bằng email, mật khẩu và họ tên.
- Đăng nhập bằng email và mật khẩu.
- Tạo pending challenge sau khi mật khẩu đúng.
- Sinh và gửi OTP đăng nhập qua Gmail SMTP khi người dùng yêu cầu.
- Xác minh, hết hạn, giới hạn số lần thử, single-use và chống replay OTP.
- Gửi lại OTP với cooldown, flow expiry và giới hạn số lần resend.
- Cấp JWT và truy cập API protected.
- Rate limiting, audit logging và frontend demo cùng origin.
- Logout phía client.

### Ngoài phạm vi

- Refresh token và thu hồi JWT phía server.
- Khôi phục mật khẩu, đổi email, quản trị tài khoản và Social Login.
- SMS OTP, OAuth Server và đăng ký thiết bị MFA.
- Microservices, CQRS, Event Sourcing, Redis và message broker.

## 2. Actor và use case

| Actor | Use case chính |
|---|---|
| Khách chưa xác thực | Đăng ký, login password, yêu cầu gửi OTP, verify OTP và resend OTP. |
| Người dùng đã xác thực | Truy cập `GET /api/auth/me` và logout ở client. |
| Gmail SMTP | Nhận email OTP từ `EmailService`. |
| Người vận hành | Cấu hình database/secrets và kiểm tra audit log. |

| ID | Use case | Kết quả thành công |
|---|---|---|
| UC-01 | Register | User được lưu với `PasswordHash`; không có OTP/JWT. |
| UC-02 | Password login | Tạo pending challenge; chưa sinh/gửi OTP và chưa có JWT. |
| UC-03 | First send OTP | OTP được gửi tới email lấy từ User của challenge. |
| UC-04 | Verify OTP | Challenge được consume đúng một lần và JWT được cấp. |
| UC-05 | Resend OTP | Challenge cũ bị revoke, challenge/OTP mới được tạo và gửi. |
| UC-06 | Protected resource | JWT hợp lệ và User active được truy cập hồ sơ của chính mình. |
| UC-07 | Logout | JWT bị xóa khỏi `sessionStorage` của tab. |

## 3. Functional Requirements

| ID | Yêu cầu đã hiện thực |
|---|---|
| FR-01 | `POST /api/auth/register` phải validate input, chuẩn hóa email, hash password và tạo User active. |
| FR-02 | Email đã chuẩn hóa phải duy nhất; duplicate trả lỗi `409`. |
| FR-03 | `POST /api/auth/login` phải kiểm tra User active và `PasswordHash`; email lạ, password sai và User inactive dùng cùng lỗi chung. |
| FR-04 | Login thành công phải revoke open login challenge cũ và tạo pending challenge mới. Login không sinh OTP, không gọi SMTP và không cấp JWT. |
| FR-05 | `POST /api/auth/send-otp` chỉ nhận `challengeId`. Server phải lấy User/email từ challenge và chỉ cho first send trên pending challenge hợp lệ. |
| FR-06 | OTP phải là chuỗi đúng 6 chữ số, cho phép số `0` ở đầu, sinh bằng CSPRNG và chỉ lưu HMAC-SHA-256. |
| FR-07 | OTP có TTL 3 phút; pre-authentication flow có TTL 10 phút. |
| FR-08 | `POST /api/auth/verify-otp` chỉ chấp nhận challenge đã sent, còn hạn, chưa revoke/consume và còn lượt thử. |
| FR-09 | OTP sai phải tăng `AttemptCount`; lần sai thứ 5 revoke challenge. |
| FR-10 | OTP đúng phải persist `ConsumedAt` trước khi tạo JWT. Replay hoặc concurrent consume thua phải bị từ chối. |
| FR-11 | `POST /api/auth/resend-otp` chỉ nhận challenge ID hiện tại, yêu cầu challenge đã sent và qua cooldown 60 giây. |
| FR-12 | Resend phải revoke challenge cũ, tạo OTP/challenge mới, giữ cùng authentication flow và tăng `ResendCount`; tối đa 3 lần resend/flow. |
| FR-13 | Gửi email lỗi hoặc không finalize được sent state phải fail closed và không trả thành công giả. |
| FR-14 | JWT access token chỉ được trả từ verify OTP thành công; TTL 15 phút. |
| FR-15 | `GET /api/auth/me` phải yêu cầu Bearer JWT hợp lệ và kiểm tra User vẫn active trong database. |
| FR-16 | Register, login, send OTP, verify OTP và resend OTP phải có rate limit riêng theo IP. |
| FR-17 | Các sự kiện xác thực quan trọng phải được ghi vào `AuditLogs` mà không chứa password, OTP plaintext, OTP HMAC, JWT hay secret. |
| FR-18 | Frontend phải hỗ trợ Register, Login, ba trạng thái OTP, Dashboard, resend timer và logout phía client. |
| FR-19 | Auth POST body lớn hơn 16 KiB phải bị từ chối bằng `413 REQUEST_TOO_LARGE`. |

## 4. Quy tắc input và dữ liệu

| Dữ liệu | Quy tắc |
|---|---|
| Email | Bắt buộc, trim, định dạng email hợp lệ, tối đa 254 ký tự; lookup bằng `NormalizedEmail`. |
| Password | Bắt buộc, 8-128 ký tự, không chỉ có khoảng trắng; không trim/normalize. |
| Full name | Bắt buộc, trim, 2-100 ký tự. |
| Challenge ID | UUID do server cấp; client không được chọn User hoặc email thông qua request. |
| OTP | String khớp `^[0-9]{6}$`; không parse thành số nguyên. |

Client không được quyết định `UserId`, `Purpose`, `AuthenticationFlowId`, TTL, attempts, resend count, consume hoặc revoke state. Các giá trị này luôn lấy từ database/configuration phía server.

## 5. Luồng nghiệp vụ thực tế

### 5.1. Register

1. Validate email, password và họ tên.
2. Chuẩn hóa email bằng trim và uppercase invariant.
3. Hash password bằng ASP.NET Core Identity `PasswordHasher<User>`.
4. Persist User và audit `REGISTER_SUCCESS` trong cùng lần `SaveChanges`.
5. Trả dữ liệu User công khai; không trả password/hash/token.

### 5.2. Login password

1. Tìm User bằng `NormalizedEmail` và verify password; email không tồn tại vẫn thực hiện dummy password verification.
2. Thất bại ghi `LOGIN_PASSWORD_FAILED`, không tạo challenge và trả lỗi generic.
3. Thành công revoke open `LOGIN` challenge cũ, sau đó tạo pending challenge mới:
   - `OtpHash = null`
   - `ExpiresAt = null`
   - `SentAt = null`
   - `FlowExpiresAt = CreatedAt + 10 phút`
4. Ghi `LOGIN_PASSWORD_SUCCESS` và trả `requiresOtp = true`, `otpSent = false`, challenge ID cùng email đã mask.

### 5.3. First send OTP

1. Client gọi `/send-otp` chỉ với pending challenge ID.
2. Server kiểm tra purpose `LOGIN`, User active, flow còn hạn, challenge chưa revoke/consume và đúng pending state.
3. `OtpService` sinh đều một giá trị trong `[0, 999999]` bằng `RandomNumberGenerator.GetInt32` rồi format `D6`.
4. Server tạo HMAC-SHA-256 bind OTP với flow/challenge/User/purpose, persist prepared state cùng `OTP_SEND_REQUESTED` và `OTP_CREATED`.
5. `EmailService` gửi OTP tạm thời tới `User.Email` bằng MailKit/Gmail SMTP STARTTLS.
6. Chỉ sau delivery thành công, server set `SentAt`, ghi `OTP_SENT` và trả timer metadata.
7. Nếu delivery/finalize thất bại, server best-effort revoke challenge, ghi `OTP_DELIVERY_FAILED` và trả lỗi an toàn.

Gọi `/send-otp` lần hai trên cùng challenge bị từ chối; first send không thay thế resend.

### 5.4. Verify OTP và cấp JWT

1. Load challenge cùng User từ database.
2. Yêu cầu challenge đã sent và có `OtpHash`/`ExpiresAt`.
3. Kiểm tra purpose, User active, consumed, flow/OTP expiry, attempts và revoked state.
4. So sánh HMAC bằng fixed-time comparison.
5. OTP sai tăng attempts bằng `SaveChanges` có `RowVersion`/retry; lần sai thứ 5 revoke challenge.
6. OTP đúng persist `ConsumedAt` cùng `OTP_VERIFY_SUCCESS`.
7. Sau khi consume commit thành công, tạo JWT và ghi `JWT_ISSUED` best-effort.

`RowVersion` và retry có giới hạn giúp chống lost update/double consume. Chỉ request consume thành công được cấp JWT.

### 5.5. Resend OTP

1. Chỉ challenge đã sent, chưa consumed/revoke/lock, còn flow và còn lượt resend mới hợp lệ.
2. Cooldown được tính từ `SentAt`; trước 60 giây trả `429 RESEND_COOLDOWN`.
3. Trong transaction ngắn, revoke challenge cũ và insert prepared replacement với OTP HMAC mới.
4. Replacement giữ `AuthenticationFlowId`/`FlowExpiresAt`, tăng `ResendCount` và reset attempts.
5. Sau SMTP success, set `SentAt`, ghi `OTP_SENT` và `OTP_RESEND_SUCCESS`, rồi trả challenge ID mới.
6. Client phải dùng challenge ID mới; OTP/challenge cũ không thể verify.

## 6. Security Requirements

Chi tiết chuẩn nằm trong [`SECURITY_REQUIREMENTS.md`](../SECURITY_REQUIREMENTS.md). Bảng dưới đây mô tả cách implementation hiện tại đáp ứng các nhóm yêu cầu:

| Nhóm | Control hiện thực |
|---|---|
| Password | `PasswordHasher<User>`; không lưu/return/log plaintext hoặc `PasswordHash`. |
| OTP generation | CSPRNG, đúng 6 chữ số và hỗ trợ leading zero. |
| OTP storage | HMAC-SHA-256 với `Otp:HashingKey` riêng; không lưu OTP plaintext. |
| Expiration | OTP TTL 3 phút, flow TTL 10 phút; server UTC quyết định expiry. |
| Brute force | 5 lần sai/challenge, revoke khi đạt giới hạn và rate limit HTTP. |
| Single-use/replay | `ConsumedAt`, `RowVersion`, optimistic concurrency/retry và JWT chỉ sau consume. |
| Resend | Cooldown 60 giây, tối đa 3 resend/flow, challenge cũ bị revoke. |
| Authentication bypass | Không endpoint nào ngoài verify OTP thành công được cấp JWT. |
| Recipient integrity | Send/resend chỉ nhận challenge ID; email được lấy từ quan hệ User phía server. |
| Secrets | DB/SMTP/JWT/HMAC secret lấy từ configuration/User Secrets/environment, không hard-code. |
| Error handling | Validation/Problem Details đã sanitize; không trả stack, SQL hoặc SMTP detail. |
| Audit | Event/reason dùng allowlist; không có raw request/exception/secret trong schema. |

### Rate limit hiện thực

| Endpoint | Policy theo `RemoteIpAddress` |
|---|---:|
| Register | 5 request / 3600 giây |
| Login | 5 request / 60 giây |
| Send OTP | 3 request / 300 giây |
| Verify OTP | 10 request / 60 giây |
| Resend OTP | 3 request / 300 giây |

Rate limiter là fixed-window in-memory. Attempt limit, first-send-only, resend cooldown và max resend là các control độc lập với HTTP rate limit.

### Audit event hiện có

`REGISTER_SUCCESS`, `LOGIN_PASSWORD_SUCCESS`, `LOGIN_PASSWORD_FAILED`, `OTP_SEND_REQUESTED`, `OTP_CREATED`, `OTP_SENT`, `OTP_DELIVERY_FAILED`, `OTP_VERIFY_FAILED`, `OTP_EXPIRED`, `OTP_REPLAY_REJECTED`, `OTP_MAX_ATTEMPTS_REACHED`, `OTP_VERIFY_SUCCESS`, `OTP_RESEND_SUCCESS`, `OTP_RESEND_FAILED`, `JWT_ISSUED`.

## 7. Yêu cầu phi chức năng

- Kiến trúc monolith phân lớp: Controller → Service → EF Core → SQL Server.
- API/frontend cùng origin; không bật CORS rộng.
- HTTPS/HSTS được áp dụng ngoài Development; HTTP local chỉ phục vụ demo.
- Timestamp và quyết định bảo mật dùng UTC phía server.
- Data access dùng EF Core LINQ/parameterized SQL và EF Core Migration.
- Auth response dùng `Cache-Control: no-store`; lỗi nội bộ được sanitize.
- Security state thay đổi quan trọng phải persist trước khi trả success/JWT.
- Thiết kế ưu tiên correctness, security, readability, testability và simplicity.

## 8. Tiêu chí chấp nhận

- Password đúng chỉ tạo pending challenge và không gọi email/JWT.
- Verify trước first send bị từ chối.
- First send chỉ gửi một lần và response không chứa OTP/hash/JWT/email đầy đủ.
- OTP đúng trong hạn consume challenge và cấp JWT; replay bị từ chối.
- OTP sai tăng attempts; lần sai thứ 5 khóa challenge.
- OTP hết hạn bị từ chối kể cả khi giá trị đúng.
- Resend trước cooldown bị từ chối; resend hợp lệ làm OTP cũ vô hiệu.
- JWT thiếu, sai signature/issuer/audience hoặc hết hạn không truy cập được `/me`.
- User inactive không truy cập được `/me` dù token đã hợp lệ về mặt mật mã.
- Audit/log/API/database không chứa password hoặc OTP plaintext.
- Refresh UI ở màn OTP không tự động gửi email mới.

## 9. Giới hạn và rủi ro còn lại

- Rate limiter hiện chạy trong memory và chỉ partition theo IP; chưa có quota theo normalized email/User hoặc budget phát OTP dùng chung giữa first send và resend.
- SMTP và SQL Server không có distributed transaction. Compensation sau delivery/finalize failure là best-effort và luôn xử lý theo hướng fail closed.
- Register trả `409 EMAIL_ALREADY_REGISTERED`, vì vậy có rủi ro account enumeration được chấp nhận cho bản demo.
- JWT lưu trong `sessionStorage`; XSS cùng origin có thể đọc bearer token. Chưa có refresh token hoặc server-side revocation.
- Gmail SMTP phù hợp demo, chưa phải transactional email architecture cho production.
- Retention của audit/challenge, key rotation, trusted reverse proxy, backup và database least privilege chưa có quy trình vận hành hoàn chỉnh.
- Một số lifecycle invariant vẫn được service enforce thay vì có đầy đủ SQL CHECK constraint.
