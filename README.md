# OTP Authentication System

Hệ thống demo xác thực người dùng theo luồng **Email + Password + OTP**, được xây dựng cho bài tập lớn môn An toàn và Bảo mật thông tin.

## Công nghệ chính

- .NET 8 / ASP.NET Core Web API
- Entity Framework Core 8 + SQL Server
- ASP.NET Core Identity `PasswordHasher<User>`
- JWT Bearer Authentication (HS256)
- MailKit + Gmail SMTP
- xUnit
- HTML, CSS và JavaScript tĩnh phục vụ cùng ASP.NET Core application

## Yêu cầu

- .NET 8 SDK
- SQL Server
- EF Core CLI (`dotnet-ef`) để apply migration
- Gmail đã bật 2-Step Verification và Google App Password nếu muốn gửi OTP thật

## Clone và restore

```powershell
git clone <REPOSITORY_URL>
cd otp-auth-system
dotnet restore
```

## Cấu hình

Ứng dụng đọc cấu hình nhạy cảm từ .NET User Secrets hoặc environment variables. Các key cần cấu hình để chạy đầy đủ luồng:

```text
ConnectionStrings:DefaultConnection
Otp:HashingKey
Jwt:SigningKey
Email:Username
Email:Password
Email:FromEmail
```

Ví dụ connection string dùng placeholder:

```text
Server=YOUR_SQL_SERVER;Database=OTPAuthDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;
```

`Otp:HashingKey` và `Jwt:SigningKey` phải là hai giá trị Base64 khác nhau, mỗi giá trị giải mã được ít nhất 32 byte. `Email:Password` phải là Google App Password, không phải mật khẩu Gmail thông thường.

Hướng dẫn cấu hình đầy đủ và xử lý lỗi: [docs/SETUP.md](docs/SETUP.md).

## Database

Apply toàn bộ migration hiện có:

```powershell
dotnet ef database update --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```

## Build và test

```powershell
dotnet build
dotnet test
```

## Chạy hệ thống

```powershell
dotnet run --project .\src\OTPAuth.API --launch-profile http
```

Địa chỉ thực tế được in trong terminal và được cấu hình tại `src/OTPAuth.API/Properties/launchSettings.json`.

- UI: `<BASE_URL>/`
- Swagger: `<BASE_URL>/swagger` trong môi trường Development

## Luồng xác thực

```text
Register
→ Password được hash và lưu User
→ Login bằng Email + Password
→ Tạo pending challenge, chưa gửi OTP và chưa có JWT
→ Người dùng bấm Gửi mã xác thực
→ Server sinh OTP, lưu HMAC và gửi qua Gmail
→ Verify OTP
→ Consume challenge đúng một lần
→ Cấp JWT
→ Truy cập GET /api/auth/me
```

Password đúng chỉ hoàn thành bước đầu. Xác thực chỉ hoàn tất khi OTP hợp lệ đã được consume và JWT được cấp từ `POST /api/auth/verify-otp`.

## Cấu trúc repository

```text
src/
  OTPAuth.API/
    Configuration/
    Controllers/
    Data/
    DTOs/
    Entities/
    Services/
    Swagger/
    wwwroot/
tests/
  OTPAuth.Tests/
docs/
```

## Tài liệu

- [Yêu cầu](docs/REQUIREMENTS.md)
- [Kiến trúc và sequence diagram](docs/ARCHITECTURE.md)
- [Database và ERD](docs/DATABASE_DESIGN.md)
- [API](docs/API_SPEC.md)
- [Hướng dẫn demo](docs/DEMO_GUIDE.md)
- [Security review](docs/SECURITY_REVIEW.md)
- [Security test report](docs/SECURITY_TEST_REPORT.md)

Không commit connection string có credential, Gmail App Password, OTP hashing key hoặc JWT signing key.
