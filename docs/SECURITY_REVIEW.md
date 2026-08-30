# Security Review

## Scope

PHASE 13 đánh giá toàn bộ hệ thống xác thực từ đăng ký đến truy cập API bảo vệ:

`Register -> Password Login -> Pending Challenge -> Explicit Send OTP -> Sent Challenge -> OTP Verification -> Consume OTP -> JWT -> Protected API`

Phạm vi gồm source ASP.NET Core, Controller, DTO, Service, Entity Framework Core, migration SQL Server, cấu hình, logging, audit, JWT, rate limiting, SMTP, frontend tĩnh, test và tài liệu bảo mật. Baseline khi bắt đầu review có 73 file được Git theo dõi; việc rà soát bao phủ source, test, cấu hình và tài liệu liên quan. Review đã đối chiếu đầy đủ 46 nhóm kiểm tra kỹ thuật của yêu cầu PHASE 13, bao gồm authentication bypass, password, OTP, resend, concurrency, JWT, secrets, logging, validation, SQL injection, HTTP security và frontend.

Không thực hiện thay đổi kiến trúc lớn, không đổi SQL Server/Entity Framework Core và không đưa secret thật vào báo cáo.

## Method

- Đọc các tài liệu bắt buộc và đối chiếu implementation PHASE 1 đến PHASE 12 với `SECURITY_REQUIREMENTS.md`.
- Theo dấu từng nhánh thành công/thất bại của register, login, first send, verify OTP, resend OTP, JWT issuance và `/api/auth/me`.
- Kiểm tra DTO allowlist, validation, response model và khả năng mass assignment.
- Kiểm tra truy vấn EF Core, migration, index, foreign key, check constraint và tìm kiếm raw SQL có thể nhận dữ liệu client.
- Kiểm tra CSPRNG, OTP HMAC, expiration, attempts, revoke, consume, replay và cạnh tranh đồng thời.
- Kiểm tra cấu hình JWT, thuật toán, key separation, issuer, audience, lifetime và protected endpoint.
- Tìm kiếm password, OTP, hash, token, Authorization, connection string, SMTP credential, key và các mẫu secret trong file được theo dõi.
- Kiểm tra log, audit event, exception response, cache policy, HTTPS, CORS, security headers và rate-limit partition.
- Kiểm tra frontend bằng static analysis cho Web Storage, DOM sink, raw error rendering, CSP, form fallback và authentication state.
- Chạy dependency vulnerability review, regression test tự động và test concurrency SQL Server dạng opt-in khi môi trường phù hợp.
- Security review gốc đã có lần chạy 80/80. Sau split-flow refactor, full suite bật SQL opt-in đạt **105/105 pass, 0 failed, 0 skipped**, gồm đủ bốn test concurrency trên SQL Server thật.

## Findings Summary

| Severity | Count |
|---|---:|
| Critical | 0 |
| High | 2 |
| Medium | 6 |
| Low | 6 |
| Info | 2 |
| **Total** | **16** |

Tất cả finding `HIGH` đã được khắc phục. Các finding còn `OPEN` cần thay đổi thiết kế dùng chung hoặc migration mới nên được giữ lại làm remaining risk có chủ đích.

## Findings

### SEC-001

Severity: HIGH  
Category: Sensitive Data Exposure / Frontend Form Fallback  
Affected file: `src/OTPAuth.API/wwwroot/index.html`; `tests/OTPAuth.Tests/StaticUiTests.cs`  
Description: Các form register, login và verify OTP ban đầu không khai báo `method`/`action`. Nếu JavaScript không tải hoặc chưa chạy, trình duyệt mặc định submit bằng GET và có thể đưa password hoặc OTP vào query string.  
Impact: Credential có thể tồn tại trong URL, browser history, access log, reverse-proxy log hoặc telemetry.  
Attack scenario: HTML render thành công nhưng `app.js` bị lỗi/404; người dùng submit form và trình duyệt điều hướng với các field nhạy cảm trong URL.  
Remediation: Đặt `method="post"` cùng action API same-origin tương ứng cho cả ba form và khóa hành vi này bằng regression test.  
Status: FIXED

