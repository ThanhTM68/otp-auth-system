# PHASE 0 - Phân tích yêu cầu hệ thống xác thực OTP

## 1. Mục đích và phạm vi

Tài liệu này đặc tả yêu cầu nghiệp vụ và bảo mật cho hệ thống demo xác thực người dùng theo luồng:

`Email + Password -> OTP qua Email -> JWT`

Phạm vi hiện tại chỉ là phân tích và thiết kế. Phase 0 không tạo solution, project ASP.NET Core, source code, migration hay database thật.

### Trong phạm vi

- Đăng ký tài khoản bằng email và mật khẩu.
- Kiểm tra email và mật khẩu khi đăng nhập.
- Sinh, bảo vệ, gửi và xác minh OTP đăng nhập.
- Gửi lại OTP an toàn.
- Chỉ cấp JWT sau khi OTP hợp lệ.
- Dùng JWT để truy cập một API được bảo vệ.
- Ghi audit log cho các sự kiện bảo mật quan trọng.

### Ngoài phạm vi

- Microservices, CQRS, Event Sourcing, Kafka và Redis.
- OAuth Server, Social Login, SMS Gateway và Refresh Token.
- Chức năng quản trị người dùng, khôi phục mật khẩu và xác minh email riêng lúc đăng ký.
- Thu hồi JWT phía server; logout trong bản demo là xóa JWT ở client.
- Frontend hoàn chỉnh.

## 2. Thuật ngữ

| Thuật ngữ | Ý nghĩa |
|---|---|
| OTP | Mã dùng một lần gồm đúng 6 chữ số. |
| OTP challenge | Bản ghi phía server gắn một OTP với người dùng, mục đích, hạn dùng và trạng thái sử dụng. |
| Password step | Bước xác minh email và mật khẩu; chưa phải xác thực hoàn tất. |
| Authentication flow | Chuỗi thao tác từ khi mật khẩu đúng đến khi OTP được xác minh; có ID và hạn tuyệt đối riêng để resend không kéo dài vô hạn. |
| JWT | Access token được cấp sau khi hoàn thành cả password step và OTP step. |
| CSPRNG | Bộ sinh số ngẫu nhiên phù hợp cho mục đích mật mã. |
| UTC | Múi giờ chuẩn dùng cho toàn bộ thời điểm phía server và database. |

## 3. Actor

| Actor | Vai trò |
|---|---|
| Khách chưa xác thực | Đăng ký, gửi thông tin đăng nhập, nhập OTP và yêu cầu gửi lại OTP. |
| Người dùng đã xác thực | Gửi JWT để truy cập API protected và logout ở phía client. |
| SMTP/Email Provider | Nhận yêu cầu gửi OTP tới đúng email đã lưu của người dùng. |
| Người vận hành/kiểm toán | Cấu hình secrets, giám sát và đọc audit log bằng công cụ vận hành; chưa có API quản trị trong phạm vi. |
| Kẻ tấn công | Actor dùng trong phân tích threat, không phải actor nghiệp vụ hợp lệ. |

## 4. Use Case

| ID | Use Case | Actor chính | Tiền điều kiện | Kết quả |
|---|---|---|---|---|
| UC-01 | Đăng ký tài khoản | Khách | Email chưa được đăng ký | User được lưu với password đã hash; không có JWT. |
| UC-02 | Đăng nhập bằng password | Khách | User tồn tại và active | Password đúng thì tạo OTP challenge; password sai không tạo OTP. |
| UC-03 | Sinh và gửi OTP | Khách; SMTP hỗ trợ | Password step thành công | OTP mới được gửi; database chỉ giữ OTP hash. |
| UC-04 | Xác minh OTP | Khách | Có challenge phù hợp | OTP hợp lệ được consume đúng một lần. |
| UC-05 | Gửi lại OTP | Khách, SMTP | Challenge còn được phép resend | Challenge cũ bị revoke và OTP/challenge mới được tạo. |
| UC-06 | Hoàn tất xác thực | Khách | OTP vừa được consume thành công | JWT có hạn dùng được cấp. |
| UC-07 | Truy cập API protected | Người dùng đã xác thực | JWT hợp lệ | Server trả thông tin của User được định danh bởi token. |
| UC-08 | Ghi audit event | Các actor nghiệp vụ (gián tiếp) | Có sự kiện bảo mật | Audit log được ghi mà không chứa password hoặc OTP plaintext. |
| UC-09 | Logout | Người dùng đã xác thực | Client đang giữ JWT | Client xóa JWT; token đã cấp hết hiệu lực khi đến hạn. |

