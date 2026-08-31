# Security Test Report

## 1. Phạm vi và cách chạy

Test project dùng xUnit trên .NET 10. Inventory hiện tại có đúng **105 test cases**:

- **101 standard cases**: unit test, EF Core InMemory, HTTP integration với `WebApplicationFactory`, fake email/JWT và kiểm tra static frontend.
- **4 SQL Server opt-in cases**: chỉ chạy khi `RUN_SQLSERVER_SECURITY_TESTS=1`; nếu không, xUnit đánh dấu bốn case này là skipped.

Chạy standard suite:

```powershell
dotnet test
```

Chạy toàn bộ suite với SQL Server đã apply migration và `DefaultConnection` được cấu hình ngoài repository:

```powershell
$env:RUN_SQLSERVER_SECURITY_TESTS = "1"
dotnet test
Remove-Item Env:\RUN_SQLSERVER_SECURITY_TESTS
```

Bốn SQL tests dùng record có định danh ngẫu nhiên và cleanup theo `UserId` trong `finally`. Chỉ chạy trên database test/development được phép ghi, không chạy trên production.

Lượt verify sau khi nâng cấp .NET 10 ngày 2026-08-31 có kết quả:

- Standard command: **101 passed, 0 failed, 4 SQL opt-in skipped, tổng 105**.
- Bật `RUN_SQLSERVER_SECURITY_TESTS=1`: **105 passed, 0 failed, 0 skipped**.

## 2. Inventory theo test source

| Test file | Số case | Scenario được kiểm tra thực tế |
|---|---:|---|
| `UnitTest1.cs` | 1 | Test project tham chiếu đúng API assembly. |
| `DatabaseModelTests.cs` | 3 | Entity không có password/OTP plaintext; pending challenge cho phép nullable đúng các delivery field. |
| `RegistrationTests.cs` | 5 | Register hợp lệ lưu password hash; duplicate normalized email; DataAnnotation invalid input; trim email/full name. |
| `LoginTests.cs` | 7 | Password đúng chỉ tạo pending challenge, không email/JWT; wrong/unknown/inactive không tạo challenge; login mới revoke open challenge cũ; validation. |
| `OtpServiceTests.cs` | 9 | OTP 6 chữ số, leading zero, keyed hash/verify, pending state, first-send preparation, expiry boundary, consumed/revoked state và max attempts. |
| `EmailDeliveryTests.cs` | 11 | First send dùng email server-side và OTP khớp hash; failure/expiry-during-send fail closed; second first-send bị chặn; invalid challenge/credentials; template và SMTP configuration diagnostics không lộ OTP/raw password. |
| `OtpVerificationTests.cs` | 11 | OTP đúng consume + JWT; wrong OTP tăng attempt; lần sai thứ 5 khóa; expiry và expiry-during-processing; replay/consumed/revoked/not-sent/wrong-purpose/inactive/missing; DTO validation. |
| `ResendOtpTests.cs` | 11 | Cooldown và boundary từ `SentAt`; pending/consumed/inactive/revoked reject; old OTP fail/new OTP pass; reset attempts; flow expiry/max resend; email failure fail closed; request validation. |
| `AuditLoggingTests.cs` | 6 | Event login, first-send, delivery failure, verify/replay/JWT, resend/cooldown; AuditLog không có sensitive property; server metadata bị giới hạn độ dài. |
| `RateLimitingTests.cs` | 8 | Threshold cho register/login/send/verify/resend và tính độc lập giữa các policy. |
| `SecurityApiTests.cs` | 16 | Login/send response không có JWT/OTP/hash/full email; `/me` yêu cầu JWT; valid/invalid/expired JWT; validation/413/500 sanitization; HSTS; HS256-only; key separation; DTO allowlist. |
| `StaticUiTests.cs` | 13 | Root/static assets, POST form fallback, session token handling, không DOM sink/log nhạy cảm, copy/accessibility, OTP string/states, responsive CSS và DOM hooks. |
| `SqlServerConcurrencyTests.cs` | 4 | Hai verify đúng chỉ một JWT; concurrent wrong OTP dừng ở 5; concurrent first-send chỉ một email; repeated login revoke trước khi insert pending mới. |
| **Tổng** | **105** | **101 standard + 4 SQL opt-in**. |

## 3. Security scenario matrix

Các dòng dưới đây chỉ ghi scenario có test tự động tương ứng trong repository.

