# PHASE 11 - Security Test Report

## Phạm vi và cách chạy

- Baseline trước Phase 11: 56 automated tests.
- Phase 11 bổ sung 7 integration tests HTTP cho JWT, protected API và validation/error sanitization.
- Phase 13 bổ sung regression tests cho form fallback, register rate limit, exception sanitization, request-size limit, JWT hardening, OTP expiration trong lúc xử lý/email delivery và concurrency trên SQL Server thật.
- Kết quả hiện tại sau Phase 13: 80/80 pass, 0 failed, 0 skipped khi bật hai test SQL Server opt-in.
- Phần lớn automated tests dùng EF Core InMemory, test-only JWT/HMAC key và FakeEmailService. Hai test concurrency chỉ chạy trên SQL Server khi opt-in bằng `RUN_SQLSERVER_SECURITY_TESTS=1`; connection string được đọc từ User Secrets/environment, dữ liệu test có định danh ngẫu nhiên và được xóa trong `finally`.

## Kết quả

| Test case | Threat/control | Expected and actual result | Status |
|---|---|---|---|
| Valid/duplicate/normalized registration | Account duplication, password leakage | Một User được tạo với PasswordHasher hash; duplicate normalized email bị từ chối; input sai không được persist. | PASS |
| Wrong/unknown/inactive login | Credential abuse | Không tạo challenge, không gửi email, không JWT. | PASS |
| OTP generation and storage | Predictable OTP, plaintext persistence | OTP đúng 6 chữ số, có leading zero; source dùng RandomNumberGenerator.GetInt32; entity/response chỉ có HMAC. | PASS |
| Valid OTP | Unauthorized authentication | ConsumedAt được persist, JWT hợp lệ chỉ được tạo sau verify. | PASS |
| Wrong and expired OTP | Brute force, expired-code use | AttemptCount tăng khi mã sai; mã ở expiry boundary bị từ chối, không JWT. | PASS |
| Replay OTP | OTP replay | VerifyOtpTwice_ShouldFailSecondAttempt: lần hai bị từ chối, không tạo JWT khác. | PASS |
| Max attempts | OTP brute force | Lần sai thứ 5 revoke challenge; mã đúng sau đó vẫn bị từ chối. | PASS |
| Revoked/wrong-purpose/inactive challenge | Authentication bypass | Challenge không phù hợp bị từ chối, không JWT. | PASS |
| Challenge/User integrity | Client-selected identity | Verify request chỉ có ChallengeId và Otp; User được resolve từ challenge trên server. | PASS |
| Resend and old OTP | OTP reuse after resend | Challenge cũ bị revoke, OTP cũ fail, OTP mới có thể verify. | PASS |
| Resend cooldown/delivery fail | Resend abuse | Cooldown không tạo challenge/email mới; SMTP fake failure revoke challenge mới. | PASS |
| JWT before OTP | Premature token issue | Login bằng password đúng trả metadata OTP, không access token. | PASS |
| Valid/invalid/expired JWT and /me | Token forgery, expired token use | JWT valid trả profile tối thiểu; thiếu, malformed, signature sai hoặc expired trả 401. | PASS |
| Rate limits | Login/OTP/resend flooding | Login 5/phút, verify 10/phút, resend 3/5 phút vượt ngưỡng đều trả 429; policy độc lập. | PASS |
| Register/body-size limits | Account/CPU spam, oversized body | Register vượt 5 request/giờ/IP trả 429; auth POST body trên 16 KiB trả 413. | PASS |
| Audit event and sensitive fields | Missing audit, secret leakage | Kiểm tra login/verify/replay/resend event; AuditLog không có Password, OtpHash, token/JWT hoặc Authorization field. | PASS |
| Validation and exception leakage | Malformed input/internal-data disclosure | Invalid request trả 400 VALIDATION_ERROR, không chứa SQL/connection string/MailKit/stack trace. | PASS |
| Unexpected exception | Stack trace/secret leakage | Exception có chuỗi chẩn đoán nhạy cảm giả lập chỉ trả generic 500 `INTERNAL_ERROR`, `traceId`, `no-store`, `application/problem+json`; capture logger không có raw message/path/stack/secret. | PASS |
| Concurrent correct/wrong OTP trên SQL Server | Double JWT, lost attempt | Hai verify đúng chỉ một JWT; 10 verify sai dừng đúng ở attempt 5, revoke challenge và không JWT. | PASS |
| Browser/JWT hardening | Clickjacking, algorithm confusion, key reuse | CSP/header an toàn, HSTS ngoài development, HS256-only validation và startup từ chối dùng chung OTP/JWT key. | PASS |

## Review tĩnh

- Logging review: chỉ giữ log delivery với email đã mask, mã event audit và exception type + trace ID đã sanitize. Raw EF Core provider logging bị tắt trong bản demo; captured-log regression không có password, raw exception/path/stack, OTP, OTP HMAC, JWT, key, SMTP credential hoặc connection string.
- Secret review: appsettings.json chỉ có placeholder rỗng; production keys/connection string được đọc qua configuration. User Secrets hiện có DefaultConnection, JWT signing key và OTP hashing key; không có file secret mới trong Git. Test keys là dữ liệu test-only sinh trong test.
- SQL injection review: data access dùng EF Core LINQ; không tìm thấy FromSqlRaw, ExecuteSqlRaw, SqlQuery hoặc raw SQL nhận email/OTP/client input.

## Giới hạn đã biết

- Không chạy end-to-end SMTP thật trong Phase 13 vì local User Secrets không có `Email:Password`; các flow email được kiểm thử bằng fake deterministic.
- SQL concurrency tests là opt-in để không vô tình ghi vào database khi chạy suite ở môi trường chưa được cho phép. Lần review này đã bật opt-in và cả hai test đều pass trên `OTPAuthDb`, sau đó cleanup dữ liệu test.
- UI Phase 12 được kiểm thử same-origin ở mức HTTP/static contract; chưa chạy browser automation nhập OTP từ mailbox thật.