## 5. Yêu cầu chức năng

| ID | Yêu cầu |
|---|---|
| FR-01 | Hệ thống phải đăng ký user với email duy nhất sau chuẩn hóa. |
| FR-02 | Hệ thống phải hash password bằng password hasher chuyên dụng trước khi lưu. |
| FR-03 | Hệ thống phải kiểm tra `IsActive` và password khi đăng nhập. |
| FR-04 | Password sai, email không tồn tại hoặc user inactive phải cho cùng một phản hồi chung và không được tạo OTP. |
| FR-05 | Password đúng phải tạo OTP challenge cho mục đích `LOGIN`, gửi OTP qua email và chưa cấp JWT. |
| FR-06 | OTP phải có đúng 6 chữ số, TTL mặc định 3 phút và tối đa 5 lần nhập sai. |
| FR-07 | OTP hợp lệ chỉ được consume một lần; mọi lần dùng lại phải bị từ chối. |
| FR-08 | Resend phải tuân thủ cooldown 60 giây, revoke challenge cũ và tạo OTP/challenge mới. |
| FR-09 | JWT chỉ được cấp sau khi thao tác consume OTP đã commit thành công. |
| FR-10 | API protected phải yêu cầu JWT hợp lệ. |
| FR-11 | Các endpoint `login`, `verify-otp` và `resend-otp` phải được rate limit. |
| FR-12 | Các sự kiện bảo mật bắt buộc phải được ghi vào audit log với dữ liệu đã được kiểm soát. |
| FR-13 | Một authentication flow có hạn tuyệt đối mặc định 10 phút và tối đa 3 lần resend; hết giới hạn phải nhập lại password. |
| FR-14 | Quota phát OTP theo User phải áp dụng chung cho cả login password thành công và resend. |

## 6. Quy tắc dữ liệu đầu vào

- `Email`: bắt buộc, trim khoảng trắng hai đầu, kiểm tra định dạng, tối đa 254 ký tự và tạo giá trị chuẩn hóa để so sánh duy nhất không phân biệt hoa thường.
- `Password`: bắt buộc, từ 8 đến 128 ký tự; không trim hoặc thay đổi nội dung trước khi hash/verify. Không trả password hoặc password hash trong response.
- `FullName`: bắt buộc, trim, từ 2 đến 100 ký tự; không được dùng để tạo HTML email mà không encode.
- `ChallengeId`: UUID hợp lệ do server cấp, không được client tự chọn.
- `Otp`: chuỗi khớp chính xác `^[0-9]{6}$`; giữ được chữ số `0` ở đầu.
- Client không được gửi hoặc quyết định `UserId`, `Purpose`, `AuthenticationFlowId`, `FlowExpiresAt`, `ResendCount`, `ExpiresAt`, `AttemptCount`, `MaxAttempts`, `ConsumedAt` hay `IsRevoked`.
- Mọi giới hạn phải được kiểm tra lại ở server. Không dựa vào validation của UI.

## 7. Các luồng nghiệp vụ

### 7.1. Register Flow

1. Client gửi `Email`, `Password` và `FullName` qua HTTPS.
2. Server validate request, chuẩn hóa email và kiểm tra unique.
3. Server hash password bằng ASP.NET Core `PasswordHasher`; không log request body.
4. Server tạo User với `IsActive = true` và thời điểm UTC.
5. Server lưu User và audit event `REGISTER_SUCCESS`.
6. Server trả `201 Created` với dữ liệu công khai của User.
7. Luồng này không tạo OTP, không đăng nhập tự động và không cấp JWT.

### 7.2. Login Password Flow

