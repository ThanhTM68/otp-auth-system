# PHASE 11 - Security Test Report

## Phạm vi và cách chạy

- Baseline trước Phase 11: 56 automated tests.
- Phase 11 bổ sung 7 integration tests HTTP cho JWT, protected API và validation/error sanitization.
- Kết quả cuối: 64/64 pass, 0 failed, 0 skipped.
- Automated tests dùng EF Core InMemory, test-only JWT/HMAC key sinh trong test, và FakeEmailService; không gửi email thật, không dùng SMTP credential hoặc SQL Server password.

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
| Audit event and sensitive fields | Missing audit, secret leakage | Kiểm tra login/verify/replay/resend event; AuditLog không có Password, OtpHash, token/JWT hoặc Authorization field. | PASS |
| Validation and exception leakage | Malformed input/internal-data disclosure | Invalid request trả 400 VALIDATION_ERROR, không chứa SQL/connection string/MailKit/stack trace. | PASS |

## Review tĩnh

- Logging review: chỉ thấy log delivery với email đã mask và mã event audit. Không có log password, OTP, OTP HMAC, JWT, key, SMTP credential hoặc connection string.
- Secret review: appsettings.json chỉ có placeholder rỗng; production keys/connection string được đọc qua configuration. User Secrets hiện có DefaultConnection, JWT signing key và OTP hashing key; không có file secret mới trong Git. Test keys là dữ liệu test-only sinh trong test.
- SQL injection review: data access dùng EF Core LINQ; không tìm thấy FromSqlRaw, ExecuteSqlRaw, SqlQuery hoặc raw SQL nhận email/OTP/client input.

## Giới hạn đã biết

- Không chạy manual Swagger flow vì môi trường không có SMTP credential; các flow email được kiểm thử bằng fake deterministic.
- Không có integration concurrency test trên SQL Server: EF InMemory không mô phỏng đầy đủ rowversion, filtered unique index, transaction và concurrent update. Cần test SQL Server riêng nếu muốn chứng minh double-consume dưới concurrency thật.
- Chưa có UI/PHASE 12; không thực hiện trong Phase 11.