### SEC-002

Severity: HIGH  
Category: Vulnerable Dependency  
Affected file: `tests/OTPAuth.Tests/OTPAuth.Tests.csproj`; dependency graph của test project  
Description: Dependency transitive của test project kéo `System.Net.Http` 4.3.0 chịu ảnh hưởng bởi `GHSA-7jgj-8wvc-jh57` và `System.Text.RegularExpressions` 4.3.0 chịu ảnh hưởng bởi `GHSA-cmhx-cq75-c4mj`.  
Impact: Giữ package version có advisory làm dependency baseline không đạt yêu cầu và có thể gây rủi ro nếu asset runtime bị dùng ngoài dự kiến.  
Attack scenario: Một test/tooling path nạp transitive runtime asset dễ tổn thương hoặc dependency này được tái sử dụng nhầm trong code chạy thực tế.  
Remediation: Pin test-only `System.Net.Http` 4.3.4 và `System.Text.RegularExpressions` 4.3.1 với `PrivateAssets=all` và `ExcludeAssets=all`, sau đó chạy lại vulnerability scan.  
Status: FIXED

### SEC-003

Severity: MEDIUM  
Category: Exception Handling / Information Disclosure  
Affected file: `src/OTPAuth.API/Program.cs`; `src/OTPAuth.API/Services/AuthService.cs`; `src/OTPAuth.API/appsettings.json`; `tests/OTPAuth.Tests/SecurityApiTests.cs`  
Description: Developer exception page trước đây có thể trả raw SQL exception, stack trace, absolute path, implementation type và request headers khi một lỗi ngoài dự kiến thoát khỏi service.  
Impact: Client biết chi tiết công nghệ, cấu trúc code và metadata môi trường; diagnostic data có thể bị lưu tiếp trong client/proxy telemetry.  
Attack scenario: Database tạm thời không truy cập được trong lúc ứng dụng chạy Development và attacker gọi một endpoint thực hiện query.  
Remediation: Dùng custom global exception boundary cho mọi environment, chỉ log exception type + trace ID, tắt raw EF Core provider logging trong bản demo, trả Problem Details allowlist với `INTERNAL_ERROR` và `traceId`, đặt no-store, không serialize/log raw exception/header; thêm injected-failure regression test kiểm tra cả response và captured log.  
Status: FIXED

### SEC-004

Severity: MEDIUM  
Category: Rate Limiting / Resource Exhaustion  
Affected file: `src/OTPAuth.API/Configuration/AuthenticationRateLimitOptions.cs`; `src/OTPAuth.API/Controllers/AuthController.cs`; `src/OTPAuth.API/Program.cs`; `src/OTPAuth.API/appsettings.json`; `tests/OTPAuth.Tests/RateLimitingTests.cs`  
Description: Endpoint register ban đầu không có rate limit riêng, trong khi mỗi request hợp lệ có thể chạy password hashing và ghi User/AuditLog.  
Impact: Anonymous client có thể gây CPU, database và storage pressure bằng nhiều email duy nhất.  
Attack scenario: Bot gửi liên tục registration hợp lệ với email ngẫu nhiên để vượt qua duplicate check.  
Remediation: Thêm fixed-window policy riêng cho register, 5 request/giờ/IP, dùng server-observed remote IP và trả `429`/`Retry-After` an toàn.  
Status: FIXED

### SEC-005

Severity: MEDIUM  
Category: Distributed Rate Limiting / OTP Issuance Abuse  
Affected file: `src/OTPAuth.API/Program.cs`; `src/OTPAuth.API/Services/AuthService.cs`; thiết kế quota trong `docs/REQUIREMENTS.md`  
Description: Hệ thống đã tách first send khỏi login và có policy `send-otp` riêng 3/300 giây/IP, nhưng chưa có quota theo normalized email/User và chưa có bộ đếm phát OTP dùng chung giữa first send với resend. Resend count chỉ giới hạn trong một authentication flow.
Impact: Botnet hoặc nhiều IP có credential đúng có thể gây email spam, tạo nhiều challenge/audit row và tăng tải SMTP/database.  
Attack scenario: Attacker dùng credential bị rò rỉ, luân phiên IP, tạo pending flow mới rồi gọi first send để vượt giới hạn theo từng IP/flow.
Remediation: Thiết kế quota nguyên tử theo normalized email/User và một issuance budget dùng chung cho first send + resend. Nếu chạy nhiều instance, bộ đếm phải dùng shared store hoặc database transaction phù hợp; không tự thêm kiến trúc phân tán khi chưa được phê duyệt.
Status: OPEN

