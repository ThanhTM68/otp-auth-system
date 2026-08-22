# SECURITY REQUIREMENTS

## Password

SR-01:
Không được lưu password plaintext.

SR-02:
Password phải được hash bằng PasswordHasher hoặc thuật toán password hashing phù hợp.

SR-03:
Không ghi password vào log.

---

## OTP Generation

SR-04:
OTP gồm 6 chữ số.

SR-05:
OTP phải được tạo bằng cryptographically secure random generator.

Không sử dụng Random() thông thường nếu không phù hợp cho mục đích mật mã.

---

## OTP Storage

SR-06:
Không lưu OTP plaintext lâu dài trong database.

Database chỉ lưu dạng hash của OTP.

SR-07:
Không ghi OTP vào application log, audit log hoặc console trong production mode.

---

## OTP Expiration

SR-08:
OTP có thời hạn sử dụng.

Giá trị mặc định cho bài demo:

OTP TTL = 3 phút.

SR-09:
OTP hết hạn phải bị từ chối ngay cả khi người dùng nhập đúng mã.

---

## OTP Single Use

SR-10:
Một OTP chỉ được xác thực thành công một lần.

SR-11:
Sau khi OTP được sử dụng thành công:

ConsumedAt phải được cập nhật.

Các lần verify tiếp theo phải bị từ chối.

---

## OTP Replay Protection

SR-12:
OTP đã được sử dụng không được sử dụng lại.

Phải có Unit Test chứng minh khả năng chống replay.

---

## OTP Attempt Limit

SR-13:
Mỗi OTP Challenge chỉ cho phép tối đa 5 lần nhập sai.

SR-14:
Sau khi vượt MaxAttempts, challenge bị khóa hoặc vô hiệu hóa.

---

## OTP Resend

SR-15:
Resend OTP phải tạo OTP mới.

SR-16:
OTP/challenge trước phải bị vô hiệu hóa khi tạo OTP mới.

SR-17:
Có resend cooldown.

Giá trị demo mặc định:

60 giây.

---

## Rate Limiting

SR-18:
Các endpoint nhạy cảm phải được xem xét rate limiting:

- login
- verify OTP
- resend OTP

---

## Authentication Flow

SR-19:
Không được cấp JWT ngay sau khi Password đúng.

Luồng đúng:

Password đúng
→ tạo OTP challenge
→ verify OTP thành công
→ cấp JWT.

---

## JWT

SR-20:
JWT Secret không được hard-code trực tiếp trong source code.

SR-21:
JWT phải có expiration.

---

## Audit Logging

SR-22:
Ghi nhận các security event quan trọng:

- REGISTER_SUCCESS
- LOGIN_PASSWORD_SUCCESS
- LOGIN_PASSWORD_FAILED
- OTP_CREATED
- OTP_VERIFY_FAILED
- OTP_EXPIRED
- OTP_VERIFY_SUCCESS
- OTP_RESEND

SR-23:
Audit log tuyệt đối không chứa OTP plaintext hoặc password.

---

## Input Validation

SR-24:
Validate tất cả request từ client.

SR-25:
Không tin tưởng dữ liệu client gửi lên.

---

## Error Handling

SR-26:
Không trả stack trace hoặc thông tin nội bộ nhạy cảm cho client.

---

## Secrets

SR-27:
Không commit:

- SMTP Password
- JWT Secret
- Database Password

vào source code.

Sử dụng configuration / environment variables / user secrets khi thích hợp.
