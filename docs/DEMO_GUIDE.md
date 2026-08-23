# PHASE 12 - Hướng dẫn demo OTP Authentication

## 1. Chuẩn bị

1. Khởi động SQL Server instance đã cấu hình và bảo đảm database migration đã được apply.
2. Cấu hình các giá trị local bằng .NET User Secrets: DefaultConnection, OTP hashing key, JWT signing key và SMTP settings.
3. Không đặt secret thật trong repository hoặc tài liệu.

## 2. Khởi chạy

```powershell
dotnet run --project .\src\OTPAuth.API --launch-profile https
```

Mở `https://localhost:7044/`. Swagger vẫn có tại `https://localhost:7044/swagger`.
Nếu development certificate chưa được trust, trình duyệt có thể cảnh báo; không cần tắt HTTPS.

## 3. Luồng demo chính

1. Chọn **Đăng ký**, nhập họ tên, email, password và xác nhận password.
2. Đăng ký thành công rồi dùng email/password vừa tạo để đăng nhập.
3. Login chỉ chuyển sang màn hình OTP và chưa có JWT.
4. Lấy OTP từ email, nhập nguyên chuỗi 6 chữ số và bấm **Xác minh OTP**.
5. Sau verify thành công, Dashboard gọi `GET /api/auth/me` với Bearer JWT và hiển thị hồ sơ tối thiểu.
6. Bấm **Đăng xuất** để xóa JWT khỏi `sessionStorage` và trở về Login.

## 4. Các tình huống bảo mật để trình bày

- **Wrong OTP:** nhập sai mã; UI báo lỗi chung, không vào Dashboard và backend tăng AttemptCount.
- **Expired OTP:** chờ timer về 00:00 rồi verify; backend vẫn là nguồn quyết định cuối cùng và phải từ chối.
- **Resend:** chờ hết cooldown, gửi lại; UI thay challenge ID mới, reset OTP/timer và mã cũ bị backend từ chối.
- **Replay:** sau một lần verify thành công, gửi lại challenge/mã cũ bằng Swagger; backend từ chối.
- **Rate limit:** gửi nhanh vượt quota login/verify/resend; UI hiển thị thông báo thao tác quá nhiều.
- **Protected API:** xóa JWT bằng Logout rồi gọi lại `GET /api/auth/me`; request không có Bearer token bị từ chối.

## 5. Dữ liệu phía trình duyệt

- Password và OTP chỉ tồn tại tạm trong input/request, không lưu vào localStorage, sessionStorage, cookie hoặc URL.
- Challenge ID chỉ giữ trong memory; reload trang sẽ yêu cầu login lại.
- JWT chỉ giữ trong sessionStorage của tab, không hiển thị hoặc ghi console.
- Frontend không chứa JWT secret, OTP hashing key, SMTP credential hoặc connection string.
