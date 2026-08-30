# PHASE 12 - Hướng dẫn demo OTP Authentication

## 1. Chuẩn bị

1. Khởi động SQL Server instance đã cấu hình và bảo đảm database migration đã được apply.
2. Cấu hình các giá trị local bằng .NET User Secrets: DefaultConnection, OTP hashing key, JWT signing key và SMTP settings.
3. Không đặt secret thật trong repository hoặc tài liệu.

## 2. Khởi chạy

```powershell
dotnet run --project .\src\OTPAuth.API --launch-profile http
```

Mở `http://localhost:5011/`. Swagger có tại `http://localhost:5011/swagger`. HTTPS vẫn phải được dùng ngoài local development.

## 3. Luồng demo chính

1. Chọn **Đăng ký**, nhập họ tên, email, password và xác nhận password.
2. Đăng ký thành công rồi dùng email/password vừa tạo để đăng nhập.
3. Login đúng chuyển ngay sang **State A**: password đã xác minh, email đã mask và nút **Gửi mã xác thực**. Lúc này chưa có OTP/email/JWT.
4. Bấm **Gửi mã xác thực**. Trong **State B**, CTA bị disable và hiện loading tại nút; timer/form OTP chưa chạy cho tới khi server xác nhận gửi thành công.
5. Sau response thành công, **State C** hiện thông báo đã gửi, email mask, timer từ `expiresAt`/`resendAvailableAt`, input OTP và nút **Xác nhận mã**.
6. Lấy OTP từ email, nhập nguyên chuỗi 6 chữ số (kể cả leading zero) và xác nhận.
7. Chỉ sau verify thành công, Dashboard mới nhận JWT, gọi `GET /api/auth/me` và hiển thị hồ sơ tối thiểu.
8. Bấm **Đăng xuất** để xóa JWT khỏi `sessionStorage` và trở về Login.

## 4. Các tình huống bảo mật để trình bày

- **Wrong OTP:** nhập sai mã; UI báo lỗi chung, không vào Dashboard và backend tăng AttemptCount.
- **Verify trước send:** dùng pending `challengeId` gọi verify qua Swagger; backend trả `OTP_NOT_SENT`, không JWT.
- **First send twice:** gọi lại `/send-otp` sau lần gửi thành công; backend từ chối và không bypass cooldown.
- **Expired OTP:** chờ timer về 00:00 rồi verify; backend vẫn là nguồn quyết định cuối cùng và phải từ chối.
- **Resend:** chờ hết cooldown, gửi lại; UI thay challenge ID mới, reset OTP/timer và mã cũ bị backend từ chối.
- **Replay:** sau một lần verify thành công, gửi lại challenge/mã cũ bằng Swagger; backend từ chối.
- **Rate limit:** gửi nhanh vượt quota login/send/verify/resend; UI hiển thị thông báo thao tác quá nhiều.
- **Refresh OTP page:** refresh ở State A/B/C; UI trở về Login và không tự gửi email mới.
- **Protected API:** xóa JWT bằng Logout rồi gọi lại `GET /api/auth/me`; request không có Bearer token bị từ chối.

## 5. Dữ liệu phía trình duyệt

- Password và OTP chỉ tồn tại tạm trong input/request, không lưu vào localStorage, sessionStorage, cookie hoặc URL.
- Challenge ID chỉ giữ trong memory; reload trang sẽ yêu cầu login lại.
- Password verification success không phải authentication success; JWT chỉ tồn tại sau khi OTP đã sent, verify và consume thành công.
- JWT chỉ giữ trong sessionStorage của tab, không hiển thị hoặc ghi console.
- Frontend không chứa JWT secret, OTP hashing key, SMTP credential hoặc connection string.