### SEC-006

Severity: MEDIUM  
Category: Input Size Validation / Denial of Service  
Affected file: `src/OTPAuth.API/Program.cs`; `src/OTPAuth.API/Controllers/AuthController.cs`; `tests/OTPAuth.Tests/SecurityApiTests.cs`  
Description: Data annotation chỉ từ chối chuỗi sau khi request đã được nhận/parse; trước remediation không có giới hạn body nhỏ dành riêng cho auth API.  
Impact: Nhiều JSON body lớn có thể gây network, memory và parser pressure trước business validation.  
Attack scenario: Anonymous clients gửi đồng thời payload rất lớn tới các endpoint POST `/api/auth/*`.  
Remediation: Giới hạn auth request body ở 16 KiB, từ chối sớm khi `Content-Length` vượt ngưỡng, ánh xạ cả lỗi body-size của Kestrel/chunked request thành generic `413 REQUEST_TOO_LARGE`, và thêm regression tests.  
Status: FIXED

### SEC-007

Severity: MEDIUM  
Category: OTP Concurrency / Attempt Accounting  
Affected file: `src/OTPAuth.API/Services/AuthService.cs`; `tests/OTPAuth.Tests/SqlServerConcurrencyTests.cs`; `tests/OTPAuth.Tests/OtpVerificationTests.cs`  
Description: Số lần retry concurrency ban đầu thấp có thể khiến một số request OTP sai kết thúc sau nhiều conflict mà không commit lần tăng `AttemptCount`, làm hard limit bị under-account dưới tải cạnh tranh.  
Impact: Attacker gửi nhiều verify đồng thời có thể nhận thêm cơ hội đoán OTP so với ý định 5 lần/challenge.  
Attack scenario: Nhiều request dùng cùng challenge và OTP sai cùng đọc một RowVersion rồi cạnh tranh update.  
Remediation: Tăng retry budget lên 6 để bao phủ tối đa năm lần sai và trạng thái terminal, luôn reload/re-evaluate sau conflict, đồng thời thêm test concurrency trên SQL Server thật.  
Status: FIXED

### SEC-008

Severity: MEDIUM  
Category: OTP Expiration / Time-of-check Time-of-use  
Affected file: `src/OTPAuth.API/Services/AuthService.cs`; `tests/OTPAuth.Tests/OtpVerificationTests.cs`  
Description: `now` trước đây được lấy một lần trước retry loop. OTP có thể hết hạn trong lúc hash/compare hoặc sau concurrency retry nhưng vẫn dùng timestamp cũ để consume.  
Impact: OTP có khả năng được chấp nhận sau ranh giới `ExpiresAt`/`FlowExpiresAt`.  
Attack scenario: Request bắt đầu ngay trước expiry, bị trì hoãn hoặc concurrency conflict rồi tiếp tục với thời gian stale.  
Remediation: Làm mới UTC time ở mỗi retry và ngay sau OTP comparison, kiểm tra expiry lần cuối trước mọi mutation/consume; thêm deterministic time-sequence test.  
Status: FIXED

### SEC-009