1. Middleware áp dụng giới hạn thô theo IP/endpoint mà không đọc hoặc log request body.
2. Server validate request, chuẩn hóa email, rồi áp dụng quota theo normalized email ở tầng application/service.
3. Server tìm User, kiểm tra User active và verify password bằng `PasswordHasher`. Với email không tồn tại, implementation nên verify một dummy hash để giảm khác biệt thời gian phản hồi.
4. Nếu email/password không đúng hoặc User inactive: ghi `LOGIN_PASSWORD_FAILED`, trả lỗi chung và kết thúc. Tuyệt đối không tạo OTP.
5. Nếu đúng, server ghi `LOGIN_PASSWORD_SUCCESS`, kể cả khi quota phát OTP ở bước sau từ chối request.
6. Server kiểm tra quota phát OTP dùng chung theo User. Khi vượt quota, trả `429`, không tạo challenge/email và ghi `RATE_LIMITED` theo policy chống log flood; nếu còn quota thì bắt đầu OTP Generation Flow mới.
7. Server gửi OTP tới email lấy từ database, không lấy địa chỉ đích khác từ request.
8. Khi SMTP chấp nhận email, server trả `200 OK` gồm `challengeId`, `expiresAt`, `flowExpiresAt` và `resendAvailableAt`; không trả OTP và không cấp JWT.

Một lần login password thành công mới sẽ revoke login challenge trước đó chưa consumed của cùng User. Quy tắc này đơn giản và an toàn cho bản demo, nhưng đồng nghĩa lần đăng nhập mới trên thiết bị khác làm mã cũ mất hiệu lực.

### 7.3. OTP Generation Flow

1. Lấy `CreatedAt = now` theo UTC và xác định metadata flow trước: login mới tạo `AuthenticationFlowId`, `FlowExpiresAt = CreatedAt + 10 phút`, `ResendCount = 0`; resend giữ ID/hạn cũ và tăng count.
2. Tạo `challengeId` dạng UUID không đoán được.
3. Sinh số nguyên phân phối đều trong `[0, 999999]` bằng CSPRNG và format `D6`.
4. Tạo keyed hash HMAC-SHA-256 bằng secret ngẫu nhiên riêng tối thiểu 256 bit, bind ít nhất `AuthenticationFlowId`, `ChallengeId`, `UserId`, `Purpose` và OTP. Cách này giảm nguy cơ brute-force offline khi database bị lộ; SHA-256 không khóa là chưa đủ mạnh cho không gian chỉ một triệu mã.
5. Chỉ lưu HMAC, `Purpose = LOGIN`, `ExpiresAt = min(CreatedAt + 3 phút, FlowExpiresAt)`, `AttemptCount = 0`, `MaxAttempts = 5`, `ConsumedAt = null`, `IsRevoked = false` cùng metadata flow đã xác định.
6. Ghi `OTP_CREATED` mà không ghi OTP.
7. OTP plaintext chỉ tồn tại tạm thời trong memory đủ để tạo email, sau đó không được giữ lại, lưu database, log, audit hoặc trả qua API.
8. Gửi email qua kết nối SMTP bảo mật. Email nêu hạn thực tế từ `ExpiresAt` (tối đa 3 phút), không hard-code luôn là 3 phút. Nếu gửi thất bại/timeout, server trả lỗi tạm thời, ghi `OTP_DELIVERY_FAILED` đã sanitize và thực hiện transaction bù để revoke challenge mới. Transaction bù là best-effort vì SQL Server và SMTP không có distributed transaction; nếu process/compensation lỗi, challenge còn lại vẫn bị giới hạn bởi TTL, flow expiry, attempts và rate limit.

### 7.4. OTP Verification Flow

1. Middleware rate limit thô theo IP; server validate `challengeId` và OTP đúng 6 chữ số.
2. Server tải challenge bằng ID, áp dụng quota theo challenge/User ở tầng service. User, purpose và trạng thái luôn lấy từ database.
3. Từ chối nếu challenge không tồn tại, sai purpose, đã revoke, đã consumed, User inactive hoặc `AttemptCount >= MaxAttempts`.
4. Kiểm tra expiry bằng đồng hồ UTC phía server. Khi `now >= ExpiresAt`, từ chối kể cả mã đúng và ghi `OTP_EXPIRED`.
5. Tính HMAC từ OTP nhận được và so sánh fixed-time với `OtpHash`.
6. Nếu sai, tăng `AttemptCount` atomically và ghi `OTP_VERIFY_FAILED`. Lần sai thứ 5 đặt `AttemptCount = 5`, revoke challenge và không cho resend từ challenge đã khóa.
7. Nếu đúng, atomically đặt `ConsumedAt` và ghi `OTP_VERIFY_SUCCESS` trong transaction. Điều kiện update phải kiểm tra lại challenge chưa consumed/revoked, chưa hết hạn và chưa bị khóa.
8. Chỉ request thắng trong trường hợp verify đồng thời được phép hoàn tất; request còn lại bị coi là replay.
9. Sau khi transaction consume commit thành công, tiếp tục Authentication Success Flow.