| Scenario | Threat/control | Expected result được assert | Test source |
|---|---|---|---|
| Valid/duplicate registration | Password leakage, duplicate account | Password không lưu plaintext; normalized duplicate bị từ chối. | `RegistrationTests`, `DatabaseModelTests` |
| Wrong/unknown/inactive login | Credential abuse, enumeration | Không tạo challenge, email hoặc JWT. | `LoginTests`, `EmailDeliveryTests` |
| Correct password login | Premature OTP/JWT | Chỉ tạo pending challenge; `otpSent=false`; không gọi email/JWT service. | `LoginTests`, `SecurityApiTests` |
| First send | Recipient tampering, resend bypass | Chỉ nhận challenge ID; server dùng User email; response không có OTP/JWT; lần gọi thứ hai fail. | `EmailDeliveryTests`, `SecurityApiTests` |
| First-send delivery failure | Success giả, OTP không nhận được vẫn verify | Failure hoặc OTP hết hạn trong delivery revoke/fail closed và không trả success. | `EmailDeliveryTests`, `AuditLoggingTests` |
| OTP generation/storage | Predictable/plaintext OTP | Output đúng 6 chữ số, giữ leading zero; entity chỉ lưu keyed hash. | `OtpServiceTests`, `DatabaseModelTests` |
| Verify trước send | Authentication bypass | Pending challenge trả NotSent, attempt không tăng, không JWT. | `OtpVerificationTests` |
| Wrong OTP | Brute force | Bị từ chối, `AttemptCount` tăng, không consume/JWT. | `OtpVerificationTests` |
| Expired OTP | Expired-code use | Boundary `now >= ExpiresAt` và mã hết hạn trong lúc xử lý đều bị từ chối. | `OtpServiceTests`, `OtpVerificationTests`, `EmailDeliveryTests` |
| Max attempts | OTP brute force | Lần sai thứ 5 revoke; mã đúng sau đó vẫn fail. | `OtpServiceTests`, `OtpVerificationTests` |
| Replay/consumed/revoked OTP | OTP reuse | Verify lần hai hoặc challenge terminal bị từ chối; không JWT thứ hai. | `OtpVerificationTests`, `AuditLoggingTests` |
| Resend | OTP reuse, resend abuse | Cooldown được enforce; challenge cũ revoke; OTP cũ fail, OTP mới pass; limit flow/resend được enforce. | `ResendOtpTests` |
| JWT before OTP | Authentication bypass | Login, pending và send response không có token; JWT chỉ sau successful consume. | `LoginTests`, `EmailDeliveryTests`, `OtpVerificationTests`, `SecurityApiTests` |
| Protected API/JWT validation | Forged/expired/missing token | `/me` chỉ trả profile tối thiểu với JWT hợp lệ; thiếu, malformed hoặc expired token trả 401. | `SecurityApiTests` |
| Rate limiting | Endpoint flooding | Vượt threshold của năm auth endpoints trả 429; policy không dùng chung nhầm. | `RateLimitingTests` |
| Audit sensitive data | Secret leakage | Event cần thiết được ghi; entity audit không có password, OTP hash, JWT/token hoặc Authorization property. | `AuditLoggingTests` |
| SMTP diagnostic leakage | OTP/App Password leakage | Missing/invalid config bị từ chối trước connect; captured diagnostic không chứa OTP hoặc raw invalid App Password. | `EmailDeliveryTests` |
| Validation/error leakage | Stack trace/internal secret disclosure | Invalid/oversized/unexpected request trả ProblemDetails an toàn; response/log capture không lộ synthetic secret/path/raw exception message. | `SecurityApiTests` |
| Frontend security contract | URL/storage/DOM leakage, clickjacking | POST form fallback; JWT chỉ sessionStorage; không localStorage/console/unsafe DOM sink; CSP/security headers và responsive hooks tồn tại. | `StaticUiTests`, `SecurityApiTests` |
| SQL concurrency | Double JWT, lost attempt, double first-send, filtered unique insert ordering | Chỉ một JWT/email; attempts dừng đúng 5; repeated login revoke bản ghi open trước khi tạo replacement. | `SqlServerConcurrencyTests` |

## 4. Giới hạn của test suite

- Static UI tests kiểm tra HTTP/HTML/CSS/JavaScript contract; chưa chạy browser automation tương tác DOM hoặc end-to-end trên thiết bị thật.
- Không gửi email tới Gmail/mailbox thật trong automated suite. `FakeEmailService` kiểm tra orchestration và failure; SMTP configuration tests dừng trước network connection.
- Rate-limit tests xác nhận threshold và policy partition, không chờ hết toàn bộ time window và không kiểm tra triển khai distributed/multi-instance.
- Bốn SQL tests chỉ bao phủ các race quan trọng nêu trong inventory; các test còn lại chủ yếu dùng EF Core InMemory.
- Không có test logout server-side/token revocation vì demo chỉ xóa JWT phía client và chưa triển khai denylist/refresh token.
- Quota phát OTP dùng chung theo User/normalized email chưa được hiện thực nên không có test cho control đó.

Các giới hạn này không được diễn giải thành PASS cho Gmail delivery thật, browser E2E, distributed rate limiting hoặc server-side JWT revocation.
