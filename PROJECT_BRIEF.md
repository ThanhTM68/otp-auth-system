# PROJECT BRIEF

## Tên đề tài

Xây dựng hệ thống xác thực người dùng sử dụng mật khẩu dùng một lần OTP.

## Môn học

An toàn và Bảo mật thông tin.

## Mục tiêu

Xây dựng một hệ thống Web demo quá trình xác thực người dùng bằng:

Email + Password + OTP.

Luồng xác thực chính:

1. Người dùng đăng ký tài khoản.
2. Password phải được hash trước khi lưu database.
3. Người dùng đăng nhập bằng Email + Password.
4. Nếu Password đúng, hệ thống tạo pending challenge; bước này chưa tạo/gửi OTP và chưa cấp JWT.
5. Người dùng chủ động bấm gửi mã xác thực bằng `challengeId`; client không được chọn email nhận.
6. Server sinh OTP, chỉ lưu HMAC và gửi OTP tới Email lấy từ User của challenge.
7. Người dùng nhập OTP.
8. Server kiểm tra OTP và consume challenge đúng một lần.
9. Nếu OTP hợp lệ thì xác thực thành công và hệ thống cấp JWT.

## Công nghệ

Backend:

- ASP.NET Core Web API
- C#

Database:

- SQL Server
- Entity Framework Core

Authentication:

- JWT

OTP:

- 6 chữ số
- sinh bằng cryptographically secure random generator

Email:

- SMTP

API documentation:

- Swagger / OpenAPI

Testing:

- xUnit

Frontend:

- HTML/CSS/Bootstrap đơn giản hoặc frontend tối thiểu phục vụ demo.

## Các chức năng chính

### Authentication

- Đăng ký
- Đăng nhập bằng Email + Password
- Gửi OTP sau khi Password đã được xác minh
- Xác thực OTP
- Gửi lại OTP
- JWT Authentication
- Logout phía client

### OTP

- Generate OTP
- Hash OTP
- Expiration
- Single-use OTP
- Resend OTP
- Giới hạn số lần nhập sai
- Resend cooldown
- Rate limiting

### Security

- Password hashing
- OTP không lưu plaintext
- OTP không xuất hiện trong log
- Chống OTP replay
- Chống brute-force OTP
- Chống spam resend OTP
- Input validation
- JWT secret không hard-code
- Audit log

## Phạm vi không làm

Không xây dựng:

- Microservices
- OAuth Server
- Face Recognition
- SMS Gateway trả phí
- Social Login
- CQRS
- Blockchain

Mục tiêu là một hệ thống OTP nhỏ nhưng đúng về mặt An toàn thông tin.