Client nhận thông báo chung cho OTP/challenge sai, hết hạn, đã dùng, đã revoke hoặc bị khóa. Lý do chi tiết chỉ xuất hiện dưới dạng reason code đã sanitize trong audit log.

### 7.5. Resend OTP Flow

1. Client gửi `challengeId`; không gửi email hoặc UserId làm căn cứ xác định người nhận. Middleware áp dụng quota thô theo IP.
2. Server validate, tải challenge/User, áp dụng quota theo User và kiểm tra cooldown 60 giây từ lần tạo/gửi gần nhất.
3. Chỉ open challenge `LOGIN` hiện tại, chưa consumed/revoke/khóa, `now < FlowExpiresAt` và `ResendCount < 3` mới được resend. Challenge OTP đã hết TTL vẫn có thể resend nếu authentication flow chưa hết hạn và còn lượt.
4. Trong một transaction ngắn, server revoke challenge cũ, tạo OTP/challenge hoàn toàn mới, giữ nguyên `AuthenticationFlowId`/`FlowExpiresAt`, tăng `ResendCount` và ghi `OTP_RESEND` cùng `OTP_CREATED`.
5. Challenge mới có `ExpiresAt = min(CreatedAt + 3 phút, FlowExpiresAt)`. OTP được gửi tới email trong database; sau SMTP, server recheck challenge vẫn usable trước khi response `200 OK` trả `challengeId`, `expiresAt`, `flowExpiresAt`, `resendAvailableAt` mới.
6. Client phải thay ID cũ bằng ID mới. Mã cũ luôn bị từ chối, kể cả khi chưa hết TTL.
7. Hai request resend đồng thời phải được bảo vệ bằng transaction/concurrency token để tối đa một challenge mới còn active.
8. Nếu gửi email thất bại, server không khôi phục challenge cũ và thực hiện best-effort compensation để revoke challenge mới. Client nhận lỗi tạm thời và phải login lại; trường hợp compensation lỗi được giới hạn bởi TTL/flow expiry.

### 7.6. Authentication Success Flow

1. Việc consume OTP và audit đã commit thành công.
2. Server tạo JWT access token có tối thiểu `sub`, `jti`, `iat`, `exp`, issuer và audience.
3. JWT dùng signing key riêng lấy từ configuration an toàn, có TTL demo 15 phút và clock skew tối đa 30 giây.
4. Server trả JWT đúng một lần trong response thành công của `verify-otp`.
5. API protected xác minh signature, thuật toán, issuer, audience và lifetime trước khi cho truy cập.
6. Không tin UserId do client gửi; danh tính được lấy từ claim `sub` đã xác minh.
7. Logout xóa JWT phía client. JWT đã cấp vẫn dùng được tới khi hết hạn; đây là giới hạn đã chấp nhận của bản demo không có token revocation.

## 8. Yêu cầu phi chức năng

- Kiến trúc phải là ASP.NET Core Web API monolith phân lớp đơn giản: Controller -> Service -> Entity Framework Core -> SQL Server.
- Giao tiếp client/API và API/SMTP phải dùng kênh mã hóa khi triển khai ngoài máy local.
- Mọi timestamp được tạo và so sánh ở server bằng UTC.
- EF Core phải dùng parameterized query; không ghép chuỗi SQL từ dữ liệu client.
- Các cập nhật trạng thái OTP nhạy cảm phải atomic và có xử lý concurrency.
- Lỗi API dùng cấu trúc nhất quán, không trả stack trace, SQL error hoặc dữ liệu cấu hình.
- Logging request phải tắt hoặc redact các field `password`, `otp`, authorization header và token.
- Swagger/OpenAPI không được chứa secret thật và không tự động mở anonymous access cho endpoint protected.
- Mọi protected endpoint phải dùng policy kiểm tra User còn `IsActive` từ database; không chỉ dựa vào JWT claim cũ.
- Thiết kế ưu tiên correctness, security, readability, testability và simplicity.