Severity: LOW  
Category: HTTP Security Headers / Cache / HTTPS  
Affected file: `src/OTPAuth.API/Program.cs`; `tests/OTPAuth.Tests/SecurityApiTests.cs`; `tests/OTPAuth.Tests/StaticUiTests.cs`  
Description: Auth responses ban đầu không đồng nhất `Cache-Control: no-store`; frontend chỉ có meta CSP và thiếu framing, MIME, referrer, permissions headers cùng HSTS ngoài development.  
Impact: Sensitive response có thể bị cache ngoài dự kiến và UI thiếu defense-in-depth trước clickjacking/MIME sniffing/HTTPS downgrade.  
Attack scenario: Browser/proxy lưu profile/error response hoặc một site khác frame UI để thực hiện UI redress.  
Remediation: Thêm no-store/no-cache cho `/api/auth`, CSP header với `frame-ancestors 'none'` và `object-src 'none'`, `X-Frame-Options: DENY`, `nosniff`, referrer/permissions policy và HSTS ngoài Development; thêm header assertions.  
Status: FIXED

### SEC-010

Severity: LOW  
Category: OTP Delivery State / Expiration  
Affected file: `src/OTPAuth.API/Services/AuthService.cs`; `tests/OTPAuth.Tests/EmailDeliveryTests.cs`  
Description: Login flow cũ từng thực hiện SMTP trong chính `/login` và không reload/recheck challenge sau delivery. Split-flow hiện tại đã chuyển SMTP sang `/send-otp`, dùng prepared/sent state và chỉ set `SentAt` sau delivery success.
Impact: Login có thể trả challenge thành công dù OTP đã hết hạn hoặc challenge không còn usable trong lúc gửi email.  
Attack scenario: SMTP chậm tới sát/quá TTL hoặc một request cạnh tranh revoke challenge trong lúc delivery đang diễn ra.  
Remediation: Tách pending challenge khỏi delivery, finalize `SentAt`/sent state chỉ sau SMTP success; nếu delivery/finalize thất bại thì fail closed/revoke, audit failure và không trả success.
Status: FIXED

### SEC-011

Severity: LOW
Category: Database Defense in Depth / Constraints
Affected file: `src/OTPAuth.API/Data/AppDbContext.cs`; migration `SupportPendingOtpChallenge`; `docs/DATABASE_DESIGN.md`
Description: Migration mới đã thêm state/expiration/consumed constraints cốt lõi và backfill `SentAt`, nhưng một số lifecycle invariant vẫn chỉ được service enforce, gồm flow TTL tối đa, max-attempt phải đi cùng revoke và mọi tổ hợp consumed/revoked.
Impact: Bug tương lai hoặc write ngoài ứng dụng có thể tạo row trạng thái không hợp lệ dù request path hiện tại vẫn kiểm tra an toàn.
Attack scenario: Một maintenance script hoặc code path có quyền DB ghi trực tiếp challenge với các field lifecycle mâu thuẫn.
Remediation: Giữ các constraint mới; nếu bổ sung phần còn lại, kiểm tra dữ liệu hiện hữu và tạo migration riêng, không sửa migration đã apply.
Status: OPEN

### SEC-012

Severity: LOW  
Category: Account Enumeration  
Affected file: `src/OTPAuth.API/Controllers/AuthController.cs`; `src/OTPAuth.API/Services/AuthService.cs`; `docs/API_SPEC.md`  
Description: Register trả distinct `409 EMAIL_ALREADY_REGISTERED` khi normalized email đã tồn tại.  
Impact: Attacker có thể kiểm tra một email có tài khoản hay không để hỗ trợ phishing hoặc credential stuffing.  
Attack scenario: Gửi registration cho danh sách email mục tiêu và phân biệt `409` với `201`.  
Remediation: Bản demo giữ response rõ cho UX/trình bày và giảm abuse bằng register limiter. Khi triển khai thực tế có yêu cầu riêng tư cao hơn, đổi sang response chung hoặc registration-confirmation flow.  
Status: ACCEPTED

### SEC-013

