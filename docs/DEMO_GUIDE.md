# Hướng dẫn demo OTP Authentication

Tài liệu này dùng để trình bày luồng đã được hiện thực. Chuẩn bị SQL Server, migration, OTP/JWT key và Gmail App Password theo [SETUP.md](SETUP.md) trước khi demo gửi email thật.

## 1. Khởi chạy

```powershell
dotnet run --project .\src\OTPAuth.API --launch-profile http
```

Mở base URL được in trong terminal. UI ở `<BASE_URL>/`; Swagger ở `<BASE_URL>/swagger` trong môi trường Development.

## 2. Luồng demo chính

### Demo 1 — Register

1. Chọn **Đăng ký** và nhập họ tên, email, password, xác nhận password.
2. Gửi form.

Kỳ vọng: User được tạo, database chỉ lưu `PasswordHash`; không có OTP hoặc JWT. Có thể kiểm tra độ dài hash mà không in nội dung hash:

```sql
SELECT Email, LEN(PasswordHash) AS PasswordHashLength, IsActive, CreatedAt
FROM Users
WHERE Email = 'EMAIL_DEMO';
```

### Demo 2 — Password login tạo pending challenge

1. Đăng nhập bằng email/password đúng.
2. Quan sát UI chuyển ngay sang **State A**.

Kỳ vọng:

- Thông tin đăng nhập đã được xác minh và email được mask.
- Có nút **Gửi mã xác thực**.
- Chưa gửi email, chưa có input/timer OTP và chưa cấp JWT.
- Challenge đang pending: `OtpHash`, `SentAt`, `ExpiresAt` đều `NULL`.

Password sai hoặc email không tồn tại phải trả cùng lỗi chung và không tạo challenge.

### Demo 3 — First send OTP

1. Bấm **Gửi mã xác thực**.
2. **State B** giữ nguyên card, disable CTA và hiện loading tại nút; không fake success và chưa chạy timer.
3. Sau khi server xác nhận gửi thành công, **State C** hiện email mask, input OTP, thời hạn và cooldown lấy từ response server.

Kỳ vọng: OTP được gửi tới email mà server lấy từ User của challenge. API chỉ nhận `challengeId`; response không chứa OTP/JWT. Gọi `/send-otp` lần hai trên challenge đã sent phải bị từ chối.

### Demo 4 — OTP success và protected API

1. Nhập nguyên chuỗi OTP 6 chữ số từ email, kể cả chữ số `0` ở đầu.
2. Bấm **Xác nhận mã**.

Kỳ vọng: challenge được consume một lần, JWT mới được cấp, UI mở Dashboard và gọi `GET /api/auth/me` để hiển thị hồ sơ tối thiểu.

Bấm **Đăng xuất** sẽ xóa JWT khỏi `sessionStorage` và trở về Login. Demo không có endpoint thu hồi token phía server; bản sao token còn hợp lệ tới `exp`.

## 3. Các tình huống bảo mật

| Demo | Cách thực hiện | Kết quả mong đợi |
|---|---|---|
| Duplicate email | Đăng ký lại cùng email với khác biệt hoa/thường | Bị từ chối; không có User thứ hai. |
| Wrong password | Login bằng password sai | Lỗi chung; không challenge, email hay JWT. |
| Verify trước first send | Dùng pending `challengeId` gọi `/verify-otp` qua Swagger | `OTP_NOT_SENT`; không tăng attempt và không JWT. |
| Wrong OTP | Nhập OTP sai | Bị từ chối; `AttemptCount` tăng. |
| Expired OTP | Chờ hết hạn rồi verify | Bị từ chối kể cả mã đúng. |
| Replay | Verify lại cùng challenge/OTP sau lần thành công | Bị từ chối; không cấp JWT thứ hai. |
| Resend | Chờ hết cooldown rồi bấm **Gửi lại mã** | Challenge cũ bị revoke, UI nhận challenge ID mới; OTP cũ fail, OTP mới pass. |
| Max attempts | Nhập sai đủ 5 lần rồi thử mã đúng | Challenge bị revoke; mã đúng sau đó vẫn fail. |
| Rate limit | Gọi nhanh vượt quota của register/login/send/verify/resend | HTTP `429` kèm lỗi an toàn. |
| Refresh OTP | Refresh ở State A, B hoặc C | Trở về Login; không tự động gửi email mới. |
| Protected API | Gọi `/api/auth/me` không có hoặc có JWT không hợp lệ | HTTP `401`; không trả hồ sơ. |
| Email failure | Dùng cấu hình SMTP không hợp lệ trong môi trường test riêng | Không báo gửi thành công; challenge mới fail closed/revoke. |