## 9. Phân tích rủi ro bảo mật

| Nguy cơ | Kịch bản | Kiểm soát thiết kế | SR liên quan | Rủi ro còn lại |
|---|---|---|---|---|
| Password leakage | Database, log hoặc traffic bị lộ | `PasswordHasher` có salt/work factor; HTTPS; không log/return plaintext; giới hạn quyền DB | SR-01..03, SR-27 | Mật khẩu yếu vẫn có thể bị dò offline; chính sách password và giám sát cần được vận hành đúng. |
| OTP brute-force | Thử nhiều mã trong không gian 1.000.000 giá trị | TTL 3 phút; tối đa 5 lần sai/challenge; rate limit IP/User/challenge; atomic counter | SR-08, SR-09, SR-13, SR-14, SR-18 | Botnet phân tán vẫn tạo tải; cần theo dõi audit và tinh chỉnh rate limit. |
| OTP replay | Gửi lại mã vừa xác minh hoặc hai request đồng thời | `ConsumedAt`; conditional update/transaction; concurrency token; chỉ phát JWT sau commit | SR-10..12, SR-19 | Lỗi triển khai concurrency có thể phá invariant, vì vậy phải có test concurrent replay. |
| OTP expiration | Dùng mã cũ nhưng đúng | Server dùng UTC và từ chối khi `now >= ExpiresAt` trước khi consume | SR-08, SR-09 | Clock sai trên server có thể ảnh hưởng; cần đồng bộ thời gian hệ thống. |
| OTP reuse | Mã cũ dùng lại sau login/resend | Single use; login/resend revoke challenge cũ; mỗi lần sinh mã mới | SR-10, SR-11, SR-15, SR-16 | Email cũ vẫn chứa mã nhưng mã không còn hiệu lực. |
| Resend abuse | Spam email, kéo dài password step hoặc dùng resend để reset số lần thử | Cooldown 60 giây; tối đa 3 resend; flow hết hạn sau 10 phút; quota phát OTP chung theo User; challenge khóa không được resend; mã cũ bị revoke | SR-15..18 | Rate limiter trong memory chỉ phù hợp deployment một instance; giới hạn phải được đo và tinh chỉnh. |
| Predictable OTP | Dự đoán mã do PRNG yếu hoặc phân phối lệch | CSPRNG, lấy đều `[0, 999999]`, format `D6`; không dùng `Random()` | SR-04, SR-05 | Endpoint email hoặc máy chủ bị xâm nhập vẫn có thể lộ OTP. |
| Offline cracking OTP hash | Kẻ tấn công đọc bảng OTP và thử hết một triệu mã | HMAC-SHA-256 với secret riêng, bind challenge/user/purpose; secret không nằm trong DB | SR-06, SR-27 | Nếu cả DB và OTP hashing key cùng bị lộ thì TTL/consumed vẫn là lớp bảo vệ còn lại. |
| Credential stuffing | Dùng email/password rò rỉ từ hệ thống khác | Rate limit theo IP và normalized email; lỗi chung; dummy hash; audit; OTP là bước bắt buộc | SR-18, SR-19, SR-22 | Tài khoản email bị chiếm cùng lúc vẫn làm giảm hiệu quả của OTP email. |
| Authentication bypass | Gọi protected API sau password step hoặc giả mạo claim/JWT | Không JWT ở register/login; token chỉ từ verify service; `[Authorize]`; validate signature/issuer/audience/lifetime/algorithm | SR-19..21, SR-24..26 | JWT bị đánh cắp dùng được đến hết hạn vì chưa có server-side revocation. |
| Secret leakage | JWT/SMTP/DB/HMAC key bị commit hoặc log | Environment variables, user secrets hoặc secret store; key tách biệt; startup fail nếu thiếu; redaction | SR-20, SR-23, SR-27 | Quản lý và rotation secrets phụ thuộc môi trường triển khai. |
| User enumeration | Phân biệt email tồn tại qua login/resend | Login trả lỗi chung; resend chỉ nhận opaque challenge ID; thời gian xử lý gần tương đương | SR-24..26 | Register trả `409` cho email trùng có thể tiết lộ tài khoản; chấp nhận cho demo và phải rate limit. |
| Injection/XSS trong dữ liệu hồ sơ | Gửi email/full name độc hại | DTO validation, EF Core parameterization, output encoding trong email/UI | SR-24..26 | UI ở phase sau vẫn phải encode output đúng ngữ cảnh. |

