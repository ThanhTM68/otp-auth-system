# OTP Authentication System

Hệ thống xác thực người dùng bằng **Email + Password + OTP** sử dụng:

- ASP.NET Core
- SQL Server
- Entity Framework Core
- MailKit + Gmail SMTP
- JWT
- xUnit

## 1. Clone và restore

```powershell
git clone <REPOSITORY_URL>
cd otp-auth-system
dotnet restore
```

## 2. Cấu hình SQL Server

Project đọc connection string từ:

`ConnectionStrings:DefaultConnection`

Ví dụ với SQL Server Express + Windows Authentication:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR-PC\SQLEXPRESS;Database=OTPAuthDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;" --project .\src\OTPAuth.API
```

Thay `YOUR-PC\SQLEXPRESS` bằng Server Name trên máy của bạn.

Apply migration:

```powershell
dotnet ef database update --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```

## 3. Cấu hình Gmail SMTP

Gmail dùng để gửi OTP cần:

- bật 2-Step Verification
- tạo Google App Password

Sau đó lưu bằng .NET User Secrets:

```powershell
dotnet user-secrets set "Email:Username" "YOUR_GMAIL@gmail.com" --project .\src\OTPAuth.API

dotnet user-secrets set "Email:Password" "YOUR_APP_PASSWORD" --project .\src\OTPAuth.API

dotnet user-secrets set "Email:FromEmail" "YOUR_GMAIL@gmail.com" --project .\src\OTPAuth.API
```

App Password nhập **16 ký tự liền nhau, không có khoảng trắng**.

Kiểm tra cấu hình:

```powershell
dotnet user-secrets list --project .\src\OTPAuth.API
```

Không commit Gmail App Password hoặc các secret khác lên GitHub.

## 4. Build và test

```powershell
dotnet build
dotnet test
```

## 5. Chạy hệ thống

```powershell
dotnet run --project .\src\OTPAuth.API
```

Profile mặc định hiện phục vụ HTTP tại `http://localhost:5011`. Có thể chạy rõ profile:

```powershell
dotnet run --project .\src\OTPAuth.API --launch-profile http
```

Swagger:

```text
http://localhost:5011/swagger
```

Frontend:

```text
http://localhost:5011/
```

## 6. Luồng sử dụng

```text
Đăng ký
→ Đăng nhập Email + Password
→ Password đúng: nhận pending challenge, chưa có OTP/JWT
→ Bấm “Gửi mã xác thực”
→ Server sinh OTP và gửi qua Gmail
→ Nhập OTP
→ Nhận JWT
→ Truy cập trang/API được bảo vệ
```

`POST /api/auth/login` không gọi SMTP. Endpoint chỉ xác minh password, thu hồi pending challenge cũ và trả `challengeId`, `requiresOtp: true`, `otpSent: false`, email đã mask và hạn của pre-auth flow. `POST /api/auth/send-otp` chỉ nhận `challengeId`; server tự lấy người nhận từ challenge. Xác thực chỉ hoàn tất sau khi `POST /api/auth/verify-otp` consume OTP hợp lệ và cấp JWT.

## 7. Lỗi thường gặp

### Không kết nối được database

Kiểm tra:

```powershell
dotnet user-secrets list --project .\src\OTPAuth.API
```

và đảm bảo có:

`ConnectionStrings:DefaultConnection`

### Không gửi được OTP

Kiểm tra:

```powershell
Test-NetConnection smtp.gmail.com -Port 587
```

và đảm bảo đã cấu hình:

```text
Email:Username
Email:Password
Email:FromEmail
```

### Chạy lại migration

```powershell
dotnet ef database update --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```
