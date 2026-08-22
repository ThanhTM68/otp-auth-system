# AGENTS.md

# Project

OTP Authentication System

Đây là bài tập lớn môn An toàn và Bảo mật thông tin.

Codex phải ưu tiên:

1. Correctness
2. Security
3. Code readability
4. Testability
5. Simplicity

Không tối ưu quá mức hoặc tự thêm kiến trúc phức tạp.

---

# Required Reading

Trước khi thực hiện bất kỳ task nào phải đọc:

- PROJECT_BRIEF.md
- SECURITY_REQUIREMENTS.md
- TASKS.md

Nếu đã tồn tại thì đọc thêm:

- docs/REQUIREMENTS.md
- docs/ARCHITECTURE.md
- docs/DATABASE_DESIGN.md
- docs/API_SPEC.md

---

# Development Rule

KHÔNG được tự động làm toàn bộ project trong một lần.

Chỉ thực hiện Phase mà người dùng yêu cầu.

Sau khi hoàn thành Phase phải DỪNG.

Không tự động chuyển sang Phase tiếp theo.

---

# Workflow For Every Phase

Trước khi code:

1. Đọc tài liệu liên quan.
2. Kiểm tra source code hiện tại.
3. Xác định file cần thay đổi.
4. Không sửa những module không liên quan.

Sau khi code:

1. Build project.
2. Chạy relevant tests.
3. Sửa lỗi do thay đổi vừa tạo.
4. Kiểm tra security requirement.
5. Chạy `git diff` để kiểm tra thay đổi.
6. Báo cáo file đã tạo.
7. Báo cáo file đã sửa.
8. Báo cáo kết quả build và test.
9. Đề xuất commit message phù hợp với phase hiện tại.
10. KHÔNG tự động chạy `git commit`.
11. KHÔNG tự động chạy `git push`.
12. Chờ người dùng kiểm tra và commit thủ công.
13. DỪNG, không tự động chuyển sang phase tiếp theo.

---

# Security Rules

Không bao giờ:

- lưu password plaintext
- lưu OTP plaintext lâu dài
- log password
- log OTP
- hard-code secret
- bỏ authentication để test chạy được
- bỏ security control để fix bug
- cấp JWT trước khi OTP verify thành công

OTP phải:

- expire
- single use
- chống replay
- giới hạn số lần thử
- hỗ trợ resend an toàn

---

# Coding Rules

Ưu tiên code đơn giản và dễ trình bày cho sinh viên.

Không tự thêm:

- CQRS
- Event Sourcing
- Microservices
- Kafka
- Redis
- Docker
- Kubernetes

trừ khi người dùng yêu cầu.

---

# Database

Mọi thay đổi schema phải thực hiện thông qua Entity Framework Core Migration.

Không tự xóa database nếu không được yêu cầu.

---

# Tests

Các security function quan trọng phải có Unit Test.

Đặc biệt phải test:

- OTP đúng
- OTP sai
- OTP hết hạn
- OTP đã sử dụng
- OTP replay
- vượt max attempts
- resend vô hiệu OTP cũ
- password sai không được tạo OTP

---

# Documentation

Khi kiến trúc hoặc API thay đổi phải cập nhật tài liệu tương ứng.

---

# Git Rules

Git được sử dụng để lưu lại tiến trình phát triển theo từng phase.

Mỗi phase hoàn chỉnh nên tương ứng với một commit rõ ràng.

Codex được phép:

- chạy `git status`
- chạy `git diff`
- chạy `git diff --stat`
- kiểm tra các file thay đổi
- đề xuất commit message

Codex KHÔNG được tự động:

- `git add`
- `git commit`
- `git push`
- `git reset --hard`
- `git clean`
- force push
- xóa branch

trừ khi người dùng yêu cầu rõ ràng.

Trước khi kết thúc mỗi phase, Codex phải báo:

1. Git status.
2. File mới.
3. File đã sửa.
4. File đã xóa nếu có.
5. Kết quả build.
6. Kết quả test.
7. Commit message đề xuất.

Ví dụ commit message:

PHASE 0:
`docs: analyze OTP authentication requirements`

PHASE 1:
`chore: initialize ASP.NET Core solution`

PHASE 2:
`feat: add database entities and EF Core context`

PHASE 3:
`feat: implement user registration`

PHASE 4:
`feat: implement password login and OTP challenge`

PHASE 5:
`feat: implement secure OTP core`

PHASE 6:
`feat: add email OTP delivery`

PHASE 7:
`feat: implement OTP verification and JWT`

PHASE 8:
`feat: add secure OTP resend`

PHASE 9:
`security: add authentication rate limiting`

PHASE 10:
`security: add authentication audit logging`

PHASE 11:
`test: add OTP security test cases`

PHASE 12:
`feat: add OTP authentication demo UI`

PHASE 13:
`security: harden OTP authentication system`

PHASE 14:
`docs: complete OTP authentication documentation`

PHASE 15:
`chore: finalize OTP authentication project`

---

# Final Rule

Nếu task hiện tại đã hoàn thành:

STOP.

Không tự động làm task tiếp theo.