## 10. Chính sách rate limiting đề xuất cho bản demo

Đây là giá trị khởi đầu và phải nằm trong configuration để có thể điều chỉnh:

| Endpoint | Partition đề xuất | Giới hạn khởi đầu |
|---|---|---|
| `register` | IP | 5 request / 1 giờ |
| `login` | IP và normalized email | 5 request / 1 phút/IP; 10 request / 15 phút/email |
| `verify-otp` | IP và challenge | 10 request / 1 phút/IP; đồng thời hard limit 5 OTP sai/challenge |
| `resend-otp` | IP và User | Cooldown 60 giây; 5 request / 15 phút/IP; 3 request / 15 phút/User |
| Phát OTP (login + resend) | User | Tổng cộng 5 OTP / 15 phút/User, áp dụng chung cho cả hai luồng |

Middleware chỉ thực hiện quota thô theo IP/endpoint và giới hạn kích thước request. Quota cần normalized email, challenge hoặc User được thực hiện ở tầng application/service sau validation/lookup; middleware không tự đọc body chứa password/OTP. Khi rate limit/cooldown bị vượt, API trả `429 Too Many Requests` và `Retry-After` khi có thể. Deployment demo một instance có thể dùng bộ đếm trong memory. Nếu triển khai nhiều instance, bộ đếm phân tán là vấn đề cần thiết kế lại; Phase 0 không bổ sung Redis.

## 11. Đối chiếu SECURITY_REQUIREMENTS.md

| SR | Cách đáp ứng trong thiết kế | Tài liệu chi tiết |
|---|---|---|
| SR-01 | Chỉ lưu `PasswordHash`, không lưu password. | Database, mục `Users` |
| SR-02 | Dùng ASP.NET Core `PasswordHasher`. | Architecture, Password security |
| SR-03 | Redact/tắt log password và request body nhạy cảm. | Architecture, Logging |
| SR-04 | OTP là chuỗi đúng 6 chữ số. | Requirements 6, 7.3 |
| SR-05 | Sinh bằng CSPRNG, không dùng `Random()`. | Requirements 7.3 |
| SR-06 | Database chỉ giữ keyed OTP hash. | Database, `OtpChallenges` |
| SR-07 | Không log/audit/console OTP plaintext. | Requirements 7.3; Database, `AuditLogs` |
| SR-08 | TTL mặc định 3 phút. | Requirements 7.3 |
| SR-09 | Từ chối khi `now >= ExpiresAt`, kể cả mã đúng. | Requirements 7.4 |
| SR-10 | OTP chỉ verify thành công một lần. | Requirements 7.4 |
| SR-11 | Commit `ConsumedAt` khi verify thành công. | Database, state transitions |
| SR-12 | Atomic consume và test replay/concurrent replay. | Architecture, concurrency |
| SR-13 | `MaxAttempts = 5` lần sai. | Requirements 7.4 |
| SR-14 | Lần sai thứ 5 revoke challenge. | Requirements 7.4 |
| SR-15 | Resend luôn sinh OTP/challenge mới. | Requirements 7.5 |
| SR-16 | Revoke challenge cũ trong transaction. | Requirements 7.5 |
| SR-17 | Cooldown mặc định 60 giây. | Requirements 7.5, 10 |
| SR-18 | Rate limit login, verify và resend. | Requirements 10; API Spec |
| SR-19 | Login chỉ trả challenge; JWT chỉ sau OTP. | Requirements 7.2, 7.6 |
| SR-20 | JWT key lấy từ configuration an toàn. | Architecture, Secrets |
| SR-21 | JWT TTL 15 phút và validate lifetime. | Requirements 7.6; API Spec |
| SR-22 | Ghi đủ tám event bắt buộc. | Database, Audit events |
| SR-23 | Audit schema không có password/OTP; metadata allowlist. | Database, `AuditLogs` |
| SR-24 | Validate toàn bộ request DTO. | Requirements 6; API Spec |
| SR-25 | Giá trị bảo mật lấy từ server/database. | Requirements 6, 7 |
| SR-26 | Error middleware/Problem Details không lộ nội bộ. | Architecture; API Spec |
| SR-27 | SMTP/JWT/DB/HMAC secrets không commit. | Architecture, Secrets |