## 4. Kiểm tra AuditLogs bằng SQL Server

Chỉ đọc metadata allowlist; không tìm hoặc sao chép password, OTP, hash, JWT hay credential:

```sql
SELECT TOP (50)
    EventType,
    Success,
    ReasonCode,
    CreatedAt,
    UserId,
    OtpChallengeId
FROM AuditLogs
ORDER BY CreatedAt DESC;
```

Kỳ vọng có các event phù hợp với thao tác vừa demo, ví dụ `REGISTER_SUCCESS`, `LOGIN_PASSWORD_SUCCESS`, `OTP_SEND_REQUESTED`, `OTP_CREATED`, `OTP_SENT`, `OTP_VERIFY_FAILED`, `OTP_VERIFY_SUCCESS` và `JWT_ISSUED`. Schema AuditLog không có cột password, OTP plaintext, OTP hash hoặc JWT.

Có thể kiểm tra trạng thái challenge mà không hiển thị hash:

```sql
SELECT TOP (20)
    Id,
    Purpose,
    DATALENGTH(OtpHash) AS OtpHashBytes,
    SentAt,
    ExpiresAt,
    ConsumedAt,
    AttemptCount,
    MaxAttempts,
    IsRevoked
FROM OtpChallenges
ORDER BY CreatedAt DESC;
```

## 5. Manual test checklist

Chỉ tick sau khi đã quan sát kết quả trong đúng môi trường demo:

- [ ] Register thành công; database lưu PasswordHash, không lưu password plaintext.
- [ ] Duplicate normalized email bị chặn.
- [ ] Login sai bị chặn và không tạo challenge.
- [ ] Login đúng tạo pending challenge, chưa gửi OTP/JWT.
- [ ] First send có loading, không double-submit và Gmail nhận OTP.
- [ ] OTP input giữ được leading zero.
- [ ] OTP sai bị chặn và AttemptCount tăng.
- [ ] OTP đúng cấp JWT và Dashboard gọi `/api/auth/me` thành công.
- [ ] Replay OTP bị chặn.
- [ ] Resend tuân thủ cooldown và vô hiệu OTP cũ.
- [ ] MaxAttempts hoạt động; mã đúng sau khi khóa vẫn fail.
- [ ] OTP hết hạn bị từ chối.
- [ ] Rate limiting trả HTTP 429.
- [ ] Refresh OTP page không tự gửi email.
- [ ] Logout xóa JWT khỏi `sessionStorage`.
- [ ] AuditLogs ghi đúng event và không chứa dữ liệu nhạy cảm.
- [ ] OtpChallenges không lưu OTP plaintext.

## 6. Dữ liệu phía trình duyệt

- Challenge ID chỉ giữ trong bộ nhớ JavaScript; reload khi đang ở bước OTP sẽ yêu cầu login lại.
- Password và OTP không được lưu vào Web Storage, cookie, URL hoặc console.
- JWT chỉ được ghi vào `sessionStorage` sau verify OTP thành công; UI không hiển thị hoặc log token.
- Frontend dùng `textContent` cho dữ liệu động và không chứa signing key, OTP hashing key, SMTP credential hoặc connection string.
