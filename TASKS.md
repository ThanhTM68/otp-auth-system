# OTP AUTHENTICATION PROJECT TASKS

## PHASE 0 - Analysis

- [x] Đọc PROJECT_BRIEF.md
- [x] Đọc SECURITY_REQUIREMENTS.md
- [x] Phân tích yêu cầu
- [x] Xác định Actor
- [x] Xác định Use Case
- [x] Thiết kế authentication flow
- [x] Thiết kế OTP flow
- [x] Thiết kế kiến trúc
- [x] Thiết kế database
- [x] Thiết kế API
- [x] Tạo docs/REQUIREMENTS.md
- [x] Tạo docs/ARCHITECTURE.md
- [x] Tạo docs/DATABASE_DESIGN.md
- [x] Tạo docs/API_SPEC.md

---

## PHASE 1 - Project Initialization

- [x] Tạo .NET Solution
- [x] Tạo ASP.NET Core Web API
- [x] Tạo xUnit Test Project
- [x] Cài Entity Framework Core
- [x] Cấu hình SQL Server
- [x] Cấu hình Swagger
- [x] Tạo cấu trúc thư mục
- [x] Build thành công
- [x] Test project chạy được

---

## PHASE 2 - Database

- [ ] User entity
- [ ] OtpChallenge entity
- [ ] AuditLog entity
- [ ] DbContext
- [ ] Entity configuration
- [ ] Migration
- [ ] Database initialization

---

## PHASE 3 - Registration

- [ ] RegisterRequest
- [ ] Validation
- [ ] Password hashing
- [ ] Check duplicate email
- [ ] Register API
- [ ] Unit Test

---

## PHASE 4 - Password Login

- [ ] LoginRequest
- [ ] Verify email
- [ ] Verify password
- [ ] Không cấp JWT ở bước này
- [ ] Create OTP challenge
- [ ] Unit Test

---

## PHASE 5 - OTP Core

- [ ] Secure OTP generator
- [ ] OTP hashing
- [ ] OTP expiration
- [ ] AttemptCount
- [ ] MaxAttempts
- [ ] ConsumedAt
- [ ] IsRevoked
- [ ] Unit Test

---

## PHASE 6 - Email OTP

- [ ] EmailService
- [ ] SMTP Configuration
- [ ] Email template
- [ ] Không log OTP
- [ ] Development safe mode nếu cần

---

## PHASE 7 - OTP Verification

- [ ] Verify OTP API
- [ ] Check Challenge
- [ ] Check expiration
- [ ] Check attempts
- [ ] Check consumed
- [ ] Check revoked
- [ ] Verify OTP hash
- [ ] Consume OTP
- [ ] Generate JWT
- [ ] Unit Test

---

## PHASE 8 - Resend OTP

- [ ] Resend API
- [ ] Resend cooldown
- [ ] Revoke previous OTP
- [ ] Generate new OTP
- [ ] Send email
- [ ] Unit Test

---

## PHASE 9 - Rate Limiting

- [ ] Login rate limit
- [ ] OTP verify rate limit
- [ ] OTP resend rate limit

---

## PHASE 10 - Audit Logging

- [ ] Authentication audit
- [ ] OTP audit
- [ ] Login failed audit
- [ ] Verify failed audit
- [ ] Không lưu secret/OTP/password

---

## PHASE 11 - Security Tests

- [ ] Wrong OTP
- [ ] Expired OTP
- [ ] Replay OTP
- [ ] Brute force
- [ ] Max attempts
- [ ] Old OTP after resend
- [ ] JWT before OTP
- [ ] Input validation

---

## PHASE 12 - UI Demo

- [ ] Register Page
- [ ] Login Page
- [ ] OTP Page
- [ ] Dashboard
- [ ] Resend timer
- [ ] Error messages

---

## PHASE 13 - Security Review

- [ ] Review Password Security
- [ ] Review OTP Security
- [ ] Review JWT
- [ ] Review Secrets
- [ ] Review Logging
- [ ] Review SQL Injection
- [ ] Review Validation
- [ ] Review Rate Limiting

---

## PHASE 14 - Documentation

- [ ] README
- [ ] Setup guide
- [ ] API documentation
- [ ] Database diagram
- [ ] Authentication sequence
- [ ] OTP sequence
- [ ] Security analysis
- [ ] Attack scenarios
- [ ] Testing report

---

## PHASE 15 - Final Verification

- [ ] Clean build
- [ ] All tests pass
- [ ] API works
- [ ] Database works
- [ ] Email works
- [ ] OTP works
- [ ] JWT works
- [ ] Replay prevented
- [ ] Brute-force mitigated
- [ ] Demo scenario ready
