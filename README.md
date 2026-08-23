<!-- PHASE 0   Terra   Medium
PHASE 1   Luna    Medium
PHASE 2   Terra   Medium    ← hiện tại

PHASE 3   Terra   Medium
PHASE 4   Terra   High
PHASE 5   Terra   High
PHASE 6   Terra   Medium
PHASE 7   Terra   High
PHASE 8   Terra   High

PHASE 9   Terra   High
PHASE 10  Terra   Medium

PHASE 11  Sol     High
PHASE 12  Luna    Medium

PHASE 13  Sol     xHigh
PHASE 14  Luna    Medium
PHASE 15  Sol     High -->

# OTP Authentication System

Hệ thống xác thực người dùng bằng **Email + Password + OTP một lần**.

Đây là bài tập lớn môn **An toàn và Bảo mật thông tin**, được xây dựng nhằm minh họa quy trình xác thực nhiều bước và các cơ chế bảo vệ OTP.

## Chức năng hiện có

- Đăng ký tài khoản
- Hash mật khẩu trước khi lưu database
- Đăng nhập bằng Email + Password
- Sinh OTP 6 chữ số bằng bộ sinh số ngẫu nhiên mật mã
- Gửi OTP qua Gmail SMTP
- OTP có thời gian hết hạn
- OTP chỉ được sử dụng một lần
- Giới hạn số lần nhập OTP sai
- Chống OTP Replay
- Resend OTP
- OTP cũ bị vô hiệu khi resend
- Resend cooldown
- Rate Limiting
- JWT Authentication
- Protected API
- Security Audit Logging
- Giao diện demo Register / Login / OTP / Dashboard
- Unit Test và Security Test

---

# Công nghệ

## Backend

- ASP.NET Core Web API
- C#
- Entity Framework Core
- SQL Server

## Authentication

- ASP.NET Core PasswordHasher
- OTP
- JWT Bearer Authentication

## Email

- MailKit
- MimeKit
- Gmail SMTP
- STARTTLS port 587

## Testing

- xUnit

---

# Yêu cầu môi trường

Cần cài:

- .NET SDK phù hợp với version trong project
- SQL Server
- SQL Server Management Studio - SSMS, khuyến nghị
- Git
- Gmail có bật 2-Step Verification nếu muốn gửi OTP thật
- Gmail App Password

Kiểm tra .NET:

```powershell
dotnet --version
```

Kiểm tra Git:

```powershell
git --version
```

---

# Clone project

```powershell
git clone <GITHUB_REPOSITORY_URL>
```

Đi vào project:

```powershell
cd otp-auth-system
```

Restore package:

```powershell
dotnet restore
```

Build:

```powershell
dotnet build
```

Chạy test:

```powershell
dotnet test
```

---

# Cấu hình SQL Server

Project sử dụng:

`ConnectionStrings:DefaultConnection`

Không nên đưa connection string chứa credential thật lên GitHub.

## Cách 1 - Windows Authentication

Ví dụ SQL Server Express:

```text
Server=YOUR-PC\SQLEXPRESS;Database=OTPAuthDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;
```

Lưu bằng .NET User Secrets:

```powershell
dotnet user-secrets init --project .\src\OTPAuth.API
```

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR-PC\SQLEXPRESS;Database=OTPAuthDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;" --project .\src\OTPAuth.API
```

Thay:

`YOUR-PC\SQLEXPRESS`

bằng SQL Server instance trên máy của bạn.

Ví dụ kiểm tra Server Name bằng SSMS.

---

# Tạo Database

Xem migration:

```powershell
dotnet ef migrations list --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```

Apply migration:

```powershell
dotnet ef database update --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```

Sau khi thành công, database:

```text
OTPAuthDb
```

sẽ có các bảng chính:

```text
Users
OtpChallenges
AuditLogs
__EFMigrationsHistory
```

---

# Cấu hình Gmail SMTP

Hệ thống sử dụng Gmail SMTP thông qua MailKit.

Thông số:

```text
Host: smtp.gmail.com
Port: 587
Security: STARTTLS
```

## 1. Bật 2-Step Verification

Tài khoản Gmail dùng để gửi OTP phải bật:

`2-Step Verification`

## 2. Tạo Google App Password

Tạo một App Password dành riêng cho ứng dụng.

Ví dụ tên:

`My OTP App`

Google sẽ cấp App Password.

Không sử dụng mật khẩu Gmail thông thường.

Không commit App Password lên GitHub.

---

# Cấu hình SMTP bằng User Secrets

Chạy:

```powershell
dotnet user-secrets set "Email:Username" "YOUR_GMAIL@gmail.com" --project .\src\OTPAuth.API
```

```powershell
dotnet user-secrets set "Email:Password" "YOUR_GMAIL_APP_PASSWORD" --project .\src\OTPAuth.API
```

```powershell
dotnet user-secrets set "Email:FromEmail" "YOUR_GMAIL@gmail.com" --project .\src\OTPAuth.API
```

Kiểm tra:

```powershell
dotnet user-secrets list --project .\src\OTPAuth.API
```

Bạn cần có:

```text
Email:Username
Email:Password
Email:FromEmail
ConnectionStrings:DefaultConnection
```

Không chia sẻ output nếu nó chứa secret thật.

---

# Kiểm tra kết nối Gmail SMTP

Trên Windows PowerShell:

```powershell
Test-NetConnection smtp.gmail.com -Port 587
```

Kết quả mong đợi:

```text
TcpTestSucceeded : True
```

---

# Chạy hệ thống

Từ thư mục root:

```powershell
dotnet run --project .\src\OTPAuth.API
```

Terminal sẽ hiển thị địa chỉ dạng:

```text
http://localhost:xxxx
https://localhost:xxxx
```

Mở Swagger:

```text
https://localhost:xxxx/swagger
```

hoặc địa chỉ được terminal hiển thị.

Nếu project có frontend tích hợp, mở URL root tương ứng:

```text
https://localhost:xxxx/
```

---

# Quy trình sử dụng

## 1. Đăng ký

Gọi:

```http
POST /api/auth/register
```

Ví dụ:

```json
{
    "email": "student@example.com",
    "password": "ExamplePassword123!",
    "fullName": "Nguyen Van A"
}
```

Hệ thống sẽ:

```text
Validate dữ liệu
→ kiểm tra email
→ hash password
→ lưu User vào SQL Server
```

Database không lưu mật khẩu plaintext.

---

# 2. Đăng nhập

Gọi:

```http
POST /api/auth/login
```

Ví dụ:

```json
{
    "email": "student@example.com",
    "password": "ExamplePassword123!"
}
```

Nếu mật khẩu đúng:

```text
Password verified
→ sinh OTP
→ hash OTP
→ tạo OtpChallenge
→ gửi OTP qua Gmail
```

Response sẽ có dạng:

```json
{
    "requiresOtp": true,
    "challengeId": "...",
    "expiresAt": "..."
}
```

Lưu ý:

**JWT chưa được cấp ở bước này.**

---

# 3. Nhận OTP

Kiểm tra email của tài khoản vừa đăng nhập.

Email có subject:

```text
Mã xác thực đăng nhập OTP
```

OTP gồm 6 chữ số.

Ví dụ:

```text
483291
```

Mã chỉ có hiệu lực trong thời gian ngắn.

---

# 4. Verify OTP

Gọi:

```http
POST /api/auth/verify-otp
```

Ví dụ:

```json
{
    "challengeId": "YOUR_CHALLENGE_ID",
    "otp": "483291"
}
```

Nếu OTP hợp lệ:

```text
OTP đúng
→ challenge được Consumed
→ OTP không dùng lại được
→ JWT được cấp
```

Response có access token/JWT theo DTO hiện tại của API.

---

# 5. Gọi Protected API

Sau khi nhận JWT, gửi header:

```http
Authorization: Bearer YOUR_ACCESS_TOKEN
```

Gọi protected endpoint, ví dụ:

```http
GET /api/auth/me
```

Không có JWT:

```text
401 Unauthorized
```

JWT hợp lệ:

```text
200 OK
```

---

# Resend OTP

Nếu OTP chưa nhận được hoặc hết hạn:

```http
POST /api/auth/resend-otp
```

Request:

```json
{
    "challengeId": "CURRENT_CHALLENGE_ID"
}
```

Khi resend thành công:

```text
OTP cũ → revoked
OTP mới → được sinh
Email mới → được gửi
challengeId mới → được trả về
```

Sau đó phải sử dụng `challengeId` mới.

OTP cũ không còn hợp lệ.

---

# Các cơ chế bảo mật

## Password

Password không được lưu plaintext.

Database chỉ lưu:

```text
PasswordHash
```

## OTP

Database không lưu OTP plaintext.

Chỉ lưu:

```text
OtpHash
```

OTP có:

- Expiration
- Single-use
- Attempt limit
- Revocation
- Replay protection
- Resend cooldown

## JWT

JWT chỉ được cấp sau khi OTP verify thành công.

Không cấp JWT chỉ sau bước Password.

## Secrets

Không đưa các dữ liệu sau lên Git:

```text
SMTP App Password
JWT Signing Key
Database Password
OTP hashing secret
```

Sử dụng:

- .NET User Secrets
- Environment Variables

---

# Kiểm tra hệ thống

Chạy:

```powershell
dotnet restore
dotnet build
dotnet test
```

Kết quả mong đợi:

```text
Restore succeeded
Build succeeded
All tests passed
```

---

# Manual Test khuyến nghị

Sau khi chạy hệ thống, test lần lượt:

## Test 1 - Register

```text
Register User
→ kiểm tra bảng Users
→ Password chỉ được lưu dạng hash
```

## Test 2 - Login sai

```text
Password sai
→ 401
→ không gửi OTP
→ không tạo JWT
```

## Test 3 - Login đúng

```text
Password đúng
→ nhận OTP Email
→ chưa có JWT
```

## Test 4 - OTP sai

```text
OTP sai
→ reject
→ AttemptCount tăng
```

## Test 5 - OTP đúng

```text
OTP đúng
→ JWT
→ Protected API hoạt động
```

## Test 6 - Replay Attack

Dùng lại OTP vừa verify thành công:

```text
OTP cũ
→ reject
```

## Test 7 - Resend

```text
OTP1
→ resend
→ OTP2

