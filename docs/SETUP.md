# Hướng dẫn cài đặt và chạy hệ thống

## 1. Điều kiện cần

- .NET 10 SDK
- SQL Server đang hoạt động
- EF Core CLI nếu máy chưa có:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.11
```

Nếu máy đã cài `dotnet-ef` phiên bản cũ, cập nhật thay vì cài mới:

```powershell
dotnet tool update --global dotnet-ef --version 10.0.11
```

- Gmail đã bật 2-Step Verification và có Google App Password nếu chạy gửi OTP thật

Kiểm tra môi trường:

```powershell
dotnet --version
dotnet ef --version
```

Hai lệnh trên phải trả về phiên bản major `10`.

## 2. Restore project

Từ thư mục gốc repository:

```powershell
dotnet restore
```

Project API đã có `UserSecretsId`, vì vậy không cần chạy lại `dotnet user-secrets init`.

## 3. Cấu hình SQL Server

Ứng dụng đọc connection string tại `ConnectionStrings:DefaultConnection`.

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=YOUR_SQL_SERVER;Database=OTPAuthDb;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;" --project .\src\OTPAuth.API
```

Thay `YOUR_SQL_SERVER` bằng SQL Server instance của môi trường đang chạy. Không đưa server name cá nhân hoặc database credential vào Git.

## 4. Cấu hình OTP và JWT key

Hai key phải khác nhau, dùng Base64 và giải mã được ít nhất 32 byte. Có thể sinh trực tiếp trong PowerShell rồi lưu vào User Secrets mà không ghi vào file:

```powershell
$otpKeyBytes = New-Object byte[] 32
$otpKeyGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$otpKeyGenerator.GetBytes($otpKeyBytes)
$otpKeyMaterial = [Convert]::ToBase64String($otpKeyBytes)
dotnet user-secrets set "Otp:HashingKey" $otpKeyMaterial --project .\src\OTPAuth.API

$jwtKeyBytes = New-Object byte[] 32
$jwtKeyGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtKeyGenerator.GetBytes($jwtKeyBytes)
$jwtKeyMaterial = [Convert]::ToBase64String($jwtKeyBytes)
dotnet user-secrets set "Jwt:SigningKey" $jwtKeyMaterial --project .\src\OTPAuth.API
```

Issuer, audience và token lifetime không nhạy cảm đang được cấu hình trong `appsettings.json`. Ứng dụng từ chối khởi động nếu key thiếu, quá ngắn, sai Base64 hoặc hai key giống nhau.

## 5. Cấu hình Gmail SMTP

Implementation dùng MailKit, `smtp.gmail.com`, cổng 587 và STARTTLS. Lưu thông tin gửi mail bằng User Secrets:

```powershell
dotnet user-secrets set "Email:Username" "YOUR_GMAIL@gmail.com" --project .\src\OTPAuth.API
dotnet user-secrets set "Email:Password" "YOUR_GOOGLE_APP_PASSWORD" --project .\src\OTPAuth.API
dotnet user-secrets set "Email:FromEmail" "YOUR_GMAIL@gmail.com" --project .\src\OTPAuth.API
```

- `Email:Username`: Gmail dùng để gửi OTP.
- `Email:Password`: Google App Password 16 ký tự; không dùng mật khẩu Gmail thông thường.
- `Email:FromEmail`: thường giống `Email:Username`.
- Không commit hoặc chụp màn hình giá trị secret.

Có thể xác nhận các key đã tồn tại bằng `dotnet user-secrets list --project .\src\OTPAuth.API`, nhưng lệnh này hiển thị giá trị; không dán output vào issue, log hoặc báo cáo.

## 6. Apply migration

EF Core Migration chịu trách nhiệm tạo/cập nhật database và schema:

```powershell
dotnet ef migrations list --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
dotnet ef database update --project .\src\OTPAuth.API --startup-project .\src\OTPAuth.API
```

Hai migration hiện có là `InitialCreate` và `SupportPendingOtpChallenge`.

## 7. Build, test và chạy

```powershell
dotnet build
dotnet test
dotnet run --project .\src\OTPAuth.API --launch-profile http
```

Terminal sẽ in base URL. Truy cập:

```text
UI:      <BASE_URL>/
Swagger: <BASE_URL>/swagger
```

Swagger chỉ được bật trong môi trường Development.

### Test concurrency trên SQL Server thật

Bốn test SQL Server được opt-in để tránh ghi database ngoài ý muốn:

```powershell
$env:RUN_SQLSERVER_SECURITY_TESTS = "1"
dotnet test
Remove-Item Env:\RUN_SQLSERVER_SECURITY_TESTS
```

Các test dùng `ConnectionStrings:DefaultConnection` từ User Secrets/environment và tự dọn dữ liệu test theo phạm vi của chúng.

## 8. Lỗi thường gặp

### `The ConnectionString property has not been initialized`

Thiếu hoặc sai key `ConnectionStrings:DefaultConnection`. Kiểm tra SQL Server service, instance name và User Secrets.

### `Otp:HashingKey is required` hoặc `Jwt:SigningKey is required`

Thiếu secret tương ứng. Nếu báo key không hợp lệ, kiểm tra giá trị là Base64 của ít nhất 32 byte và hai key không giống nhau.

### `SMTP_AUTH_FAILED`

Kiểm tra Gmail username, Google App Password, `FromEmail` và bảo đảm App Password thuộc đúng Gmail. Không dùng mật khẩu Gmail thông thường.

### Không kết nối được cổng SMTP 587

```powershell
Test-NetConnection smtp.gmail.com -Port 587
```

Nếu thất bại, kiểm tra firewall, mạng hoặc chính sách chặn SMTP của môi trường.

### Cảnh báo HTTPS khi chạy local

Dùng URL/profile được terminal in ra. Profile `http` dành cho demo local; ngoài local development phải dùng HTTPS với certificate hợp lệ.

## 9. Nguyên tắc secret

- Không lưu User Secrets trong repository.
- Không ghi password, OTP, OTP HMAC, JWT hoặc SMTP credential vào log/tài liệu.
- Không dùng cùng key cho OTP HMAC và JWT signing.
- Production cần secret store, rotation và phân quyền phù hợp; User Secrets chỉ dành cho local development.