Severity: LOW  
Category: JWT Hardening  
Affected file: `src/OTPAuth.API/Program.cs`; `src/OTPAuth.API/Services/JwtTokenService.cs`; `tests/OTPAuth.Tests/SecurityApiTests.cs`  
Description: JWT đã ký HS256 nhưng validation trước đây chưa pin explicit algorithm/RequireSignedTokens, expiration configuration cho phép giá trị tùy ý và startup chưa từ chối việc tái dùng cùng key cho OTP HMAC với JWT.  
Impact: Configuration drift làm giảm key separation hoặc cho phép token lifetime dài hơn thiết kế; explicit algorithm pinning thiếu defense-in-depth.  
Attack scenario: Operator cấu hình nhầm hai secret giống nhau, kéo dài lifetime hoặc một token dùng thuật toán ngoài profile thiết kế được đưa vào validator.  
Remediation: Pin HS256, yêu cầu signed token, giữ validation issuer/audience/lifetime/signing key, bắt buộc lifetime 15 phút và từ chối startup nếu OTP/JWT keys giống nhau bằng fixed-time comparison.  
Status: FIXED

### SEC-014

Severity: LOW  
Category: Email Failure Compensation / Residual Active Challenge  
Affected file: `src/OTPAuth.API/Services/AuthService.cs`; kiến trúc SMTP + SQL Server không có distributed transaction  
Description: Revoke sau SMTP failure là best-effort; process crash hoặc database conflict lặp lại có thể để challenge open cho tới khi hết hạn.  
Impact: Một challenge mà client nhận failure có thể còn tồn tại ngắn hạn, dù attacker vẫn cần OTP plaintext đã gửi tới mailbox.  
Attack scenario: SMTP provider nhận message rồi process chết trước compensation hoặc compensation liên tục lỗi.  
Remediation: Chấp nhận trong demo vì transaction không được giữ qua network; giảm thiểu bằng challenge ID opaque, TTL/flow expiry, max attempts, rate limit và post-delivery recheck. Production có thể dùng durable outbox/delivery state trong một thiết kế riêng.  
Status: ACCEPTED

### SEC-015

Severity: INFO  
Category: Security Test Coverage / Real SQL Concurrency  
Affected file: `tests/OTPAuth.Tests/SqlServerConcurrencyTests.cs`; test configuration qua environment/User Secrets  
Description: Test EF InMemory trước đây không chứng minh RowVersion, filtered unique index và concurrent consume/attempt behavior của SQL Server thật.  
Impact: Race condition đặc thù provider có thể không được phát hiện trong test suite thông thường.  
Attack scenario: Hai hay nhiều verify request cùng challenge chạy song song trên SQL Server và hành vi khác giả lập InMemory.  
Remediation: Bổ sung integration tests SQL Server dạng opt-in, lấy connection string ngoài source, dùng record có định danh ngẫu nhiên và cleanup đúng phạm vi trong `finally`; đã thực thi các scenario concurrent consume/wrong-attempt accounting trên SQL Server thật.  
Status: FIXED

### SEC-016

Severity: INFO  
Category: Frontend Token Storage / XSS Trade-off  
Affected file: `src/OTPAuth.API/wwwroot/app.js`; `src/OTPAuth.API/wwwroot/index.html`; `tests/OTPAuth.Tests/StaticUiTests.cs`  
Description: JWT demo được lưu trong `sessionStorage`, vì vậy JavaScript cùng origin có thể đọc token nếu XSS xảy ra.  
Impact: Một XSS thành công có thể lấy bearer token và dùng tới khi token hết hạn.  
Attack scenario: Một DOM/server-rendered injection tương lai chạy script trong origin của ứng dụng rồi đọc `otpAuth.accessToken`.  
Remediation: Chấp nhận cho frontend demo same-origin: CSP chặt, local assets, `textContent`, không raw HTML/error, không console log, JWT TTL ngắn và logout xóa session. Nếu nâng cấp production, đánh giá HttpOnly/Secure/SameSite cookie cùng CSRF control tương ứng.  
Status: ACCEPTED

## Security Controls Verified