OTP1 → reject
OTP2 → accept
```

## Test 8 - Max Attempts

Nhập OTP sai nhiều lần.

Sau khi đạt giới hạn:

```text
OTP đúng cũng phải bị reject
```

## Test 9 - Rate Limiting

Spam:

```text
/login
/verify-otp
/resend-otp
```

Kết quả mong đợi:

```text
429 Too Many Requests
```

---

# Audit Logs

Các security event được lưu trong:

```text
AuditLogs
```

Ví dụ:

```text
REGISTER_SUCCESS
LOGIN_PASSWORD_SUCCESS
LOGIN_PASSWORD_FAILED
OTP_CREATED
OTP_VERIFY_FAILED
OTP_EXPIRED
OTP_VERIFY_SUCCESS
OTP_REPLAY_REJECTED
OTP_RESEND_SUCCESS
JWT_ISSUED
```

AuditLog không được chứa:

```text
Password
OTP plaintext
OtpHash
JWT
SMTP Password
JWT Secret
```

---

# Một số lỗi thường gặp

## `The ConnectionString property has not been initialized`

Chưa cấu hình:

```text
ConnectionStrings:DefaultConnection
```

Kiểm tra:

```powershell
dotnet user-secrets list --project .\src\OTPAuth.API
```

---

## `Chưa thể gửi email OTP. Vui lòng thử lại sau.`

Kiểm tra:

```text
Email:Username
Email:Password
Email:FromEmail
```

và:

```powershell
Test-NetConnection smtp.gmail.com -Port 587
```

Đảm bảo:

- Gmail đã bật 2-Step Verification
- đang dùng App Password
- không dùng mật khẩu Gmail thông thường

---

## Migration chưa apply

Chạy:

```powershell
dotnet ef database update --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```

---

# Cấu trúc chính

```text
otp-auth-system/
│
├── src/
│   └── OTPAuth.API/
│       ├── Controllers/
│       ├── Data/
│       ├── DTOs/
│       ├── Entities/
│       ├── Services/
│       ├── Configuration/
│       └── Program.cs
│
├── tests/
│   └── OTPAuth.Tests/
│
├── docs/
│
├── AGENTS.md
├── PROJECT_BRIEF.md
├── SECURITY_REQUIREMENTS.md
├── TASKS.md
└── README.md
```

---

# Lưu ý

Đây là hệ thống phục vụ mục đích học tập và demo môn **An toàn và Bảo mật thông tin**.

Không commit credential thật vào repository.

Trước khi đưa hệ thống lên môi trường production cần tiếp tục đánh giá:

- Secret management
- HTTPS/TLS production
- Email provider production
- Monitoring
- Distributed rate limiting
- Token storage
- Security headers
- Infrastructure security