## 12. Tiêu chí chấp nhận và test cho các phase sau

- Đăng ký lưu password hash, không trả hoặc log password/hash.
- Password đúng tạo challenge nhưng không tạo JWT.
- Password sai, email không tồn tại hoặc User inactive không tạo challenge.
- OTP đúng trong hạn consume challenge và cấp đúng một JWT response.
- OTP sai tăng `AttemptCount` chính xác.
- OTP hết hạn bị từ chối ngay cả khi mã đúng.
- OTP consumed và replay đồng thời đều bị từ chối sau lần thành công đầu tiên.
- Lần sai thứ 5 khóa challenge; request sau không thể verify hoặc resend challenge đó.
- Resend trước cooldown bị từ chối; resend hợp lệ vô hiệu OTP cũ và trả challenge ID mới.
- Resend thứ tư hoặc resend tại/sau `FlowExpiresAt` bị từ chối và buộc login password lại.
- OTP hiện tại được tạo bởi lần resend thứ 3 vẫn verify được nếu còn hạn; `ResendCount = 3` chỉ cấm tạo lần resend thứ 4.
- Không response/log/audit/database nào chứa OTP plaintext hoặc password.
- JWT thiếu/sai signature, issuer, audience hoặc đã hết hạn không truy cập được API protected.
- Không JWT nào được cấp trước khi OTP consume commit.

## 13. Quyết định thiết kế quan trọng

- Monolith phân lớp đơn giản, không có repository layer bắt buộc và không dùng kiến trúc ngoài phạm vi.
- Một User chỉ có tối đa một login challenge chưa consumed và chưa revoke tại một thời điểm.
- Challenge ID là opaque UUID; resend dùng ID này thay vì email/UserId.
- OTP hash dùng HMAC-SHA-256 với key riêng và fixed-time comparison.
- OTP hết hạn tại `now >= ExpiresAt`; lần sai thứ 5 khóa ngay challenge.
- Challenge hết TTL có thể resend chỉ trong cùng flow 10 phút và tối đa 3 lần; sau đó phải login password lại.
- Consume, tăng attempt và revoke/create khi resend phải có transaction/concurrency control.
- JWT TTL demo là 15 phút và chỉ được cấp sau commit consume.
- Phản hồi lỗi xác thực là lỗi chung; audit reason code mới giữ chi tiết nội bộ.
- SMTP không nằm trong database transaction; lỗi gửi email dùng transaction bù best-effort, còn TTL/flow expiry/attempt/rate limit là hàng rào khi compensation không chạy được.

## 14. Vấn đề cần xử lý ở các phase sau

- Chọn SMTP provider, sender address, chế độ TLS và cách xử lý retry/delivery failure thực tế.
- Cấu hình giá trị thật cho connection string, JWT key, OTP HMAC key và SMTP credential; xây dựng quy trình rotation.
- Kiểm thử/tinh chỉnh ngưỡng rate limit và cấu hình trusted proxy để lấy đúng client IP.
- Chốt thời gian lưu AuditLog/OtpChallenge và job dọn dữ liệu hết hạn; không tự xóa database.
- Chấp nhận hoặc thay đổi việc register trả `409` làm lộ email đã tồn tại.
- Nếu cần nhiều application instance, thiết kế bộ đếm rate limit dùng chung mà không tự ý thêm Redis trong phạm vi hiện tại.
- Mọi protected endpoint phải áp dụng active-user policy để việc disable có hiệu lực ngay. Nếu cần logout thu hồi JWT tức thì, bổ sung token revocation/security stamp ở một yêu cầu tương lai.
- Xây dựng và chạy các security unit/integration test ở đúng phase; Phase 0 chưa có source code để test.