- Password dùng ASP.NET Core `IPasswordHasher<User>`; không có password plaintext trong entity/database/response/log.
- Register chuẩn hóa email, có unique database index và xử lý race duplicate an toàn.
- Login dùng generic error và dummy password hash; password đúng chỉ tạo pending challenge, không sinh/gửi OTP và không cấp JWT.
- First send chỉ nhận challenge ID, lấy email từ server state, có policy IP riêng và không thể gọi lặp để né resend cooldown.
- OTP gồm đúng 6 chữ số, giữ leading zero và dùng `RandomNumberGenerator`.
- Database chỉ lưu HMAC-SHA-256 của OTP với key ngoài source; comparison dùng fixed-time API.
- OTP enforce TTL 3 phút, flow TTL 10 phút, tối đa 5 lần sai, revoke, consume và single-use/replay protection.
- Resend chỉ nhận opaque challenge ID, dùng server time, cooldown 60 giây, tối đa 3 lần/flow, revoke mã cũ và reset attempt cho mã mới.
- JWT chỉ được tạo sau khi `ConsumedAt` commit; token có claim tối thiểu, HS256, issuer, audience, signing-key và lifetime validation.
- `/api/auth/me` có `[Authorize]`, parse claim `sub`, recheck active User và chỉ trả profile tối thiểu.
- Rate limiter dùng remote IP do server quan sát, không tin trực tiếp `X-Forwarded-For`; register, login, send, verify và resend có policy riêng.
- Controller bind request DTO allowlist, không bind Entity; client không thể set UserId, security state, expiry, attempt hoặc revoke fields.
- Data access dùng EF Core LINQ/parameterized query; không phát hiện user-controlled raw SQL hoặc SQL injection path.
- API error dùng generic Problem Details; không trả stack, SQL/SMTP detail, connection string, local path hoặc secret.
- Audit log dùng event/reason allowlist và field có độ dài giới hạn; không có password, OTP, hash, JWT hoặc Authorization field.
- Email service dùng SMTP TLS, configuration ngoài source, masked recipient logging và không log OTP/credential/provider exception.
- Không có CORS mở rộng; frontend và API cùng origin. Bearer token được gắn bằng JavaScript, không dùng authentication cookie nên protected flow không mang CSRF risk tự động kiểu cookie.
- Frontend không dùng `innerHTML`, `eval`, `document.write`, raw error rendering, `localStorage` hoặc console logging dữ liệu nhạy cảm.
- Password/OTP không vào Web Storage; challenge ID chỉ giữ trong memory; JWT chỉ giữ trong session của tab.
- Static assets nằm trong `wwwroot`, không chứa server configuration, signing key, OTP key, SMTP credential hoặc connection string.
- Secret scan không phát hiện credential thật trong tracked source/config/docs/tests; test keys là dữ liệu test-only.
- Dependency vulnerability check đã được chạy lại sau remediation của SEC-002.
- Regression coverage có wrong/expired/replayed/revoked/consumed OTP, max attempts, resend old OTP, JWT trước OTP, protected API, rate limit, exception sanitization, headers, body size và concurrency SQL Server opt-in.

## Remaining Risks

- **SEC-005 — OPEN:** chưa có quota normalized-email/User và issuance budget dùng chung cho first send + resend; thiết kế nhiều instance cần shared state có chủ đích.
- **SEC-011 — OPEN:** database chưa encode toàn bộ lifecycle invariant bằng CHECK constraint; cần migration riêng sau khi đánh giá dữ liệu hiện hữu.
- **SEC-012 — ACCEPTED:** register `409` tiết lộ email tồn tại để đổi lấy UX demo rõ ràng; register limiter chỉ giảm abuse, không loại bỏ enumeration.
- **SEC-014 — ACCEPTED:** SMTP và SQL không atomic; compensation failure hiếm vẫn có thể để challenge open tới TTL.
- **SEC-016 — ACCEPTED:** `sessionStorage` phù hợp demo nhưng bearer token vẫn chịu XSS risk; không coi đây là mô hình token storage production mặc định.
- Rate limiter in-memory chỉ phù hợp một application instance và có thể bị botnet phân tán vượt qua.
- Logout hiện chỉ xóa JWT phía client; token đã sao chép vẫn dùng được tới khi hết hạn vì chưa có server-side revocation.
- Audit/challenge retention, key rotation và database backup/least-privilege vẫn cần chính sách vận hành cụ thể trước deployment thực tế.
